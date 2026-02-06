using System;
using System.Collections;
using UnityEngine;

namespace Game.Combat
{
    /// <summary>
    /// Vida genérica con:
    /// - Knockback al recibir daño (robusto aunque esté congelado por states)
    /// - Blink/Flash (SpriteRenderer) durante un tiempo
    /// - (Opcional) Animator flash
    /// - Implementa IDamageable
    /// - (Opcional) Emite evento de "Enemy killed" para recompensas (mana +1, etc.)
    /// </summary>
    public class Health : MonoBehaviour, IDamageable
    {
        [Header("HP")]
        [SerializeField, Min(1)] private int maxHP = 3;
        public int CurrentHP { get; private set; }
        public int MaxHP => maxHP;

        [Header("Feedback - Knockback")]
        [SerializeField] private float knockbackSpeedX = 8f;   // seco
        [SerializeField] private float knockbackSpeedY = 1.5f; // un pelín arriba

        [Header("Knockback - Force Unfreeze (si estaba congelado)")]
        [Tooltip("Si algún state congela al enemy (FreezeAll / Kinematic), esto lo libera para que se vea el knockback.")]
        [SerializeField] private bool forceUnfreezeForKnockback = true;

        [Tooltip("Tiempo mínimo (seg) que dejamos libre antes de restaurar constraints/bodytype.")]
        [SerializeField, Range(0.01f, 0.2f)] private float unfreezeWindow = 0.06f;

        [Header("Feedback - Blink (SpriteRenderer)")]
        [SerializeField] private bool blinkOnHit = true;
        [SerializeField, Range(0.02f, 0.25f)] private float blinkInterval = 0.06f;
        [SerializeField, Range(0.02f, 1.5f)] private float blinkDuration = 0.30f;

        [Tooltip("Si está vacío, auto-busca SpriteRenderers en hijos.")]
        [SerializeField] private SpriteRenderer[] blinkRenderers;

        [Header("Feedback - Animator Flash (opcional)")]
        [Tooltip("Si ON, dispara parámetros en Animator además del blink.")]
        [SerializeField] private bool useAnimatorFlash = false;
        [SerializeField] private string flashBoolName = "Flash";
        [SerializeField] private string flashTriggerName = "FlashTrigger";

        [Header("Rewards (opcional)")]
        [Tooltip("Si asignas este channel, al morir se emitirá para que sistemas (mana +1 por kill, etc.) reaccionen.")]
        [SerializeField] private Game.Events.EnemyKilledEventChannelSO enemyKilledChannel;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;

        public event Action<Health> OnDied; // opcional (para quien quiera escuchar sin SO)

        private Rigidbody2D rb2d;
        private Animator anim;
        private Coroutine blinkCo;
        private Coroutine restoreRBco;

        // Cache para restaurar si “descongelamos” temporalmente
        private RigidbodyType2D cachedBodyType;
        private RigidbodyConstraints2D cachedConstraints;
        private float cachedGravity;
        private bool cached;

        private bool dead;

        private void Awake()
        {
            CurrentHP = Mathf.Max(1, maxHP);
            rb2d = GetComponent<Rigidbody2D>();
            anim = GetComponent<Animator>();

            // Auto-assign renderers si no hay lista
            if (blinkRenderers == null || blinkRenderers.Length == 0)
            {
                var sr = GetComponent<SpriteRenderer>();
                if (sr != null) blinkRenderers = new[] { sr };
                else blinkRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            }
        }

        // ---------------------------------
        // Compatibilidad: IDamageable
        // ---------------------------------
        public void TakeDamage(int dmg, Vector2 sourcePosition)
        {
            TakeDamage(dmg, (Vector3)sourcePosition);
        }

        // Firma que ya usáis desde PlayerController (Vector3 attackerPosition)
        public void TakeDamage(int dmg, Vector3 attackerPosition)
        {
            if (dead) return;
            if (dmg <= 0) return;

            int before = CurrentHP;
            CurrentHP -= dmg;
            if (CurrentHP < 0) CurrentHP = 0;

            if (debugLogs)
                Debug.Log($"[HIT] {name} {before}->{CurrentHP}/{maxHP} (-{dmg}) from={attackerPosition}");

            ApplyKnockback(attackerPosition);

            if (blinkOnHit)
                StartBlink(blinkDuration);

            if (useAnimatorFlash && anim != null)
            {
                if (!string.IsNullOrEmpty(flashBoolName))
                    anim.SetBool(flashBoolName, true);

                if (!string.IsNullOrEmpty(flashTriggerName))
                    anim.SetTrigger(flashTriggerName);
            }

            if (CurrentHP <= 0)
                Die();
        }

        /// <summary>
        /// Para Animation Event: llamad a EndFlash() al final del clip flash si usáis Animator.
        /// </summary>
        public void EndFlash()
        {
            if (anim == null) return;
            if (!string.IsNullOrEmpty(flashBoolName))
                anim.SetBool(flashBoolName, false);
        }

        public void SetMaxHP(int newMax, bool refill = true)
        {
            maxHP = Mathf.Max(1, newMax);
            if (refill)
                CurrentHP = maxHP;
            else
                CurrentHP = Mathf.Clamp(CurrentHP, 0, maxHP);
        }

        public void Heal(int amount)
        {
            if (dead) return;
            if (amount <= 0) return;
            int before = CurrentHP;
            CurrentHP = Mathf.Clamp(CurrentHP + amount, 0, maxHP);
            if (debugLogs && CurrentHP != before)
                Debug.Log($"[HEAL] {name} {before}->{CurrentHP}/{maxHP} (+{amount})");
        }

        private void ApplyKnockback(Vector3 attackerPosition)
        {
            if (rb2d == null) return;

            float dirX = Mathf.Sign(transform.position.x - attackerPosition.x);
            if (dirX == 0f) dirX = 1f;

            // ✅ Si está congelado/kinematic por algún state, lo liberamos un momento para que el knockback se vea
            if (forceUnfreezeForKnockback)
            {
                CacheRBIfNeeded();
                rb2d.bodyType = RigidbodyType2D.Dynamic;
                rb2d.constraints = RigidbodyConstraints2D.FreezeRotation;
            }

            rb2d.velocity = new Vector2(dirX * knockbackSpeedX, knockbackSpeedY);

            if (debugLogs)
                Debug.Log($"[KB] {name} dirX={dirX} vel=({rb2d.velocity.x:0.00},{rb2d.velocity.y:0.00})");

            // Restaurar constraints/bodytype si tocamos algo
            if (forceUnfreezeForKnockback)
            {
                if (restoreRBco != null) StopCoroutine(restoreRBco);
                restoreRBco = StartCoroutine(RestoreRBRoutine(unfreezeWindow));
            }
        }

        private void CacheRBIfNeeded()
        {
            if (cached || rb2d == null) return;
            cachedBodyType = rb2d.bodyType;
            cachedConstraints = rb2d.constraints;
            cachedGravity = rb2d.gravityScale;
            cached = true;
        }

        private IEnumerator RestoreRBRoutine(float delay)
        {
            yield return new WaitForSeconds(delay);

            // Puede haber muerto/destruido durante la ventana
            if (dead) yield break;

            if (rb2d != null && cached)
            {
                rb2d.bodyType = cachedBodyType;
                rb2d.constraints = cachedConstraints;
                rb2d.gravityScale = cachedGravity;
            }

            restoreRBco = null;
        }

        private void StartBlink(float duration)
        {
            if (blinkRenderers == null || blinkRenderers.Length == 0) return;

            if (blinkCo != null) StopCoroutine(blinkCo);
            blinkCo = StartCoroutine(BlinkRoutine(duration));
        }

        private IEnumerator BlinkRoutine(float duration)
        {
            float t = 0f;
            bool visible = true;
            float interval = Mathf.Max(0.02f, blinkInterval);

            while (t < duration)
            {
                visible = !visible;
                SetVisible(visible);
                yield return new WaitForSeconds(interval);
                t += interval;
            }

            SetVisible(true);
            blinkCo = null;
        }

        private void SetVisible(bool visible)
        {
            for (int i = 0; i < blinkRenderers.Length; i++)
            {
                var r = blinkRenderers[i];
                if (r != null) r.enabled = visible;
            }
        }

        private void Die()
        {
            if (dead) return;
            dead = true;

            if (debugLogs) Debug.Log($"[DEAD] {name}");

            // Deja el sprite visible por si estaba blinkando
            SetVisible(true);
            if (blinkCo != null) StopCoroutine(blinkCo);
            if (restoreRBco != null) StopCoroutine(restoreRBco);

            // ✅ Recompensas por kill (mana +1, etc.)
            if (enemyKilledChannel != null)
                enemyKilledChannel.Raise(gameObject);

            OnDied?.Invoke(this);

            Destroy(gameObject);
        }
    }
}

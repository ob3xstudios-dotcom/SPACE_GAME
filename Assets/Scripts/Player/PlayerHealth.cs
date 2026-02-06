using System;
using System.Collections;
using UnityEngine;
using Game.Combat;

namespace Game.Player
{
    // ✅ Implementa la interfaz REAL: Game.Combat.IDamageable (TakeDamage(int, Vector2))
    public class PlayerHealth : MonoBehaviour, IDamageable
    {
        [Header("Refs")]
        [SerializeField] private PlayerResources playerResources;

        [Header("Damage / i-frames")]
        [SerializeField, Range(0f, 3f)] private float iFrames = 0.8f;
        [SerializeField] private bool invulnerableOnSpawn = false;
        [SerializeField, Range(0f, 3f)] private float spawnIFrames = 0.5f;

        [Header("Hit Reaction (Knockback)")]
        [Tooltip("Fuerza horizontal del knockback (se aplica alejándose de sourcePosition).")]
        [SerializeField, Range(0f, 50f)] private float knockbackX = 10f;
        [Tooltip("Impulso vertical del knockback.")]
        [SerializeField, Range(0f, 50f)] private float knockbackY = 6f;
        [Tooltip("Tiempo de 'hitstun' (bloquea movimiento si hay PlayerController). 0 = no bloquea.")]
        [SerializeField, Range(0f, 1f)] private float hitStunTime = 0.12f;

        [Header("Flash / Blink (durante i-frames)")]
        [SerializeField] private bool blinkOnIFrames = true;
        [SerializeField, Range(0.02f, 0.2f)] private float blinkInterval = 0.08f;
        [Tooltip("Si está vacío, usa SpriteRenderer del GO. Si tu sprite está en un child, arrástralo aquí.")]
        [SerializeField] private SpriteRenderer[] blinkRenderers;

        [Header("Death / Respawn")]
        [Tooltip("Si es null, usa posición inicial.")]
        [SerializeField] private Transform respawnPoint;
        [Tooltip("Delay antes de respawnear. 0 = instantáneo.")]
        [SerializeField, Range(0f, 5f)] private float respawnDelay = 0.0f;
        [Tooltip("Si true, al respawn resetea vida a full.")]
        [SerializeField] private bool restoreFullHealthOnRespawn = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;

        private bool invulnerable;
        private Vector3 initialPos;
        private Rigidbody2D rb;
        private PlayerController playerController;

        private Coroutine iFramesCo;
        private Coroutine blinkCo;
        private Coroutine hitStunCo;
        private Coroutine respawnCo;

        // Eventos (opcional, UI / animaciones / VFX)
        public event Action<int, int> OnHeartsChanged; // (current, max) -> viene de PlayerResources.Health
        public event Action<int> OnDamaged;            // daño recibido
        public event Action OnDied;
        public event Action OnRespawned;

        public bool IsInvulnerable => invulnerable;

        private void Awake()
        {
            initialPos = transform.position;

            rb = GetComponent<Rigidbody2D>();
            playerController = GetComponent<PlayerController>();

            if (playerResources == null)
                playerResources = GetComponent<PlayerResources>();

            // Auto-assign renderers si no hay lista
            if (blinkRenderers == null || blinkRenderers.Length == 0)
            {
                var sr = GetComponent<SpriteRenderer>();
                if (sr != null) blinkRenderers = new[] { sr };
                else blinkRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            }
        }

        private void OnEnable()
        {
            // 🔗 Engancha cambios de vida del recurso (para que UI/anim reciba “hearts changed”)
            if (playerResources != null && playerResources.Health != null)
            {
                playerResources.Health.OnChanged += HandleHealthChanged;
                // primer push
                OnHeartsChanged?.Invoke(playerResources.Health.Current, playerResources.Health.Max);
            }

            if (invulnerableOnSpawn && spawnIFrames > 0f)
                StartIFrames(spawnIFrames);
        }

        private void OnDisable()
        {
            if (playerResources != null && playerResources.Health != null)
                playerResources.Health.OnChanged -= HandleHealthChanged;
        }

        private void HandleHealthChanged(int current, int max)
        {
            OnHeartsChanged?.Invoke(current, max);
        }

        // -------------------------
        // IDamageable
        // -------------------------
        public void TakeDamage(int damage, Vector2 sourcePosition)
        {
            ApplyDamage(damage, sourcePosition);
        }

        // Helper por si queréis llamar desde tests sin source
        public void TakeDamage(int damage)
        {
            ApplyDamage(damage, transform.position);
        }

        private void ApplyDamage(int dmg, Vector2 sourcePosition)
        {
            if (dmg <= 0) return;

            if (playerResources == null || playerResources.Health == null)
            {
                if (debugLogs) Debug.LogWarning("[PLAYER HEALTH] playerResources/Health NULL");
                return;
            }

            if (invulnerable)
            {
                if (debugLogs)
                    Debug.Log($"[PLAYER HIT] Ignorado (invulnerable) dmg={dmg} from={sourcePosition}");
                return;
            }

            int before = playerResources.Health.Current;

            // ✅ Vida REAL: baja aquí (esto dispara OnChanged -> UI de máscaras cambia)
            playerResources.TakeDamage(dmg);

            int after = playerResources.Health.Current;

            if (debugLogs)
                Debug.Log($"[PLAYER HIT] {before}->{after}/{playerResources.Health.Max} (-{dmg}) from={sourcePosition}");

            OnDamaged?.Invoke(dmg);

            ApplyKnockback(sourcePosition);

            // ¿murió?
            if (after <= 0)
            {
                Die();
                return;
            }

            if (iFrames > 0f) StartIFrames(iFrames);
            if (hitStunTime > 0f) StartHitStun(hitStunTime);
        }

        private void ApplyKnockback(Vector2 sourcePosition)
        {
            if (rb == null) return;

            float dx = transform.position.x - sourcePosition.x;
            float dirX = Mathf.Sign(dx);
            if (dirX == 0f) dirX = 1f;

            rb.velocity = new Vector2(dirX * knockbackX, knockbackY);

            if (debugLogs)
                Debug.Log($"[PLAYER KB] dirX={dirX} vel=({rb.velocity.x:0.00},{rb.velocity.y:0.00})");
        }

        // -------------------------
        // I-FRAMES + BLINK
        // -------------------------
        private void StartIFrames(float duration)
        {
            if (iFramesCo != null) StopCoroutine(iFramesCo);
            iFramesCo = StartCoroutine(IFramesRoutine(duration));
        }

        private IEnumerator IFramesRoutine(float duration)
        {
            invulnerable = true;
            if (debugLogs) Debug.Log($"[PLAYER I-FRAMES] ON ({duration:0.00}s)");

            if (blinkOnIFrames)
            {
                if (blinkCo != null) StopCoroutine(blinkCo);
                blinkCo = StartCoroutine(BlinkRoutine(duration));
            }

            yield return new WaitForSeconds(duration);

            invulnerable = false;
            if (debugLogs) Debug.Log("[PLAYER I-FRAMES] OFF");

            SetBlinkVisible(true);
            iFramesCo = null;
        }

        private IEnumerator BlinkRoutine(float duration)
        {
            float t = 0f;
            bool visible = true;
            float interval = Mathf.Max(0.02f, blinkInterval);

            while (t < duration)
            {
                visible = !visible;
                SetBlinkVisible(visible);
                yield return new WaitForSeconds(interval);
                t += interval;
            }

            SetBlinkVisible(true);
            blinkCo = null;
        }

        private void SetBlinkVisible(bool visible)
        {
            if (blinkRenderers == null) return;
            for (int i = 0; i < blinkRenderers.Length; i++)
            {
                var r = blinkRenderers[i];
                if (r != null) r.enabled = visible;
            }
        }

        // -------------------------
        // HITSTUN
        // -------------------------
        private void StartHitStun(float duration)
        {
            if (playerController == null) return;
            if (hitStunCo != null) StopCoroutine(hitStunCo);
            hitStunCo = StartCoroutine(HitStunRoutine(duration));
        }

        private IEnumerator HitStunRoutine(float duration)
        {
            playerController.enabled = false;
            if (debugLogs) Debug.Log($"[PLAYER HITSTUN] ON ({duration:0.00}s)");
            yield return new WaitForSeconds(duration);
            playerController.enabled = true;
            if (debugLogs) Debug.Log("[PLAYER HITSTUN] OFF");
            hitStunCo = null;
        }

        // -------------------------
        // Death / Respawn
        // -------------------------
        private void Die()
        {
            if (debugLogs) Debug.Log("[PLAYER HEALTH] DIED");
            OnDied?.Invoke();

            invulnerable = true;

            if (respawnCo != null) StopCoroutine(respawnCo);

            if (respawnDelay > 0f)
                respawnCo = StartCoroutine(RespawnRoutine(respawnDelay));
            else
                RespawnImmediate();
        }

        private IEnumerator RespawnRoutine(float delay)
        {
            yield return new WaitForSeconds(delay);
            RespawnImmediate();
            respawnCo = null;
        }

        private void RespawnImmediate()
        {
            Vector3 target = respawnPoint != null ? respawnPoint.position : initialPos;

            if (rb != null)
            {
                rb.velocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.position = target;
            }
            else
            {
                transform.position = target;
            }

            if (playerResources != null && playerResources.Health != null && restoreFullHealthOnRespawn)
            {
                // reset vida a full
                playerResources.Health.SetCurrent(playerResources.Health.Max);
            }

            invulnerable = false;
            SetBlinkVisible(true);

            if (spawnIFrames > 0f)
                StartIFrames(spawnIFrames);

            if (debugLogs) Debug.Log("[PLAYER HEALTH] RESPAWN");
            OnRespawned?.Invoke();
        }

        // -------------------------
        // Extras
        // -------------------------
        public void SetRespawnPoint(Transform t) => respawnPoint = t;

        public void Heal(int amount)
        {
            if (amount <= 0) return;
            if (playerResources == null || playerResources.Health == null) return;

            int before = playerResources.Health.Current;
            playerResources.Heal(amount);
            int after = playerResources.Health.Current;

            if (debugLogs)
                Debug.Log($"[PLAYER HEAL] +{amount} => {before}->{after}/{playerResources.Health.Max}");
        }
    }
}

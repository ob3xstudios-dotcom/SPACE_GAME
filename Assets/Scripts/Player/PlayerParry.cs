using System.Collections;
using System;
using UnityEngine;
using Game.Systems;
using Game.Combat;

namespace Game.Player
{
    public class PlayerParry : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private PlayerController controller;
        [SerializeField] private Game.Input.InputReader input;
        [SerializeField] private PlayerAnimatorDriver animDriver;
        [SerializeField] private PlayerMana mana;
        [SerializeField] private PlayerStamina stamina;
        [SerializeField] private PlayerProgress progress;

        [Header("Parry Base")]
        [SerializeField] private bool parryUnlocked = true;
        [SerializeField, Range(0.05f, 0.35f)] private float parryWindow = 0.14f;
        [SerializeField, Range(0.1f, 1.2f)] private float parryCooldown = 0.45f;
        [SerializeField, Range(0f, 0.5f)] private float failRecovery = 0.18f;
        [SerializeField, Range(0f, 0.5f)] private float movementLockSeconds = 0.22f;

        [Header("Hitbox")]
        [SerializeField] private float radius = 0.65f;
        [SerializeField] private float forwardOffset = 0.7f;
        [SerializeField] private LayerMask enemyLayer;

        [Header("Enemy Push")]
        [SerializeField] private float pushSpeed = 9f;
        [SerializeField] private float pushLockSeconds = 0.18f;

        [Header("Perfect Parry Feel")]
        [SerializeField, Range(0f, 0.15f)] private float successHitStop = 0.06f;
        [SerializeField, Range(0f, 0.15f)] private float failHitStop = 0.00f;

        [Header("Dagger Upgrade")]
        [SerializeField] private bool useDaggerUpgrade = true;
        [SerializeField, Min(0)] private int stunManaCost = 1;
        [SerializeField, Range(0f, 3f)] private float stunSeconds = 0.65f;

        [Header("Stamina Reward")]
        [SerializeField, Min(0)] private int parryStaminaGain = 1;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private float cdT;
        private float windowT;
        private float failT;
        private bool projectileParryResolvedThisWindow;
        private readonly Collider2D[] projectileParryHits = new Collider2D[16];

        public event Action<Game.Enemies.EnemyBase> ParrySucceeded;
        public bool IsProjectileParryWindowActive =>
            isActiveAndEnabled &&
            parryUnlocked &&
            windowT > 0f &&
            !projectileParryResolvedThisWindow;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<PlayerController>();
            if (input == null) input = GetComponent<Game.Input.InputReader>();
            if (animDriver == null) animDriver = GetComponent<PlayerAnimatorDriver>();
            if (mana == null) mana = GetComponent<PlayerMana>();
            if (stamina == null) stamina = GetComponent<PlayerStamina>();
            if (progress == null) progress = GetComponent<PlayerProgress>();
        }

        private void Update()
        {
            cdT -= Time.deltaTime;
            windowT -= Time.deltaTime;
            failT -= Time.deltaTime;

            if (!parryUnlocked) return;

            if (IsProjectileParryWindowActive)
                TryParryProjectileInRange();

            if (input == null) return;

            if (controller != null &&
                (controller.IsSwinging || controller.IsOverheadRailHanging))
            {
                input.ConsumeParryPressed();
                return;
            }

            if (!input.ConsumeParryPressed()) return;

            if (debugLogs)
                Debug.Log("[PARRY] BUTTON PRESSED");

            if (failT > 0f)
            {
                if (debugLogs) Debug.Log($"[PARRY] blocked by fail recovery: {failT:0.00}s");
                return;
            }

            if (cdT > 0f)
            {
                if (debugLogs) Debug.Log($"[PARRY] blocked by cooldown: {cdT:0.00}s");
                return;
            }

            StartParry();
        }

        private void StartParry()
        {
            if (debugLogs)
                Debug.Log("[PARRY] START");

            controller?.DropCarriedObject();

            cdT = parryCooldown;
            windowT = parryWindow;
            projectileParryResolvedThisWindow = false;

            animDriver?.TriggerParry();
            controller?.LockMovement(movementLockSeconds);

            bool success = TryParryHit();

            if (debugLogs)
                Debug.Log($"[PARRY] success={success}");

            if (!success)
            {
                failT = failRecovery;

                if (failHitStop > 0f)
                    HitStopManager.Request(failHitStop);
            }
        }

        private bool TryParryHit()
        {
            Vector2 origin = transform.position;
            Vector2 dir = controller != null && controller.FacingLeft ? Vector2.left : Vector2.right;
            Vector2 hitPos = origin + dir * forwardOffset;

            if (debugLogs)
                Debug.Log($"[PARRY] checking enemies at={hitPos} radius={radius} mask={enemyLayer.value}");

            Collider2D[] hits = Physics2D.OverlapCircleAll(hitPos, radius, enemyLayer);

            if (hits == null || hits.Length == 0)
            {
                if (debugLogs) Debug.Log("[PARRY] no enemies in hitbox");
                return false;
            }

            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D col = hits[i];
                if (col == null) continue;

                var enemy = col.GetComponentInParent<Game.Enemies.EnemyBase>();
                if (enemy == null)
                {
                    if (debugLogs) Debug.Log($"[PARRY] collider {col.name} has no EnemyBase");
                    continue;
                }

                if (debugLogs)
                    Debug.Log($"[PARRY] enemy found: {enemy.name} | IsParryable={enemy.IsParryable}");

                if (!enemy.IsParryable) continue;

                bool parried = Game.Enemies.States.EnemyAttackState.TryParryEnemy(enemy);
                if (!parried)
                {
                    if (debugLogs) Debug.Log($"[PARRY] TryParryEnemy failed on {enemy.name}");
                    continue;
                }

                Vector2 pushDir = (Vector2)enemy.transform.position - origin;
                if (pushDir.sqrMagnitude < 0.0001f)
                    pushDir = dir;

                enemy.ApplyParryPush(pushDir, pushSpeed, pushLockSeconds);
                TryApplyDaggerStun(enemy);
                stamina?.RestoreStamina(parryStaminaGain);

                if (successHitStop > 0f)
                    HitStopManager.Request(successHitStop);

                if (debugLogs)
                    Debug.Log($"[PARRY] SUCCESS on {enemy.name}");

                // Melee keeps its existing priority and consumes this parry attempt.
                projectileParryResolvedThisWindow = true;
                windowT = 0f;
                ParrySucceeded?.Invoke(enemy);
                return true;
            }

            // Preserve melee priority and existing EnemyBase behaviour. Projectiles
            // are considered only when no melee enemy resolved this button press.
            if (TryParryProjectiles(hits, hits.Length))
                return true;

            if (debugLogs)
                Debug.Log("[PARRY] enemies found, but none were parryable");

            return false;
        }

        /// <summary>
        /// Projectile-side fallback used when a swept projectile reaches the Player
        /// during the active window. It never grants general invulnerability.
        /// </summary>
        public bool TryParryProjectile(IParryableProjectile projectile)
        {
            if (!IsProjectileParryWindowActive || projectile == null || !projectile.IsParryable)
                return false;

            Collider2D projectileCollider = projectile.ParryCollider;
            if (projectileCollider == null || !projectileCollider.enabled)
                return false;

            int projectileLayer = 1 << projectileCollider.gameObject.layer;
            if ((enemyLayer.value & projectileLayer) == 0)
                return false;

            Vector2 origin = transform.position;
            Vector2 direction = GetParryDirection();
            Vector2 hitPosition = origin + direction * forwardOffset;
            Vector2 toProjectile = projectile.Position - origin;

            if (Vector2.Dot(toProjectile, direction) <= 0f)
                return false;

            Vector2 closestPoint = projectileCollider.ClosestPoint(hitPosition);
            if ((closestPoint - hitPosition).sqrMagnitude > radius * radius)
                return false;

            var context = new ProjectileParryContext(transform, origin, direction);
            ProjectileParryResolution resolution = projectile.ResolveParry(
                ProjectileParryAction.Destroy,
                context);
            if (resolution != ProjectileParryResolution.Destroyed)
                return false;

            projectileParryResolvedThisWindow = true;
            windowT = 0f;
            failT = 0f;
            stamina?.RestoreStamina(parryStaminaGain);

            if (successHitStop > 0f)
                HitStopManager.Request(successHitStop);

            if (debugLogs)
                Debug.Log($"[PARRY] PROJECTILE SUCCESS at={projectile.Position}");

            // Existing feedback handles a null enemy by using the Player position.
            ParrySucceeded?.Invoke(null);
            return true;
        }

        private bool TryParryProjectileInRange()
        {
            Vector2 hitPosition = (Vector2)transform.position +
                                  GetParryDirection() * forwardOffset;
            int count = Physics2D.OverlapCircleNonAlloc(
                hitPosition,
                radius,
                projectileParryHits,
                enemyLayer);
            return TryParryProjectiles(projectileParryHits, count);
        }

        private bool TryParryProjectiles(Collider2D[] hits, int count)
        {
            if (hits == null || count <= 0)
                return false;

            int safeCount = Mathf.Min(count, hits.Length);
            for (int i = 0; i < safeCount; i++)
            {
                Collider2D hit = hits[i];
                if (hit == null)
                    continue;

                IParryableProjectile projectile =
                    hit.GetComponent<IParryableProjectile>() ??
                    hit.GetComponentInParent<IParryableProjectile>();
                if (projectile != null && TryParryProjectile(projectile))
                    return true;
            }

            return false;
        }

        private Vector2 GetParryDirection()
        {
            return controller != null && controller.FacingLeft
                ? Vector2.left
                : Vector2.right;
        }

        private void TryApplyDaggerStun(Game.Enemies.EnemyBase enemy)
        {
            if (!useDaggerUpgrade) return;
            if (enemy == null) return;

            bool hasDagger = progress != null && progress.HasDagger;
            if (!hasDagger)
            {
                if (debugLogs) Debug.Log("[PARRY] no dagger: stun skipped");
                return;
            }

            int cost = progress != null ? progress.ParryStunManaCost : stunManaCost;
            float stun = progress != null ? progress.ParryStunSeconds : stunSeconds;

            if (mana == null)
            {
                if (debugLogs) Debug.Log("[PARRY] no mana component: stun skipped");
                return;
            }

            if (!mana.ConsumeMana(cost))
            {
                if (debugLogs) Debug.Log("[PARRY] not enough mana: stun skipped");
                return;
            }

            enemy.SetState(new Game.Enemies.States.EnemyStunnedState(stun));

            if (debugLogs)
                Debug.Log($"[PARRY] dagger stun applied: {stun:0.00}s");
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector2 origin = transform.position;
            Vector2 dir = Vector2.right;

            if (controller != null && controller.FacingLeft)
                dir = Vector2.left;

            Vector2 hitPos = origin + dir * forwardOffset;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(hitPos, radius);
        }
#endif
    }
}

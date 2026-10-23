using Game.Combat;
using Game.Player;
using UnityEngine;

namespace Game.Enemies.CavePlant
{
    public enum CavePlantState
    {
        Idle,
        Telegraph,
        Fire,
        Cooldown
    }

    /// <summary>
    /// Stationary ranged enemy. It records one unobstructed Player position at the
    /// start of its telegraph and never steers a projectile after it has been fired.
    /// </summary>
    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(Collider2D))]
    [DisallowMultipleComponent]
    public sealed class CavePlantController : MonoBehaviour
    {
        private const int DetectionCapacity = 16;
        private const int WorldCastCapacity = 12;

        [Header("References")]
        [SerializeField] private Transform muzzle;
        [SerializeField] private Vector2 muzzleOffset = new Vector2(0.55f, 0.65f);
        [SerializeField] private CavePlantProjectile projectilePrefab;
        [SerializeField] private CavePlantPrototypeVisual prototypeVisual;

        [Header("Detection")]
        [SerializeField, Min(0.1f)] private float detectionRange = 7f;
        [SerializeField] private LayerMask worldMask = 10312;
        [SerializeField] private LayerMask playerMask = 512;

        [Header("Shared Perception Pilot")]
        [SerializeField] private bool useSharedPerception;
        [SerializeField] private EnemyPerception2D sharedPerception;
        [SerializeField] private Vector2 perceptionFacingDirection = Vector2.right;

        [Header("Attack")]
        [SerializeField, Min(0f)] private float telegraphDuration = 0.65f;
        [SerializeField, Min(0f)] private float fireCooldown = 1.8f;
        [SerializeField, Min(0.1f)] private float projectileSpeed = 7f;
        [SerializeField, Min(1)] private int projectileDamage = 1;
        [SerializeField, Min(0.1f)] private float projectileLifetime = 4f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs;
        [SerializeField] private bool drawGizmos = true;

        private readonly Collider2D[] detectionHits = new Collider2D[DetectionCapacity];
        private readonly Collider2D[] muzzleOverlapHits = new Collider2D[WorldCastCapacity];
        private readonly RaycastHit2D[] worldCastHits = new RaycastHit2D[WorldCastCapacity];
        private Health health;
        private float stateTimer;
        private Vector2 lockedTargetPosition;
        private Vector2 aimDirection = Vector2.right;
        private bool hasLockedTarget;

        public CavePlantState CurrentState { get; private set; } = CavePlantState.Idle;
        public float TelegraphProgress =>
            CurrentState == CavePlantState.Telegraph && telegraphDuration > 0f
                ? Mathf.Clamp01(stateTimer / telegraphDuration)
                : CurrentState == CavePlantState.Fire ? 1f : 0f;
        public Vector2 AimDirection => aimDirection;
        public Vector2 MuzzlePosition => muzzle != null
            ? (Vector2)muzzle.position
            : (Vector2)transform.TransformPoint(muzzleOffset);
        public bool HasLockedTarget => hasLockedTarget;
        public Vector2 LockedTargetPosition => lockedTargetPosition;

        private void Awake()
        {
            health = GetComponent<Health>();
            if (muzzle == null)
                muzzle = transform.Find("Muzzle");
            if (prototypeVisual == null)
                prototypeVisual = GetComponent<CavePlantPrototypeVisual>();
            if (sharedPerception == null)
                sharedPerception = GetComponent<EnemyPerception2D>();
            if (sharedPerception != null)
            {
                sharedPerception.SetFacingDirection(perceptionFacingDirection);
                sharedPerception.enabled = useSharedPerception;
            }

            TransitionTo(CavePlantState.Idle);
        }

        private void OnEnable()
        {
            if (health == null)
                health = GetComponent<Health>();
            if (health != null)
                health.OnDied += HandleDied;
        }

        private void OnDisable()
        {
            if (health != null)
                health.OnDied -= HandleDied;

            stateTimer = 0f;
            hasLockedTarget = false;
        }

        private void Update()
        {
            switch (CurrentState)
            {
                case CavePlantState.Idle:
                    UpdateIdle();
                    break;
                case CavePlantState.Telegraph:
                    UpdateTelegraph();
                    break;
                case CavePlantState.Fire:
                    FireRecordedShot();
                    TransitionTo(CavePlantState.Cooldown);
                    break;
                case CavePlantState.Cooldown:
                    UpdateCooldown();
                    break;
            }
        }

        private void UpdateIdle()
        {
            if (!TryAcquireTarget(out Vector2 targetPosition))
                return;

            lockedTargetPosition = targetPosition;
            hasLockedTarget = true;
            UpdateAimDirection(targetPosition);
            TransitionTo(CavePlantState.Telegraph);
        }

        private void UpdateTelegraph()
        {
            stateTimer += Time.deltaTime;
            if (stateTimer < telegraphDuration)
                return;

            TransitionTo(CavePlantState.Fire);
        }

        private void UpdateCooldown()
        {
            stateTimer += Time.deltaTime;
            if (stateTimer < fireCooldown)
                return;

            hasLockedTarget = false;
            TransitionTo(CavePlantState.Idle);
        }

        private bool TryAcquireTarget(out Vector2 targetPosition)
        {
            if (useSharedPerception)
                return TryAcquireSharedPerceptionTarget(out targetPosition);

            targetPosition = default;
            int count = Physics2D.OverlapCircleNonAlloc(
                transform.position,
                detectionRange,
                detectionHits,
                playerMask);

            bool found = false;
            float bestDistanceSq = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider2D candidate = detectionHits[i];
                if (candidate == null || !candidate.enabled)
                    continue;
                if (candidate.GetComponentInParent<PlayerHealth>() == null)
                    continue;

                Vector2 candidatePoint = candidate.bounds.center;
                float distanceSq = (candidatePoint - MuzzlePosition).sqrMagnitude;
                if (distanceSq >= bestDistanceSq || !HasClearShot(candidatePoint))
                    continue;

                bestDistanceSq = distanceSq;
                targetPosition = candidatePoint;
                found = true;
            }

            return found;
        }

        private bool TryAcquireSharedPerceptionTarget(out Vector2 targetPosition)
        {
            targetPosition = default;
            if (sharedPerception == null || !sharedPerception.isActiveAndEnabled ||
                (!sharedPerception.IsPlayerVisible && !sharedPerception.HasActiveNoiseStimulus))
            {
                return false;
            }

            Transform perceivedTarget = sharedPerception.Target;
            if (perceivedTarget == null)
                return false;
            PlayerHealth perceivedHealth = perceivedTarget.GetComponent<PlayerHealth>() ??
                                           perceivedTarget.GetComponentInChildren<PlayerHealth>();
            if (perceivedHealth == null)
                return false;

            targetPosition = sharedPerception.TargetPoint;
            if (((Vector2)transform.position - targetPosition).sqrMagnitude > detectionRange * detectionRange)
                return false;

            return HasClearShot(targetPosition);
        }

        private void FireRecordedShot()
        {
            if (!hasLockedTarget || projectilePrefab == null)
            {
                LogShotBlocked("MissingTargetOrProjectile");
                return;
            }

            Vector2 spawnPosition = MuzzlePosition;
            Vector2 toTarget = lockedTargetPosition - spawnPosition;
            if (toTarget.sqrMagnitude < 0.0001f)
            {
                LogShotBlocked("TargetAtMuzzle");
                return;
            }

            if (!HasClearShot(lockedTargetPosition))
            {
                LogShotBlocked("ShotBecameOccluded");
                return;
            }

            Vector2 direction = toTarget.normalized;
            UpdateAimDirection(lockedTargetPosition);
            CavePlantProjectile projectile = Instantiate(
                projectilePrefab,
                spawnPosition,
                Quaternion.identity);
            projectile.Initialize(
                direction,
                projectileSpeed,
                projectileDamage,
                projectileLifetime,
                worldMask,
                playerMask,
                transform,
                debugLogs);

            if (debugLogs)
            {
                Debug.Log(
                    $"[CAVE PLANT] Fired at recorded={lockedTargetPosition} " +
                    $"direction={direction} speed={projectileSpeed:0.00}",
                    this);
            }
        }

        private bool HasClearShot(Vector2 targetPosition)
        {
            Vector2 origin = MuzzlePosition;
            Vector2 toTarget = targetPosition - origin;
            float distance = toTarget.magnitude;
            if (distance <= 0.0001f)
                return false;

            float radius = projectilePrefab != null
                ? projectilePrefab.CollisionRadius
                : 0.14f;

            int overlapCount = Physics2D.OverlapCircleNonAlloc(
                origin,
                radius,
                muzzleOverlapHits,
                worldMask);
            for (int i = 0; i < overlapCount; i++)
            {
                Collider2D candidate = muzzleOverlapHits[i];
                if (IsBlockingWorldCollider(candidate))
                    return false;
            }

            int hitCount = Physics2D.CircleCastNonAlloc(
                origin,
                radius,
                toTarget / distance,
                worldCastHits,
                distance,
                worldMask);
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = worldCastHits[i];
                if (IsBlockingWorldCollider(hit.collider))
                    return false;
            }

            return true;
        }

        private bool IsBlockingWorldCollider(Collider2D candidate)
        {
            if (candidate == null || candidate.isTrigger)
                return false;
            if (candidate.transform == transform || candidate.transform.IsChildOf(transform))
                return false;
            return true;
        }

        private void UpdateAimDirection(Vector2 targetPosition)
        {
            Vector2 direction = targetPosition - MuzzlePosition;
            if (direction.sqrMagnitude > 0.0001f)
            {
                aimDirection = direction.normalized;
                if (useSharedPerception && sharedPerception != null)
                    sharedPerception.SetFacingDirection(aimDirection);
            }
        }

        private void TransitionTo(CavePlantState nextState)
        {
            CurrentState = nextState;
            stateTimer = 0f;
            prototypeVisual?.NotifyStateChanged(nextState);
        }

        private void HandleDied(Health _)
        {
            enabled = false;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogShotBlocked(string reason)
        {
            if (debugLogs)
                Debug.Log($"[CAVE PLANT] Shot blocked: {reason}", this);
        }

        private void OnValidate()
        {
            detectionRange = Mathf.Max(0.1f, detectionRange);
            telegraphDuration = Mathf.Max(0f, telegraphDuration);
            fireCooldown = Mathf.Max(0f, fireCooldown);
            projectileSpeed = Mathf.Max(0.1f, projectileSpeed);
            projectileDamage = Mathf.Max(1, projectileDamage);
            projectileLifetime = Mathf.Max(0.1f, projectileLifetime);
            if (perceptionFacingDirection.sqrMagnitude < 0.0001f)
                perceptionFacingDirection = Vector2.right;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
                return;

            Gizmos.color = new Color(0.45f, 1f, 0.3f, 0.65f);
            Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.1f, detectionRange));

            Vector2 muzzlePosition = muzzle != null
                ? (Vector2)muzzle.position
                : (Vector2)transform.TransformPoint(muzzleOffset);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(muzzlePosition, projectilePrefab != null
                ? projectilePrefab.CollisionRadius
                : 0.14f);

            Vector2 end = hasLockedTarget
                ? lockedTargetPosition
                : muzzlePosition + aimDirection * Mathf.Min(3f, detectionRange);
            Gizmos.color = hasLockedTarget ? Color.red : new Color(1f, 0.6f, 0.2f, 0.65f);
            Gizmos.DrawLine(muzzlePosition, end);
        }
#endif
    }
}

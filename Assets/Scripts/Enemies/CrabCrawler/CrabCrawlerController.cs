using Game.Combat;
using Game.Player;
using UnityEngine;

namespace Game.Enemies.CrabCrawler
{
    public enum CrabCrawlerState
    {
        Patrolling,
        Windup,
        Lunging,
        Recovering
    }

    /// <summary>
    /// Small ground enemy with a bounded patrol and a deliberately short attack step.
    /// Art is kept under a separate visual root so it can be replaced independently.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(BoxCollider2D))]
    [RequireComponent(typeof(Health))]
    public sealed class CrabCrawlerController : MonoBehaviour, IPlayerDownAttackContactSuppressor
    {
        private const float BlockedAttackTargetHysteresis = 0.15f;

        [Header("References")]
        [SerializeField] private BoxCollider2D bodyCollider;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private string playerTag = "Player";

        [Header("Patrol Limits (local to spawn)")]
        [SerializeField] private float leftPatrolOffset = -2.5f;
        [SerializeField] private float rightPatrolOffset = 2.5f;
        [SerializeField, Min(0f)] private float patrolSpeed = 1.5f;
        [SerializeField, Min(0f)] private float patrolAcceleration = 18f;
        [SerializeField, Min(0f)] private float turnPauseDuration = 0.12f;

        [Header("Navigation Safety")]
        [SerializeField] private LayerMask groundMask = 10312;
        [SerializeField] private LayerMask obstacleMask = 10312;
        [SerializeField, Min(0.02f)] private float groundProbeDistance = 0.5f;
        [SerializeField, Min(0f)] private float groundProbeForwardOffset = 0.1f;
        [SerializeField, Min(0.02f)] private float obstacleProbeDistance = 0.16f;

        [Header("Detection")]
        [SerializeField] private LayerMask playerMask = 1 << 9;
        [SerializeField, Min(0.1f)] private float detectionRange = 2.25f;
        [SerializeField, Min(0.1f)] private float detectionVerticalTolerance = 1.1f;

        [Header("Shared Perception Pilot")]
        [Tooltip("OFF preserves the original Crab detection exactly. Enable deliberately to test EnemyPerception2D.")]
        [SerializeField] private bool useSharedPerception;
        [SerializeField] private EnemyPerception2D sharedPerception;

        [Header("Short Attack")]
        [SerializeField, Min(0f)] private float windupDuration = 0.18f;
        [SerializeField, Min(0.05f)] private float lungeDistance = 0.7f;
        [SerializeField, Min(0f)] private float lungeSpeed = 4.5f;
        [SerializeField, Min(0f)] private float lungeAcceleration = 35f;
        [SerializeField, Min(0.05f)] private float maximumLungeDuration = 0.35f;
        [SerializeField, Min(0f)] private float recoveryDuration = 0.65f;

        [Header("Contact Damage")]
        [SerializeField, Min(1)] private int contactDamage = 1;
        [SerializeField, Min(0.05f)] private float contactDamageInterval = 0.75f;
        [Tooltip("Contact grace granted only after this Crab takes a confirmed downward attack.")]
        [SerializeField, Min(0f)] private float downAttackContactGraceDuration = 0.12f;

        [Header("Debug")]
        [SerializeField] private bool debugGizmos = true;

        private Rigidbody2D body;
        private PlayerHealth playerHealth;
        private Collider2D playerBodyCollider;
        private Vector3 visualBaseScale = Vector3.one;
        private float patrolOriginX;
        private float stateTimer;
        private float turnPauseTimer;
        private float lungeStartX;
        private float nextContactDamageTime;
        private float suppressContactDamageUntil;
        private int lastSuppressedDownAttackId;
        private PlayerHealth pendingContactDamage;
        private int patrolDirection = 1;
        private int lungeDirection = 1;
        private int blockedAttackDirection;
        private float blockedAttackTargetX;

        public CrabCrawlerState CurrentState { get; private set; } = CrabCrawlerState.Patrolling;
        public int FacingDirection { get; private set; } = 1;
        public float WindupProgress => CurrentState != CrabCrawlerState.Windup
            ? 0f
            : windupDuration <= 0f
                ? 1f
                : Mathf.Clamp01(stateTimer / windupDuration);

        private float LeftPatrolX => patrolOriginX + Mathf.Min(leftPatrolOffset, rightPatrolOffset);
        private float RightPatrolX => patrolOriginX + Mathf.Max(leftPatrolOffset, rightPatrolOffset);

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            if (bodyCollider == null)
                bodyCollider = GetComponent<BoxCollider2D>();
            if (visualRoot == null)
            {
                Transform candidate = transform.Find("Visual");
                if (candidate != null)
                    visualRoot = candidate;
            }

            body.bodyType = RigidbodyType2D.Dynamic;
            body.constraints |= RigidbodyConstraints2D.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            patrolOriginX = body.position.x;

            if (visualRoot != null)
                visualBaseScale = visualRoot.localScale;

            SetFacing(patrolDirection);
            if (sharedPerception != null)
                sharedPerception.enabled = useSharedPerception;
            ResolvePlayer();
        }

        private void OnEnable()
        {
            ResolvePlayer();
        }

        private void FixedUpdate()
        {
            switch (CurrentState)
            {
                case CrabCrawlerState.Patrolling:
                    UpdatePatrol();
                    break;
                case CrabCrawlerState.Windup:
                    UpdateWindup();
                    break;
                case CrabCrawlerState.Lunging:
                    UpdateLunge();
                    break;
                case CrabCrawlerState.Recovering:
                    UpdateRecovery();
                    break;
            }
        }

        private void LateUpdate()
        {
            ResolvePendingContactDamage();
        }

        private void UpdatePatrol()
        {
            if (TryReturnToPatrolBounds())
                return;

            if (TryGetAttackTarget(out Vector2 playerCenter))
            {
                BeginWindup(playerCenter);
                return;
            }

            if (turnPauseTimer > 0f)
            {
                turnPauseTimer = Mathf.Max(0f, turnPauseTimer - Time.fixedDeltaTime);
                StopHorizontal(patrolAcceleration);
                return;
            }

            if (patrolSpeed <= 0f)
            {
                StopHorizontal(patrolAcceleration);
                return;
            }

            float projectedStep = patrolSpeed * Time.fixedDeltaTime;
            if (MustTurn(patrolDirection, projectedStep))
            {
                TurnAround();
                return;
            }

            SetFacing(patrolDirection);
            MoveHorizontal(patrolDirection * patrolSpeed, patrolAcceleration);
        }

        private bool TryReturnToPatrolBounds()
        {
            int returnDirection;
            if (body.position.x < LeftPatrolX)
                returnDirection = 1;
            else if (body.position.x > RightPatrolX)
                returnDirection = -1;
            else
                return false;

            patrolDirection = returnDirection;
            SetFacing(returnDirection);

            float projectedStep = patrolSpeed * Time.fixedDeltaTime;
            if (patrolSpeed <= 0f ||
                HasObstacleAhead(returnDirection, projectedStep) ||
                !HasGroundAhead(returnDirection, projectedStep))
            {
                StopHorizontal(patrolAcceleration);
                return true;
            }

            MoveHorizontal(returnDirection * patrolSpeed, patrolAcceleration);
            return true;
        }

        private void BeginWindup(Vector2 playerCenter)
        {
            SetFacing(DirectionTo(playerCenter.x));
            stateTimer = 0f;
            CurrentState = CrabCrawlerState.Windup;
            StopHorizontal(lungeAcceleration);
        }

        private void UpdateWindup()
        {
            stateTimer += Time.fixedDeltaTime;
            StopHorizontal(lungeAcceleration);

            if (!TryGetAttackTarget(out Vector2 playerCenter))
            {
                BeginRecovery();
                return;
            }

            SetFacing(DirectionTo(playerCenter.x));

            if (stateTimer >= windupDuration)
                BeginLunge();
        }

        private void BeginLunge()
        {
            lungeDirection = FacingDirection;
            lungeStartX = body.position.x;
            stateTimer = 0f;
            CurrentState = CrabCrawlerState.Lunging;
        }

        private void UpdateLunge()
        {
            stateTimer += Time.fixedDeltaTime;
            float travelled = Mathf.Abs(body.position.x - lungeStartX);
            if (travelled >= lungeDistance || stateTimer >= maximumLungeDuration)
            {
                BeginRecovery();
                return;
            }

            float projectedStep = lungeSpeed * Time.fixedDeltaTime;
            if (HasObstacleAhead(lungeDirection, projectedStep) ||
                !HasGroundAhead(lungeDirection, projectedStep))
            {
                BlockCurrentAttackTarget();
                patrolDirection = -lungeDirection;
                SetFacing(patrolDirection);
                BeginRecovery();
                return;
            }

            MoveHorizontal(lungeDirection * lungeSpeed, lungeAcceleration);
        }

        private void BeginRecovery()
        {
            stateTimer = 0f;
            CurrentState = CrabCrawlerState.Recovering;
            body.velocity = new Vector2(0f, body.velocity.y);
        }

        private void UpdateRecovery()
        {
            stateTimer += Time.fixedDeltaTime;
            StopHorizontal(lungeAcceleration);
            if (stateTimer < recoveryDuration)
                return;

            stateTimer = 0f;
            CurrentState = CrabCrawlerState.Patrolling;
        }

        private void TurnAround()
        {
            patrolDirection *= -1;
            SetFacing(patrolDirection);
            turnPauseTimer = turnPauseDuration;
            StopHorizontal(patrolAcceleration);
        }

        private bool MustTurn(int direction, float projectedStep)
        {
            return HasReachedPatrolLimit(direction, projectedStep) ||
                   HasObstacleAhead(direction, projectedStep) ||
                   !HasGroundAhead(direction, projectedStep);
        }

        private bool HasReachedPatrolLimit(int direction, float projectedStep)
        {
            float projectedX = body.position.x + direction * projectedStep;
            return direction > 0
                ? projectedX >= RightPatrolX
                : projectedX <= LeftPatrolX;
        }

        private bool HasGroundAhead(int direction, float projectedStep)
        {
            Bounds bounds = bodyCollider.bounds;
            Vector2 origin = new Vector2(bounds.center.x, bounds.min.y + 0.05f);
            origin.x += direction * (bounds.extents.x + groundProbeForwardOffset + projectedStep);
            return Physics2D.Raycast(origin, Vector2.down, groundProbeDistance, groundMask).collider != null;
        }

        private bool HasObstacleAhead(int direction, float projectedStep)
        {
            Bounds bounds = bodyCollider.bounds;
            Vector2 origin = bounds.center;
            origin.x += direction * bounds.extents.x;
            Vector2 probeSize = new Vector2(0.05f, Mathf.Max(0.1f, bounds.size.y * 0.75f));
            float distance = obstacleProbeDistance + projectedStep;

            return Physics2D.BoxCast(
                origin,
                probeSize,
                0f,
                Vector2.right * direction,
                distance,
                obstacleMask).collider != null;
        }

        private bool TryGetAttackTarget(out Vector2 playerCenter)
        {
            if (!TryGetPlayerBounds(out Bounds playerBounds))
            {
                playerCenter = default;
                return false;
            }

            playerCenter = playerBounds.center;

            Vector2 origin = bodyCollider.bounds.center;
            Vector2 delta = playerCenter - origin;
            bool perceivedBySharedSystem = useSharedPerception &&
                                           sharedPerception != null &&
                                           sharedPerception.isActiveAndEnabled &&
                                           (sharedPerception.IsPlayerVisible ||
                                            sharedPerception.HasActiveNoiseStimulus);
            bool detectedByLegacySystem = delta.sqrMagnitude <= detectionRange * detectionRange &&
                                          Mathf.Abs(delta.y) <= detectionVerticalTolerance;
            if (useSharedPerception ? !perceivedBySharedSystem : !detectedByLegacySystem)
            {
                ClearBlockedAttackTarget();
                return false;
            }

            int direction = DirectionTo(playerCenter.x);
            if (IsBlockedAttackTarget(direction, playerCenter.x))
                return false;

            if (!CanReachWithCurrentLunge(playerBounds))
                return false;

            if (Mathf.Abs(delta.x) > 0.01f &&
                (HasObstacleAhead(direction, 0f) || !HasGroundAhead(direction, 0f)))
            {
                BlockAttackTarget(direction, playerCenter.x);
                return false;
            }

            return true;
        }

        private bool CanReachWithCurrentLunge(Bounds playerBounds)
        {
            Bounds crawlerBounds = bodyCollider.bounds;
            bool overlapsVertically =
                playerBounds.min.y <= crawlerBounds.max.y &&
                playerBounds.max.y >= crawlerBounds.min.y;
            if (!overlapsVertically)
                return false;

            float horizontalGap = 0f;
            if (playerBounds.min.x > crawlerBounds.max.x)
                horizontalGap = playerBounds.min.x - crawlerBounds.max.x;
            else if (playerBounds.max.x < crawlerBounds.min.x)
                horizontalGap = crawlerBounds.min.x - playerBounds.max.x;

            return horizontalGap <= lungeDistance;
        }

        private bool IsBlockedAttackTarget(int direction, float targetX)
        {
            if (blockedAttackDirection == 0)
                return false;

            bool targetChangedSide = direction != blockedAttackDirection;
            bool targetMoved = Mathf.Abs(targetX - blockedAttackTargetX) >=
                               BlockedAttackTargetHysteresis;
            if (!targetChangedSide && !targetMoved)
                return true;

            ClearBlockedAttackTarget();
            return false;
        }

        private void BlockCurrentAttackTarget()
        {
            if (TryGetPlayerCenter(out Vector2 playerCenter))
                BlockAttackTarget(lungeDirection, playerCenter.x);
        }

        private void BlockAttackTarget(int direction, float targetX)
        {
            blockedAttackDirection = direction < 0 ? -1 : 1;
            blockedAttackTargetX = targetX;
        }

        private void ClearBlockedAttackTarget()
        {
            blockedAttackDirection = 0;
        }

        private bool TryGetPlayerBounds(out Bounds playerBounds)
        {
            playerBounds = default;
            if (!TryGetPlayerCenter(out Vector2 playerCenter))
                return false;

            playerBounds = playerBodyCollider != null
                ? playerBodyCollider.bounds
                : new Bounds(playerCenter, Vector3.zero);
            return true;
        }

        private bool TryGetPlayerCenter(out Vector2 playerCenter)
        {
            playerCenter = default;
            ResolvePlayer();
            if (playerHealth == null || !playerHealth.isActiveAndEnabled)
                return false;

            if (!IsLayerAllowed(playerHealth.gameObject.layer) &&
                (playerBodyCollider == null || !IsLayerAllowed(playerBodyCollider.gameObject.layer)))
            {
                return false;
            }

            playerCenter = playerBodyCollider != null
                ? (Vector2)playerBodyCollider.bounds.center
                : (Vector2)playerHealth.transform.position;
            return true;
        }

        private void ResolvePlayer()
        {
            if (playerHealth != null && playerHealth.gameObject.activeInHierarchy)
                return;

            playerHealth = null;
            playerBodyCollider = null;

            GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);
            if (playerObject == null)
                return;

            playerHealth =
                playerObject.GetComponent<PlayerHealth>() ??
                playerObject.GetComponentInParent<PlayerHealth>() ??
                playerObject.GetComponentInChildren<PlayerHealth>(true);
            if (playerHealth == null)
                return;

            Collider2D[] colliders = playerHealth.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D candidate = colliders[i];
                if (candidate == null || candidate.isTrigger || !candidate.enabled)
                    continue;

                playerBodyCollider = candidate;
                break;
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            QueueContactDamage(collision != null ? collision.collider : null);
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            QueueContactDamage(collision != null ? collision.collider : null);
        }

        private void QueueContactDamage(Collider2D other)
        {
            if (other == null || !IsLayerAllowed(other.gameObject.layer))
                return;

            PlayerHealth target =
                other.GetComponent<PlayerHealth>() ??
                other.GetComponentInParent<PlayerHealth>();
            if (target != null && target.isActiveAndEnabled)
                pendingContactDamage = target;
        }

        private void ResolvePendingContactDamage()
        {
            PlayerHealth target = pendingContactDamage;
            pendingContactDamage = null;
            if (target == null ||
                !target.isActiveAndEnabled ||
                Time.time < suppressContactDamageUntil ||
                Time.time < nextContactDamageTime)
            {
                return;
            }

            // Set the gate before applying damage so simultaneous callbacks from
            // multiple Player colliders cannot produce duplicate hits.
            nextContactDamageTime = Time.time + contactDamageInterval;
            bool applied = target.TryTakeDamage(contactDamage, body.position, 0f);
            if (applied && CurrentState == CrabCrawlerState.Lunging)
                BeginRecovery();
        }

        public void SuppressContactDamageForDownAttack(int attackId)
        {
            if (attackId <= 0 || attackId == lastSuppressedDownAttackId)
                return;

            lastSuppressedDownAttackId = attackId;
            suppressContactDamageUntil = Mathf.Max(
                suppressContactDamageUntil,
                Time.time + downAttackContactGraceDuration);
            pendingContactDamage = null;
        }

        private bool IsLayerAllowed(int layer)
        {
            return playerMask.value == 0 || (playerMask.value & (1 << layer)) != 0;
        }

        private int DirectionTo(float targetX)
        {
            float delta = targetX - body.position.x;
            if (Mathf.Abs(delta) <= 0.01f)
                return FacingDirection;
            return delta < 0f ? -1 : 1;
        }

        private void SetFacing(int direction)
        {
            if (direction == 0)
                return;

            FacingDirection = direction < 0 ? -1 : 1;
            if (sharedPerception != null)
                sharedPerception.SetFacingDirection(Vector2.right * FacingDirection);
            if (visualRoot == null)
                return;

            Vector3 scale = visualBaseScale;
            scale.x = Mathf.Abs(scale.x) * FacingDirection;
            visualRoot.localScale = scale;
        }

        private void MoveHorizontal(float targetSpeed, float acceleration)
        {
            float velocityX = Mathf.MoveTowards(
                body.velocity.x,
                targetSpeed,
                Mathf.Max(0f, acceleration) * Time.fixedDeltaTime);
            body.velocity = new Vector2(velocityX, body.velocity.y);
        }

        private void StopHorizontal(float acceleration)
        {
            MoveHorizontal(0f, acceleration);
        }

        private void OnValidate()
        {
            patrolSpeed = Mathf.Max(0f, patrolSpeed);
            patrolAcceleration = Mathf.Max(0f, patrolAcceleration);
            turnPauseDuration = Mathf.Max(0f, turnPauseDuration);
            groundProbeDistance = Mathf.Max(0.02f, groundProbeDistance);
            groundProbeForwardOffset = Mathf.Max(0f, groundProbeForwardOffset);
            obstacleProbeDistance = Mathf.Max(0.02f, obstacleProbeDistance);
            downAttackContactGraceDuration = Mathf.Max(0f, downAttackContactGraceDuration);
            detectionRange = Mathf.Max(0.1f, detectionRange);
            detectionVerticalTolerance = Mathf.Max(0.1f, detectionVerticalTolerance);
            windupDuration = Mathf.Max(0f, windupDuration);
            lungeDistance = Mathf.Max(0.05f, lungeDistance);
            lungeSpeed = Mathf.Max(0f, lungeSpeed);
            lungeAcceleration = Mathf.Max(0f, lungeAcceleration);
            maximumLungeDuration = Mathf.Max(0.05f, maximumLungeDuration);
            recoveryDuration = Mathf.Max(0f, recoveryDuration);
            contactDamage = Mathf.Max(1, contactDamage);
            contactDamageInterval = Mathf.Max(0.05f, contactDamageInterval);
        }

        private void OnDrawGizmosSelected()
        {
            if (!debugGizmos)
                return;

            float originX = Application.isPlaying && body != null
                ? patrolOriginX
                : transform.position.x;
            float left = originX + Mathf.Min(leftPatrolOffset, rightPatrolOffset);
            float right = originX + Mathf.Max(leftPatrolOffset, rightPatrolOffset);
            float y = transform.position.y;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(new Vector3(left, y, 0f), new Vector3(right, y, 0f));
            Gizmos.DrawWireSphere(new Vector3(left, y, 0f), 0.1f);
            Gizmos.DrawWireSphere(new Vector3(right, y, 0f), 0.1f);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRange);

            int direction = Application.isPlaying ? FacingDirection : 1;
            Bounds bounds = bodyCollider != null
                ? bodyCollider.bounds
                : new Bounds(transform.position, new Vector3(1.15f, 0.65f, 0f));
            Vector2 groundOrigin = new Vector2(bounds.center.x, bounds.min.y + 0.05f);
            groundOrigin.x += direction * (bounds.extents.x + groundProbeForwardOffset);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(groundOrigin, groundOrigin + Vector2.down * groundProbeDistance);

            Vector2 obstacleOrigin = bounds.center;
            obstacleOrigin.x += direction * bounds.extents.x;
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(
                obstacleOrigin,
                obstacleOrigin + Vector2.right * direction * obstacleProbeDistance);

            Gizmos.color = Color.red;
            Gizmos.DrawLine(
                transform.position,
                transform.position + Vector3.right * direction * lungeDistance);
        }
    }
}

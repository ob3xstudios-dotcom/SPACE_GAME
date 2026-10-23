using System.Collections.Generic;
using UnityEngine;
using Game.Audio;
using Game.Combat;
using Game.World;

namespace Game.Enemies.AerialSentry
{
    public enum AerialSentryPatrolAxis
    {
        Horizontal,
        Vertical
    }

    public enum AerialSentryState
    {
        Patrolling,
        Acquiring,
        Pursuing,
        Charging,
        Cooldown,
        Searching,
        Returning,
        Stunned
    }

    internal enum AerialSentrySearchPhase
    {
        ApproachLastKnown,
        AlignDown,
        FirstSide,
        OppositeSide,
        ReturnDown
    }

    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CircleCollider2D))]
    [RequireComponent(typeof(AerialSentryVision))]
    [RequireComponent(typeof(Health))]
    public sealed class AerialSentryRobot : MonoBehaviour, IPlayerAttackReceiver
    {
        private const int MaxReturnBreadcrumbs = 512;
        private const int ProjectilePathCastCapacity = 12;

        private readonly struct SafeMoveResult
        {
            public SafeMoveResult(
                Vector2 nextPosition,
                Vector2 appliedDelta,
                Vector2 blockingNormal,
                bool blocked)
            {
                NextPosition = nextPosition;
                AppliedDelta = appliedDelta;
                BlockingNormal = blockingNormal;
                Blocked = blocked;
            }

            public Vector2 NextPosition { get; }
            public Vector2 AppliedDelta { get; }
            public Vector2 BlockingNormal { get; }
            public bool Blocked { get; }
        }

        private const string HoverLoopEventId = "enemy.aerial.hover.loop";
        private const string PatrolTurnEventId = "enemy.aerial.patrol.turn";
        private const string DetectEventId = "enemy.aerial.detect";
        private const string AlertEventId = "enemy.aerial.alert";
        private const string ChargeLoopEventId = "enemy.aerial.attack.charge";
        private const string FireEventId = "enemy.aerial.attack.fire";
        private const string DamageEventId = "enemy.aerial.damage";
        private const string DeathEventId = "enemy.aerial.death";

        [Header("Patrol")]
        [SerializeField] private AerialSentryPatrolAxis axis = AerialSentryPatrolAxis.Horizontal;
        [SerializeField, Min(0f)] private float patrolDistance = 4f;
        [SerializeField, Min(0f)] private float patrolSpeed = 2f;
        [SerializeField] private bool waitAtEnds = true;
        [SerializeField, Min(0f)] private float endWaitDuration = 0.35f;

        [Header("Pursuit")]
        [SerializeField, Min(0f)] private float chaseSpeed = 7f;
        [SerializeField, Min(0f)] private float chaseAcceleration = 32f;
        [SerializeField, Min(0f)] private float chaseDeceleration = 38f;
        [SerializeField, Min(0f)] private float chaseTurnAcceleration = 48f;
        [SerializeField, Min(0f)] private float hoverHeight = 2.5f;
        [SerializeField, Min(0f)] private float horizontalDeadZone = 0.4f;
        [SerializeField, Min(0f)] private float verticalDeadZone = 0.35f;
        [SerializeField, Min(0f)] private float maxHorizontalChaseSpeed = 9.5f;
        [SerializeField, Min(0f)] private float maxVerticalChaseSpeed = 7f;
        [SerializeField, Min(0f)] private float minimumGroundClearance = 1.25f;
        [SerializeField, Min(0.1f)] private float lostTargetTime = 4f;
        [SerializeField, Min(0f)] private float searchSweepStartDelay = 0.35f;
        [SerializeField, Min(0.01f)] private float searchArrivalRadius = 0.4f;
        [SerializeField, Min(1f)] private float searchSweepSpeed = 150f;
        [SerializeField, Min(0f)] private float returnSpeed = 3f;

        [Header("Navigation")]
        [SerializeField] private LayerMask navigationWorldMask = 10312;
        [SerializeField, Min(0f)] private float navigationSkin = 0.05f;
        [SerializeField, Min(0.1f)] private float blockedPursuitTimeout = 0.9f;
        [SerializeField, Min(0.1f)] private float blockedSearchTimeout = 0.6f;
        [SerializeField, Min(0.1f)] private float returnBlockedRetryDuration = 0.75f;
        [SerializeField, Min(0.1f)] private float returnBlockedTimeout = 3f;
        [SerializeField, Min(0.05f)] private float breadcrumbSpacing = 0.2f;
        [SerializeField, Min(0f)] private float blockedReacquireDelay = 1.25f;

        [Header("Hearing")]
        [SerializeField, Min(0f)] private float hearingHorizontalRange = 3f;
        [SerializeField, Min(0f)] private float hearingVerticalRange = 3.5f;
        [SerializeField, Min(0f)] private float hearingMovementSpeedThreshold = 0.5f;

        [Header("Attack")]
        [SerializeField] private Transform muzzle;
        [SerializeField] private AerialSentryProjectile projectilePrefab;
        [SerializeField, Min(0.1f)] private float effectiveFireRange = 5.5f;
        [SerializeField, Min(0.01f)] private float projectilePathRadius = 0.12f;
        [SerializeField, Min(0f)] private float chargeDuration = 0.45f;
        [SerializeField, Min(0.1f)] private float projectileSpeed = 7f;
        [SerializeField, Min(0f)] private float attackCooldown = 1.5f;
        [SerializeField, Tooltip("Fixed to one health unit for this enemy.")] private int damage = 1;
        [SerializeField, Min(0f)] private float stunDuration = 0.35f;
        [SerializeField, Min(0.1f)] private float projectileLifetime = 4f;
        [SerializeField] private LayerMask projectileWorldCollisionMask = 10312;

        [Header("Hit Reaction")]
        [SerializeField, Min(0f)] private float hitStunDuration = 0.35f;
        [SerializeField, Min(0f)] private float hitKnockbackHorizontal = 3.5f;
        [SerializeField, Min(0f)] private float hitKnockbackVertical = 1.5f;
        [SerializeField, Min(1f)] private float downHitSeparationMultiplier = 1.5f;
        [SerializeField, Min(0f)] private float downHitSeparationSafetyMargin = 0.6f;
        [SerializeField, Min(0.05f)] private float downHitSeparationTimeout = 0.75f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs;

        private Rigidbody2D body;
        private Collider2D ownerCollider;
        private AerialSentryVision vision;
        private Vector2 patrolCenter;
        private bool movingToPositiveEnd = true;
        private float endWaitTimer;
        private float stateTimer;
        private float lostSightTimer;
        private float attackCooldownTimer;
        private float timeWithoutSight;
        private float navigationBlockedTimer;
        private float returnBlockedTimer;
        private float returnBlockedTotalTimer;
        private float blockedReacquireTimer;
        private Vector2 visibleTarget;
        private Vector2 lastKnownPosition;
        private Vector2 returnTarget;
        private Vector2 pursuitVelocity;
        private bool hasTargetThisFrame;
        private bool playerSupportedThisFrame;
        private bool hasLastKnownPosition;
        private bool wasAlertBeforeStun;
        private bool searchSweepCompleteThisFrame;
        private int lastKnownHorizontalSide = 1;
        private int searchFirstSide = 1;
        private float searchApproachTimer;
        private float searchSweepAngle;
        private AerialSentrySearchPhase searchPhase = AerialSentrySearchPhase.ApproachLastKnown;
        private PlayerStandableSurface standableSurface;
        private Game.Player.PlayerController cachedPlayerController;
        private Rigidbody2D cachedPlayerBody;
        private Game.Player.StealthDebugIndicator cachedStealthDebugIndicator;
        private Health health;
        private PlayerAttackContext pendingPlayerAttack;
        private bool hasPendingPlayerAttack;
        private bool downHitSeparationActive;
        private int downHitSeparationDirection;
        private float downHitSeparationTargetX;
        private float downHitSeparationTimer;
        private int returnBreadcrumbIndex = -1;
        private readonly List<Vector2> returnBreadcrumbs = new List<Vector2>(128);
        private readonly RaycastHit2D[] groundClearanceHits = new RaycastHit2D[8];
        private readonly RaycastHit2D[] separationHits = new RaycastHit2D[8];
        private readonly RaycastHit2D[] navigationHits = new RaycastHit2D[16];
        private readonly RaycastHit2D[] projectilePathHits =
            new RaycastHit2D[ProjectilePathCastCapacity];
        private AudioPlaybackHandle hoverLoopHandle;
        private AudioPlaybackHandle chargeLoopHandle;

        public AerialSentryState CurrentState { get; private set; } = AerialSentryState.Patrolling;
        public float ChargeProgress => CurrentState != AerialSentryState.Charging
            ? 0f
            : chargeDuration <= 0f
                ? 1f
                : Mathf.Clamp01(stateTimer / chargeDuration);
        public float CooldownProgress => attackCooldown <= 0f
            ? 1f
            : 1f - Mathf.Clamp01(attackCooldownTimer / attackCooldown);
        public Vector2 ChargeTarget => visibleTarget;
        public Vector2 MuzzlePosition => muzzle != null ? muzzle.position : transform.position;
        public Vector2 LastKnownPosition => lastKnownPosition;
        public Vector2 CombatHoverPosition => GetHoverPosition(visibleTarget);
        public Vector2 SearchHoverPosition => GetHoverPosition(lastKnownPosition);
        public float TimeWithoutSight => timeWithoutSight;
        public bool HasLastKnownPosition => hasLastKnownPosition;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            ownerCollider = GetComponent<Collider2D>();
            vision = GetComponent<AerialSentryVision>();
            standableSurface = GetComponent<PlayerStandableSurface>();
            health = GetComponent<Health>();
            patrolCenter = body.position;

            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.constraints |= RigidbodyConstraints2D.FreezeRotation;
        }

        private void OnEnable()
        {
            if (health == null)
                health = GetComponent<Health>();
            if (health != null)
            {
                health.OnDamaged += HandleDamaged;
                health.OnDied += HandleDied;
            }

            EnsureLoop(ref hoverLoopHandle, HoverLoopEventId);
        }

        private void Update()
        {
            EnsureLoop(ref hoverLoopHandle, HoverLoopEventId);
            if (CurrentState == AerialSentryState.Charging)
                EnsureLoop(ref chargeLoopHandle, ChargeLoopEventId);

            if (attackCooldownTimer > 0f)
                attackCooldownTimer = Mathf.Max(0f, attackCooldownTimer - Time.deltaTime);
            if (blockedReacquireTimer > 0f)
                blockedReacquireTimer = Mathf.Max(0f, blockedReacquireTimer - Time.deltaTime);

            searchSweepCompleteThisFrame = false;
            if (CurrentState == AerialSentryState.Stunned)
            {
                hasTargetThisFrame = false;
                playerSupportedThisFrame = false;
                stateTimer += Time.deltaTime;
                if (stateTimer >= hitStunDuration && !downHitSeparationActive)
                    RecoverFromStun();
                return;
            }

            UpdateVisionDirection(Time.deltaTime);
            bool hasSight = vision.TryGetVisibleTarget(out Vector2 targetPoint, out AerialSentrySightFailure failure);
            bool playerSupported = IsPlayerSupportedByThisSentry();
            bool hasTarget = hasSight || playerSupported;
            hasTargetThisFrame = hasTarget;
            playerSupportedThisFrame = playerSupported;
            bool hasEffectiveShot = HasEffectiveShot(hasTarget, targetPoint);

            if (hasTarget)
            {
                if (hasSight)
                    ReportPlayerVisible();
                visibleTarget = targetPoint;
                lastKnownPosition = targetPoint;
                hasLastKnownPosition = true;
                timeWithoutSight = 0f;
                float horizontalOffset = targetPoint.x - body.position.x;
                if (Mathf.Abs(horizontalOffset) > 0.05f)
                    lastKnownHorizontalSide = horizontalOffset < 0f ? -1 : 1;
            }
            else if (UsesAlertMemory(CurrentState))
            {
                timeWithoutSight += Time.deltaTime;
            }

            if (!hasTarget && blockedReacquireTimer <= 0f &&
                TryHearPlayer(out Vector2 heardPosition))
                ReactToPlayerHearing(heardPosition);

            switch (CurrentState)
            {
                case AerialSentryState.Patrolling:
                    if (hasTarget)
                    {
                        TransitionTo(AerialSentryState.Acquiring);
                        FocusVisionOn(targetPoint, Time.deltaTime);
                        LogDetected();
                    }
                    break;

                case AerialSentryState.Acquiring:
                    if (!hasTarget)
                    {
                        LogLost(failure);
                        hasLastKnownPosition = false;
                        timeWithoutSight = 0f;
                        TransitionTo(AerialSentryState.Patrolling);
                        break;
                    }

                    stateTimer += Time.deltaTime;
                    if (stateTimer >= vision.DetectionAcquireTime)
                    {
                        TransitionTo(hasEffectiveShot
                            ? AerialSentryState.Charging
                            : AerialSentryState.Pursuing);
                    }
                    break;

                case AerialSentryState.Pursuing:
                    if (!hasTarget)
                    {
                        TransitionTo(AerialSentryState.Searching);
                        break;
                    }

                    if (hasEffectiveShot && attackCooldownTimer <= 0f)
                        TransitionTo(AerialSentryState.Charging);
                    break;

                case AerialSentryState.Charging:
                    stateTimer += Time.deltaTime;
                    if (hasEffectiveShot)
                    {
                        lostSightTimer = 0f;
                    }
                    else
                    {
                        lostSightTimer += Time.deltaTime;
                        if (lostSightTimer > vision.LostSightGrace)
                        {
                            LogChargeCancel(failure);
                            TransitionTo(hasTarget
                                ? AerialSentryState.Pursuing
                                : AerialSentryState.Searching);
                            break;
                        }
                    }

                    if (stateTimer >= chargeDuration && hasEffectiveShot)
                        FireAt(visibleTarget);
                    break;

                case AerialSentryState.Cooldown:
                    TransitionTo(hasTarget
                        ? AerialSentryState.Pursuing
                        : AerialSentryState.Searching);
                    break;

                case AerialSentryState.Searching:
                    if (hasTarget)
                    {
                        LogReacquiredDuringSearch();
                        FocusVisionOn(targetPoint, Time.deltaTime);
                        TransitionTo(hasEffectiveShot && attackCooldownTimer <= 0f
                            ? AerialSentryState.Charging
                            : AerialSentryState.Pursuing);
                        break;
                    }

                    if (searchSweepCompleteThisFrame)
                        BeginReturnToPatrol();
                    break;

                case AerialSentryState.Returning:
                    if (hasTarget && blockedReacquireTimer <= 0f)
                    {
                        TransitionTo(AerialSentryState.Acquiring);
                        FocusVisionOn(targetPoint, Time.deltaTime);
                        LogDetected();
                    }
                    break;
            }
        }

        private void FixedUpdate()
        {
            if (standableSurface != null)
                standableSurface.SetMovementDelta(Vector2.zero);

            switch (CurrentState)
            {
                case AerialSentryState.Patrolling:
                case AerialSentryState.Acquiring:
                    UpdatePatrol();
                    break;
                case AerialSentryState.Pursuing:
                    UpdatePursuit(visibleTarget);
                    break;
                case AerialSentryState.Charging:
                    break;
                case AerialSentryState.Cooldown:
                    UpdateAlertMovement();
                    break;
                case AerialSentryState.Searching:
                    UpdateSearch();
                    break;
                case AerialSentryState.Returning:
                    UpdateReturnToPatrol();
                    break;
                case AerialSentryState.Stunned:
                    UpdateStunMovement();
                    break;
            }
        }

        private void UpdatePatrol()
        {
            if (patrolDistance <= 0f || patrolSpeed <= 0f)
                return;

            if (endWaitTimer > 0f)
            {
                endWaitTimer = Mathf.Max(0f, endWaitTimer - Time.fixedDeltaTime);
                return;
            }

            Vector2 patrolDirection = axis == AerialSentryPatrolAxis.Horizontal
                ? Vector2.right
                : Vector2.up;
            float endSign = movingToPositiveEnd ? 1f : -1f;
            Vector2 destination = patrolCenter + patrolDirection * (patrolDistance * 0.5f * endSign);
            Vector2 nextPosition = Vector2.MoveTowards(
                body.position,
                destination,
                patrolSpeed * Time.fixedDeltaTime);

            SafeMoveResult move = MoveBodySafely(
                nextPosition,
                false,
                CurrentState == AerialSentryState.Acquiring);
            if (move.Blocked)
            {
                TurnPatrolAround();
                return;
            }

            if ((move.NextPosition - destination).sqrMagnitude > 0.000001f)
                return;

            TurnPatrolAround();
        }

        private void TurnPatrolAround()
        {
            movingToPositiveEnd = !movingToPositiveEnd;
            AudioService.PlayAtAcoustic(PatrolTurnEventId, transform.position);
            if (waitAtEnds)
                endWaitTimer = endWaitDuration;
        }

        private void UpdatePursuit(Vector2 target)
        {
            if (HasEffectiveShot(hasTargetThisFrame, target))
            {
                MoveWithPursuitAcceleration(Vector2.zero, true, true);
                navigationBlockedTimer = 0f;
                return;
            }

            if (playerSupportedThisFrame)
            {
                MoveWithPursuitAcceleration(Vector2.zero, true, true);
                navigationBlockedTimer += Time.fixedDeltaTime;
                if (navigationBlockedTimer >= blockedPursuitTimeout)
                    AbandonBlockedNavigation();
                return;
            }

            Vector2 pursuitTarget = GetHoverPosition(target);
            float previousDistance = Vector2.Distance(body.position, pursuitTarget);
            Vector2 desiredVelocity = GetHoverDesiredVelocity(pursuitTarget);
            SafeMoveResult move = MoveWithPursuitAcceleration(desiredVelocity, true, true);
            if (MadeProgress(previousDistance, move.NextPosition, pursuitTarget))
            {
                navigationBlockedTimer = 0f;
                return;
            }

            navigationBlockedTimer += Time.fixedDeltaTime;
            if (navigationBlockedTimer >= blockedPursuitTimeout)
                AbandonBlockedNavigation();
        }

        private void UpdateAlertMovement()
        {
            if (hasTargetThisFrame)
            {
                UpdatePursuit(visibleTarget);
                return;
            }

            UpdateSearch();
        }

        private void UpdateSearch()
        {
            if (!hasLastKnownPosition)
            {
                MoveWithPursuitAcceleration(Vector2.zero, true, true);
                return;
            }

            Vector2 searchHoverPosition = SearchHoverPosition;
            Vector2 desiredVelocity = Vector2.Distance(body.position, searchHoverPosition) > searchArrivalRadius
                ? GetHoverDesiredVelocity(searchHoverPosition)
                : Vector2.zero;
            float previousDistance = Vector2.Distance(body.position, searchHoverPosition);
            SafeMoveResult move = MoveWithPursuitAcceleration(desiredVelocity, true, true);
            if (previousDistance <= searchArrivalRadius ||
                MadeProgress(previousDistance, move.NextPosition, searchHoverPosition))
            {
                navigationBlockedTimer = 0f;
                return;
            }

            navigationBlockedTimer += Time.fixedDeltaTime;
            if (navigationBlockedTimer >= blockedSearchTimeout)
                AbandonBlockedNavigation();
        }

        private void UpdateVisionDirection(float deltaTime)
        {
            switch (CurrentState)
            {
                case AerialSentryState.Patrolling:
                case AerialSentryState.Returning:
                    vision.SetViewDirection(Vector2.down);
                    break;
                case AerialSentryState.Searching:
                    UpdateSearchVision(deltaTime);
                    break;
                case AerialSentryState.Acquiring:
                case AerialSentryState.Pursuing:
                case AerialSentryState.Charging:
                case AerialSentryState.Cooldown:
                    if (hasLastKnownPosition)
                        FocusVisionOn(visibleTarget, deltaTime);
                    break;
            }
        }

        private void FocusVisionOn(Vector2 targetPoint, float deltaTime)
        {
            vision.RotateViewDirectionTowards(targetPoint - vision.Origin, deltaTime);
        }

        private void UpdateSearchVision(float deltaTime)
        {
            if (!hasLastKnownPosition)
            {
                searchSweepCompleteThisFrame = true;
                return;
            }

            switch (searchPhase)
            {
                case AerialSentrySearchPhase.ApproachLastKnown:
                    searchApproachTimer += deltaTime;
                    vision.RotateViewDirectionTowards(
                        lastKnownPosition - vision.Origin,
                        deltaTime);
                    if (searchApproachTimer >= searchSweepStartDelay ||
                        searchApproachTimer >= lostTargetTime)
                        SetSearchPhase(AerialSentrySearchPhase.AlignDown);
                    break;
                case AerialSentrySearchPhase.AlignDown:
                    if (vision.RotateViewDirectionTowards(
                            Vector2.down,
                            searchSweepSpeed,
                            deltaTime))
                    {
                        searchSweepAngle = 0f;
                        SetSearchPhase(AerialSentrySearchPhase.FirstSide);
                    }
                    break;
                case AerialSentrySearchPhase.FirstSide:
                    if (UpdateSearchSweepAngle(searchFirstSide * 90f, deltaTime))
                    {
                        SetSearchPhase(AerialSentrySearchPhase.OppositeSide);
                    }
                    break;
                case AerialSentrySearchPhase.OppositeSide:
                    if (UpdateSearchSweepAngle(searchFirstSide * -90f, deltaTime))
                    {
                        SetSearchPhase(AerialSentrySearchPhase.ReturnDown);
                    }
                    break;
                case AerialSentrySearchPhase.ReturnDown:
                    if (UpdateSearchSweepAngle(0f, deltaTime))
                    {
                        vision.SetViewDirection(Vector2.down);
                        searchSweepCompleteThisFrame = true;
                    }
                    break;
            }
        }

        private bool UpdateSearchSweepAngle(float targetAngle, float deltaTime)
        {
            searchSweepAngle = Mathf.MoveTowards(
                searchSweepAngle,
                targetAngle,
                searchSweepSpeed * Mathf.Max(0f, deltaTime));
            vision.SetViewDirection(
                Quaternion.Euler(0f, 0f, searchSweepAngle) * Vector2.down);
            return Mathf.Approximately(searchSweepAngle, targetAngle);
        }

        private void BeginSearch()
        {
            searchApproachTimer = 0f;
            searchSweepAngle = 0f;
            searchSweepCompleteThisFrame = false;
            searchPhase = AerialSentrySearchPhase.ApproachLastKnown;

            float horizontalOffset = lastKnownPosition.x - body.position.x;
            searchFirstSide = Mathf.Abs(horizontalOffset) > 0.05f
                ? (horizontalOffset < 0f ? -1 : 1)
                : lastKnownHorizontalSide;
        }

        private void SetSearchPhase(AerialSentrySearchPhase nextPhase)
        {
            if (searchPhase == nextPhase)
                return;

            searchPhase = nextPhase;
            LogSearchPhase(nextPhase);
        }

        private Vector2 GetHoverPosition(Vector2 playerPosition)
        {
            return playerPosition + Vector2.up * hoverHeight;
        }

        private Vector2 GetHoverDesiredVelocity(Vector2 hoverPosition)
        {
            Vector2 error = hoverPosition - body.position;
            return new Vector2(
                GetAxisDesiredVelocity(error.x, horizontalDeadZone, maxHorizontalChaseSpeed),
                GetAxisDesiredVelocity(error.y, verticalDeadZone, maxVerticalChaseSpeed));
        }

        private float GetAxisDesiredVelocity(float error, float deadZone, float maximumSpeed)
        {
            float distanceOutsideDeadZone = Mathf.Abs(error) - deadZone;
            if (distanceOutsideDeadZone <= 0f || maximumSpeed <= 0f)
                return 0f;

            float desiredSpeed = Mathf.Min(maximumSpeed, distanceOutsideDeadZone * chaseSpeed);
            return Mathf.Sign(error) * desiredSpeed;
        }

        private bool HasEffectiveShot(bool hasTarget, Vector2 targetPoint)
        {
            if (!hasTarget || effectiveFireRange <= 0f)
                return false;

            float projectileTravelRange = projectileSpeed * projectileLifetime;
            float allowedRange = Mathf.Min(effectiveFireRange, projectileTravelRange);
            if (((Vector2)MuzzlePosition - targetPoint).sqrMagnitude >
                allowedRange * allowedRange)
            {
                return false;
            }

            return HasClearProjectilePath(targetPoint);
        }

        private bool HasClearProjectilePath(Vector2 targetPoint)
        {
            Vector2 origin = MuzzlePosition;
            Vector2 toTarget = targetPoint - origin;
            float distance = toTarget.magnitude;
            if (distance <= 0.0001f || projectileWorldCollisionMask.value == 0)
                return true;

            int hitCount = Physics2D.CircleCastNonAlloc(
                origin,
                projectilePathRadius,
                toTarget / distance,
                projectilePathHits,
                distance,
                projectileWorldCollisionMask);
            for (int i = 0; i < hitCount; i++)
            {
                Collider2D hitCollider = projectilePathHits[i].collider;
                if (hitCollider == null || hitCollider.isTrigger)
                    continue;
                if (hitCollider.transform == transform ||
                    hitCollider.transform.IsChildOf(transform))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private bool IsPlayerSupportedByThisSentry()
        {
            ResolvePlayerRuntimeComponents();
            if (cachedPlayerController == null || standableSurface == null)
                return false;

            Collider2D currentGround = cachedPlayerController.CurrentGroundCollider;
            return cachedPlayerController.IsGrounded &&
                   standableSurface.Accepts(currentGround);
        }

        private void BeginReturnToPatrol()
        {
            if (CurrentState == AerialSentryState.Returning)
                return;

            hasLastKnownPosition = false;
            timeWithoutSight = 0f;
            navigationBlockedTimer = 0f;
            returnBlockedTimer = 0f;
            returnBlockedTotalTimer = 0f;
            pursuitVelocity = Vector2.zero;
            vision.SetViewDirection(Vector2.down);
            returnTarget = GetNearestPointOnPatrolRoute(body.position);
            if (returnBreadcrumbs.Count > 0)
            {
                RecordReturnBreadcrumb(body.position, true);
                returnBreadcrumbIndex = returnBreadcrumbs.Count - 2;
            }
            else
            {
                returnBreadcrumbIndex = -1;
            }
            TransitionTo(AerialSentryState.Returning);
        }

        private Vector2 GetNearestPointOnPatrolRoute(Vector2 position)
        {
            float halfDistance = patrolDistance * 0.5f;
            if (axis == AerialSentryPatrolAxis.Horizontal)
            {
                return new Vector2(
                    Mathf.Clamp(position.x, patrolCenter.x - halfDistance, patrolCenter.x + halfDistance),
                    patrolCenter.y);
            }

            return new Vector2(
                patrolCenter.x,
                Mathf.Clamp(position.y, patrolCenter.y - halfDistance, patrolCenter.y + halfDistance));
        }

        private void UpdateReturnToPatrol()
        {
            bool hasBreadcrumbRoute = returnBreadcrumbs.Count > 0;
            if (hasBreadcrumbRoute && returnBreadcrumbIndex < 0)
            {
                CompleteReturnToPatrol();
                return;
            }

            Vector2 target = hasBreadcrumbRoute
                ? returnBreadcrumbs[returnBreadcrumbIndex]
                : returnTarget;
            Vector2 toTarget = target - body.position;
            if (toTarget.magnitude <= searchArrivalRadius)
            {
                if (hasBreadcrumbRoute)
                    returnBreadcrumbIndex--;
                else
                    CompleteReturnToPatrol();

                returnBlockedTimer = 0f;
                returnBlockedTotalTimer = 0f;
                return;
            }

            float previousDistance = toTarget.magnitude;
            SafeMoveResult move = MoveWithPursuitAcceleration(
                toTarget.normalized * returnSpeed,
                true,
                false);
            if (MadeProgress(previousDistance, move.NextPosition, target))
            {
                returnBlockedTimer = 0f;
                returnBlockedTotalTimer = 0f;
                return;
            }

            returnBlockedTimer += Time.fixedDeltaTime;
            returnBlockedTotalTimer += Time.fixedDeltaTime;
            if (returnBlockedTimer >= returnBlockedRetryDuration)
            {
                returnBlockedTimer = 0f;
                if (hasBreadcrumbRoute)
                    TrySelectEarlierReachableBreadcrumb();
            }

            if (returnBlockedTotalTimer >= returnBlockedTimeout)
                ResumeSafeLocalPatrol();
        }

        private SafeMoveResult MoveWithPursuitAcceleration(
            Vector2 desiredVelocity,
            bool allowSlide,
            bool recordBreadcrumb)
        {
            desiredVelocity = ApplyMinimumGroundClearance(desiredVelocity);
            pursuitVelocity.x = Mathf.MoveTowards(
                pursuitVelocity.x,
                desiredVelocity.x,
                GetVelocityChangeRate(pursuitVelocity.x, desiredVelocity.x) *
                Time.fixedDeltaTime);
            pursuitVelocity.y = Mathf.MoveTowards(
                pursuitVelocity.y,
                desiredVelocity.y,
                GetVelocityChangeRate(pursuitVelocity.y, desiredVelocity.y) *
                Time.fixedDeltaTime);

            SafeMoveResult move = MoveBodySafely(
                body.position + pursuitVelocity * Time.fixedDeltaTime,
                allowSlide,
                recordBreadcrumb);
            if (move.Blocked)
                RemoveVelocityIntoSurface(ref pursuitVelocity, move.BlockingNormal);
            return move;
        }

        private float GetVelocityChangeRate(float current, float desired)
        {
            if (Mathf.Abs(desired) <= 0.0001f || Mathf.Abs(desired) < Mathf.Abs(current))
                return chaseDeceleration;
            if (Mathf.Abs(current) > 0.0001f && Mathf.Sign(current) != Mathf.Sign(desired))
                return chaseTurnAcceleration;
            return chaseAcceleration;
        }

        private void UpdateStunMovement()
        {
            Vector2 movementVelocity = ApplyMinimumGroundClearance(pursuitVelocity);
            bool maintainHorizontalSeparation = TryApplyDownHitSeparation(ref movementVelocity);
            SafeMoveResult move = MoveBodySafely(
                body.position + movementVelocity * Time.fixedDeltaTime,
                true,
                wasAlertBeforeStun);
            if (move.Blocked)
            {
                RemoveVelocityIntoSurface(ref pursuitVelocity, move.BlockingNormal);
                RemoveVelocityIntoSurface(ref movementVelocity, move.BlockingNormal);
                if (Mathf.Abs(movementVelocity.x) <= 0.0001f)
                    downHitSeparationActive = false;
            }

            float horizontalDeceleration = hitStunDuration > 0f
                ? hitKnockbackHorizontal / hitStunDuration
                : hitKnockbackHorizontal;
            float verticalDeceleration = hitStunDuration > 0f
                ? hitKnockbackVertical / hitStunDuration
                : hitKnockbackVertical;
            pursuitVelocity.x = maintainHorizontalSeparation
                ? movementVelocity.x
                : Mathf.MoveTowards(
                    pursuitVelocity.x,
                    0f,
                    horizontalDeceleration * Time.fixedDeltaTime);
            pursuitVelocity.y = Mathf.MoveTowards(
                pursuitVelocity.y,
                0f,
                verticalDeceleration * Time.fixedDeltaTime);
        }

        private bool TryApplyDownHitSeparation(ref Vector2 movementVelocity)
        {
            if (!downHitSeparationActive)
                return false;

            downHitSeparationTimer += Time.fixedDeltaTime;
            float signedRemaining =
                (downHitSeparationTargetX - body.position.x) * downHitSeparationDirection;
            if (signedRemaining <= 0.02f)
            {
                downHitSeparationActive = false;
                return false;
            }

            float speed = Mathf.Max(
                hitKnockbackHorizontal * downHitSeparationMultiplier,
                Mathf.Abs(movementVelocity.x));
            float desiredStep = downHitSeparationDirection *
                Mathf.Min(signedRemaining, speed * Time.fixedDeltaTime);
            float safeStep = GetSafeHorizontalSeparationStep(desiredStep);
            if (Mathf.Abs(safeStep) <= 0.0001f)
            {
                downHitSeparationActive = false;
                movementVelocity.x = 0f;
                return false;
            }

            if (downHitSeparationTimer >= downHitSeparationTimeout)
            {
                downHitSeparationActive = false;
                movementVelocity.x = 0f;
                return false;
            }

            movementVelocity.x = safeStep / Time.fixedDeltaTime;
            return true;
        }

        private float GetSafeHorizontalSeparationStep(float desiredStep)
        {
            float distance = Mathf.Abs(desiredStep);
            if (distance <= 0f || ownerCollider == null || vision == null)
                return desiredStep;

            int worldMask = navigationWorldMask.value;
            if (worldMask == 0)
                return desiredStep;

            int direction = desiredStep < 0f ? -1 : 1;
            ContactFilter2D filter = new ContactFilter2D();
            filter.SetLayerMask(worldMask);
            filter.useLayerMask = true;
            filter.useTriggers = false;
            float allowedDistance = GetAllowedCastDistance(
                ownerCollider,
                direction,
                distance,
                filter);
            Collider2D supportCollider = standableSurface != null
                ? standableSurface.SurfaceCollider
                : null;
            if (supportCollider != null && supportCollider != ownerCollider)
            {
                allowedDistance = Mathf.Min(
                    allowedDistance,
                    GetAllowedCastDistance(
                        supportCollider,
                        direction,
                        distance,
                        filter));
            }

            return direction * allowedDistance;
        }

        private float GetAllowedCastDistance(
            Collider2D colliderToCast,
            int direction,
            float distance,
            ContactFilter2D filter)
        {
            int count = colliderToCast.Cast(
                Vector2.right * direction,
                filter,
                separationHits,
                distance + 0.02f);
            float allowedDistance = distance;
            for (int i = 0; i < count; i++)
            {
                RaycastHit2D hit = separationHits[i];
                if (hit.collider == null || hit.collider.isTrigger)
                    continue;
                if (hit.collider == colliderToCast ||
                    hit.collider.transform.IsChildOf(transform))
                    continue;

                allowedDistance = Mathf.Min(
                    allowedDistance,
                    Mathf.Max(0f, hit.distance - 0.02f));
            }

            return allowedDistance;
        }

        private Vector2 ApplyMinimumGroundClearance(Vector2 desiredVelocity)
        {
            if (minimumGroundClearance <= 0f || vision == null)
                return desiredVelocity;

            float projectedDescent = Mathf.Max(
                Mathf.Max(0f, -desiredVelocity.y),
                Mathf.Max(0f, -pursuitVelocity.y)) * Time.fixedDeltaTime;
            float probeDistance = minimumGroundClearance + projectedDescent + 0.05f;
            if (!TryGetGroundClearance(probeDistance, out float clearance))
                return desiredVelocity;

            if (clearance < minimumGroundClearance)
            {
                float correctionSpeed = Mathf.Min(
                    maxVerticalChaseSpeed,
                    (minimumGroundClearance - clearance) * chaseSpeed);
                desiredVelocity.y = Mathf.Max(desiredVelocity.y, correctionSpeed);
                if (pursuitVelocity.y < 0f)
                    pursuitVelocity.y = 0f;
            }
            else if ((desiredVelocity.y < 0f || pursuitVelocity.y < 0f) &&
                     clearance <= minimumGroundClearance + projectedDescent)
            {
                desiredVelocity.y = 0f;
                if (pursuitVelocity.y < 0f)
                    pursuitVelocity.y = 0f;
            }

            return desiredVelocity;
        }

        private bool TryGetGroundClearance(float probeDistance, out float clearance)
        {
            clearance = float.PositiveInfinity;
            int worldMask = navigationWorldMask.value;
            if (worldMask == 0)
                return false;

            Vector2 origin = GetGroundClearanceProbeOrigin();
            int hitCount = Physics2D.RaycastNonAlloc(
                origin,
                Vector2.down,
                groundClearanceHits,
                probeDistance,
                worldMask);

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D candidate = groundClearanceHits[i];
                if (candidate.collider == null || candidate.collider.isTrigger)
                    continue;
                if (candidate.collider == ownerCollider ||
                    candidate.collider.transform.IsChildOf(transform))
                    continue;
                if (candidate.distance >= clearance)
                    continue;

                clearance = candidate.distance;
            }

            return !float.IsPositiveInfinity(clearance);
        }

        private Vector2 GetGroundClearanceProbeOrigin()
        {
            if (ownerCollider == null)
                return body != null ? body.position : (Vector2)transform.position;

            Bounds bounds = ownerCollider.bounds;
            return new Vector2(bounds.center.x, bounds.min.y);
        }

        private SafeMoveResult MoveBodySafely(
            Vector2 requestedPosition,
            bool allowSlide,
            bool recordBreadcrumb)
        {
            Vector2 origin = body.position;
            Vector2 requestedDelta = requestedPosition - origin;
            if (requestedDelta.sqrMagnitude <= 0.000001f)
                return new SafeMoveResult(origin, Vector2.zero, Vector2.zero, false);

            Vector2 appliedDelta = ResolveSafeMovementDelta(
                requestedDelta,
                allowSlide,
                out Vector2 blockingNormal,
                out bool blocked);
            Vector2 nextPosition = origin + appliedDelta;

            if (standableSurface != null)
                standableSurface.SetMovementDelta(appliedDelta);

            if (appliedDelta.sqrMagnitude > 0.000001f)
            {
                body.MovePosition(nextPosition);
                if (recordBreadcrumb)
                    RecordReturnBreadcrumb(nextPosition, false);
            }

            return new SafeMoveResult(
                nextPosition,
                appliedDelta,
                blockingNormal,
                blocked);
        }

        private Vector2 ResolveSafeMovementDelta(
            Vector2 requestedDelta,
            bool allowSlide,
            out Vector2 blockingNormal,
            out bool blocked)
        {
            float requestedDistance = requestedDelta.magnitude;
            Vector2 requestedDirection = requestedDelta / requestedDistance;
            float allowedDistance = GetAllowedNavigationDistance(
                requestedDirection,
                requestedDistance,
                out blockingNormal);
            Vector2 bestDelta = requestedDirection * allowedDistance;
            blocked = allowedDistance + 0.0001f < requestedDistance;

            if (!blocked || !allowSlide || blockingNormal.sqrMagnitude <= 0.0001f)
                return bestDelta;

            float intoSurface = Vector2.Dot(requestedDelta, blockingNormal);
            if (intoSurface >= -0.0001f)
                return bestDelta;

            Vector2 slideDelta = requestedDelta - blockingNormal * intoSurface;
            float slideDistance = slideDelta.magnitude;
            if (slideDistance <= 0.0001f)
                return bestDelta;

            Vector2 slideDirection = slideDelta / slideDistance;
            float allowedSlideDistance = GetAllowedNavigationDistance(
                slideDirection,
                slideDistance,
                out _);
            Vector2 safeSlideDelta = slideDirection * allowedSlideDistance;
            float directProgress = Vector2.Dot(bestDelta, requestedDirection);
            float slideProgress = Vector2.Dot(safeSlideDelta, requestedDirection);
            return slideProgress > directProgress + 0.0001f
                ? safeSlideDelta
                : bestDelta;
        }

        private float GetAllowedNavigationDistance(
            Vector2 direction,
            float requestedDistance,
            out Vector2 nearestNormal)
        {
            nearestNormal = Vector2.zero;
            if (requestedDistance <= 0f || navigationWorldMask.value == 0)
                return requestedDistance;

            ContactFilter2D filter = new ContactFilter2D();
            filter.SetLayerMask(navigationWorldMask);
            filter.useLayerMask = true;
            filter.useTriggers = false;

            float allowedDistance = requestedDistance;
            allowedDistance = GetAllowedNavigationDistance(
                ownerCollider,
                direction,
                requestedDistance,
                allowedDistance,
                filter,
                ref nearestNormal);

            Collider2D supportCollider = standableSurface != null
                ? standableSurface.SurfaceCollider
                : null;
            if (supportCollider != null && supportCollider != ownerCollider)
            {
                allowedDistance = GetAllowedNavigationDistance(
                    supportCollider,
                    direction,
                    requestedDistance,
                    allowedDistance,
                    filter,
                    ref nearestNormal);
            }

            return allowedDistance;
        }

        private float GetAllowedNavigationDistance(
            Collider2D colliderToCast,
            Vector2 direction,
            float requestedDistance,
            float currentAllowedDistance,
            ContactFilter2D filter,
            ref Vector2 nearestNormal)
        {
            if (colliderToCast == null || !colliderToCast.enabled)
                return currentAllowedDistance;

            int hitCount = colliderToCast.Cast(
                direction,
                filter,
                navigationHits,
                requestedDistance + navigationSkin);
            float allowedDistance = currentAllowedDistance;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = navigationHits[i];
                Collider2D hitCollider = hit.collider;
                if (hitCollider == null || hitCollider.isTrigger)
                    continue;
                if (hitCollider == colliderToCast ||
                    hitCollider.transform == transform ||
                    hitCollider.transform.IsChildOf(transform))
                {
                    continue;
                }

                float candidateDistance = Mathf.Max(0f, hit.distance - navigationSkin);
                if (candidateDistance >= allowedDistance)
                    continue;

                allowedDistance = candidateDistance;
                nearestNormal = hit.normal;
            }

            return allowedDistance;
        }

        private static void RemoveVelocityIntoSurface(
            ref Vector2 velocity,
            Vector2 surfaceNormal)
        {
            if (surfaceNormal.sqrMagnitude <= 0.0001f)
                return;

            float intoSurface = Vector2.Dot(velocity, surfaceNormal);
            if (intoSurface < 0f)
                velocity -= surfaceNormal * intoSurface;
        }

        private static bool MadeProgress(
            float previousDistance,
            Vector2 nextPosition,
            Vector2 target)
        {
            return previousDistance - Vector2.Distance(nextPosition, target) > 0.005f;
        }

        private void AbandonBlockedNavigation()
        {
            blockedReacquireTimer = Mathf.Max(
                blockedReacquireTimer,
                blockedReacquireDelay);
            BeginReturnToPatrol();
        }

        private void RecordReturnBreadcrumb(Vector2 position, bool force)
        {
            if (returnBreadcrumbs.Count == 0)
            {
                returnBreadcrumbs.Add(position);
                return;
            }

            float minimumDistance = force ? 0.01f : breadcrumbSpacing;
            Vector2 previous = returnBreadcrumbs[returnBreadcrumbs.Count - 1];
            if ((position - previous).sqrMagnitude < minimumDistance * minimumDistance)
                return;
            if (returnBreadcrumbs.Count >= MaxReturnBreadcrumbs)
                return;

            returnBreadcrumbs.Add(position);
        }

        private bool TrySelectEarlierReachableBreadcrumb()
        {
            for (int i = returnBreadcrumbIndex - 1; i >= 0; i--)
            {
                if (!IsNavigationSegmentClear(body.position, returnBreadcrumbs[i]))
                    continue;

                returnBreadcrumbIndex = i;
                return true;
            }

            return false;
        }

        private bool IsNavigationSegmentClear(Vector2 start, Vector2 end)
        {
            Vector2 offset = end - start;
            float distance = offset.magnitude;
            if (distance <= 0.0001f)
                return true;

            float allowedDistance = GetAllowedNavigationDistance(
                offset / distance,
                distance,
                out _);
            return allowedDistance + 0.0001f >= distance;
        }

        private void CompleteReturnToPatrol()
        {
            pursuitVelocity = Vector2.zero;
            navigationBlockedTimer = 0f;
            returnBlockedTimer = 0f;
            returnBlockedTotalTimer = 0f;
            returnBreadcrumbIndex = -1;
            returnBreadcrumbs.Clear();
            TransitionTo(AerialSentryState.Patrolling);
        }

        private void ResumeSafeLocalPatrol()
        {
            patrolCenter = body.position;
            movingToPositiveEnd = true;
            CompleteReturnToPatrol();

            if (debugLogs)
            {
                Debug.LogWarning(
                    "[AERIAL NAV] Return path remained blocked; " +
                    "resuming a local patrol from the last safe position.",
                    this);
            }
        }

        private static bool UsesAlertMemory(AerialSentryState state)
        {
            return state == AerialSentryState.Pursuing ||
                   state == AerialSentryState.Charging ||
                   state == AerialSentryState.Cooldown ||
                   state == AerialSentryState.Searching;
        }

        public PlayerAttackResolution ReceivePlayerAttack(PlayerAttackContext context)
        {
            if (health == null || health.CurrentHP <= 0)
                return PlayerAttackResolution.None;

            int healthBefore = health.CurrentHP;
            pendingPlayerAttack = context;
            hasPendingPlayerAttack = true;
            try
            {
                health.TakeDamage(1, context.Origin);
            }
            finally
            {
                hasPendingPlayerAttack = false;
            }

            int healthAfter = health.CurrentHP;
            bool applied = healthAfter < healthBefore;
            LogPlayerAttack(
                healthBefore,
                healthAfter,
                context.Direction,
                context.AttackId,
                applied);
            if (applied && context.Direction == PlayerAttackDirection.Down)
                LogDownAttack(context.IsCurrentSupport);

            return applied ? PlayerAttackResolution.Damage : PlayerAttackResolution.None;
        }

        private void HandleDamaged(Health damagedHealth, int receivedDamage, Vector2 sourcePosition)
        {
            if (damagedHealth == null)
                return;

            LogHit(damagedHealth.CurrentHP);
            if (damagedHealth.CurrentHP > 0)
                AudioService.PlayAtAcoustic(DamageEventId, transform.position);

            if (CurrentState != AerialSentryState.Stunned)
            {
                wasAlertBeforeStun = CurrentState == AerialSentryState.Acquiring ||
                                     UsesAlertMemory(CurrentState);
            }

            attackCooldownTimer = Mathf.Max(attackCooldownTimer, attackCooldown);
            TransitionTo(AerialSentryState.Stunned);
            if (hasPendingPlayerAttack &&
                pendingPlayerAttack.Direction == PlayerAttackDirection.Down)
            {
                BeginDownHitSeparation(pendingPlayerAttack);
            }
            else
            {
                downHitSeparationActive = false;
            }
            pursuitVelocity = GetHitKnockbackVelocity(sourcePosition);
            hasTargetThisFrame = false;
            playerSupportedThisFrame = false;
        }

        private void HandleDied(Health deadHealth)
        {
            StopAllEnemyLoops();
            AudioService.PlayAtAcoustic(DeathEventId, transform.position);
        }

        private void BeginDownHitSeparation(PlayerAttackContext context)
        {
            Collider2D playerCollider = context.Attacker != null
                ? context.Attacker.GetComponent<Collider2D>()
                : null;
            Collider2D supportCollider = standableSurface != null
                ? standableSurface.SurfaceCollider
                : ownerCollider;

            float playerHalfWidth = playerCollider != null
                ? playerCollider.bounds.extents.x
                : 0.4f;
            float playerCenterX = playerCollider != null
                ? playerCollider.bounds.center.x
                : context.Origin.x;
            Bounds separationBounds = ownerCollider != null
                ? ownerCollider.bounds
                : new Bounds(body.position, new Vector3(1f, 1f, 0f));
            if (supportCollider != null && supportCollider != ownerCollider)
            {
                separationBounds.Encapsulate(supportCollider.bounds.min);
                separationBounds.Encapsulate(supportCollider.bounds.max);
            }

            float supportHalfWidth = separationBounds.extents.x;
            float supportCenterX = separationBounds.center.x;
            float awayFromAttacker = supportCenterX - playerCenterX;
            downHitSeparationDirection = Mathf.Abs(awayFromAttacker) > 0.1f
                ? (awayFromAttacker < 0f ? -1 : 1)
                : -context.AttackerFacing;

            float requiredSeparation =
                playerHalfWidth + supportHalfWidth + downHitSeparationSafetyMargin;
            float targetSupportX =
                playerCenterX + downHitSeparationDirection * requiredSeparation;
            downHitSeparationTargetX =
                body.position.x + (targetSupportX - supportCenterX);
            downHitSeparationTimer = 0f;
            downHitSeparationActive = true;
        }

        private Vector2 GetHitKnockbackVelocity(Vector2 sourcePosition)
        {
            Vector2 away = body.position - sourcePosition;
            if (hasPendingPlayerAttack &&
                pendingPlayerAttack.Direction == PlayerAttackDirection.Down)
            {
                float horizontalDirection = downHitSeparationActive
                    ? downHitSeparationDirection
                    : Mathf.Abs(away.x) > 0.1f
                        ? Mathf.Sign(away.x)
                        : -pendingPlayerAttack.AttackerFacing;
                return new Vector2(
                    horizontalDirection * hitKnockbackHorizontal * downHitSeparationMultiplier,
                    -hitKnockbackVertical);
            }

            bool sourceAbove = sourcePosition.y > body.position.y + 0.1f;
            float horizontal = !sourceAbove && Mathf.Abs(away.x) > 0.1f
                ? Mathf.Sign(away.x) * hitKnockbackHorizontal
                : 0f;
            float vertical = sourceAbove
                ? -hitKnockbackVertical
                : hitKnockbackVertical;
            return new Vector2(horizontal, vertical);
        }

        private void RecoverFromStun()
        {
            bool hasSight = vision.TryGetVisibleTarget(
                out Vector2 targetPoint,
                out AerialSentrySightFailure _);
            bool playerSupported = IsPlayerSupportedByThisSentry();
            bool hasTarget = hasSight || playerSupported;
            hasTargetThisFrame = hasTarget;
            playerSupportedThisFrame = playerSupported;
            pursuitVelocity = Vector2.zero;
            downHitSeparationActive = false;

            if (hasTarget)
            {
                if (hasSight)
                    ReportPlayerVisible();
                visibleTarget = targetPoint;
                lastKnownPosition = targetPoint;
                hasLastKnownPosition = true;
                timeWithoutSight = 0f;
                FocusVisionOn(targetPoint, Time.deltaTime);
                TransitionTo(wasAlertBeforeStun
                    ? (HasEffectiveShot(true, targetPoint) && attackCooldownTimer <= 0f
                        ? AerialSentryState.Charging
                        : AerialSentryState.Pursuing)
                    : AerialSentryState.Acquiring);
                return;
            }

            if (wasAlertBeforeStun && hasLastKnownPosition)
                TransitionTo(AerialSentryState.Searching);
            else
                TransitionTo(AerialSentryState.Patrolling);
        }

        private void FireAt(Vector2 targetAtFireTime)
        {
            LogFireRequest(targetAtFireTime);

            if (muzzle == null)
            {
                LogFireBlocked("MissingMuzzle");
                CompleteAttackCycle();
                return;
            }

            if (projectilePrefab == null)
            {
                LogFireBlocked("MissingProjectilePrefab");
                CompleteAttackCycle();
                return;
            }

            Vector2 spawnPosition = muzzle.position;
            Vector2 direction = targetAtFireTime - spawnPosition;
            if (direction.sqrMagnitude < 0.0001f)
                direction = Vector2.down;
            direction.Normalize();

            AerialSentryProjectile projectile = Instantiate(
                projectilePrefab,
                spawnPosition,
                Quaternion.identity);
            projectile.Initialize(
                direction,
                projectileSpeed,
                damage,
                stunDuration,
                projectileLifetime,
                projectileWorldCollisionMask,
                ownerCollider,
                debugLogs);
            AudioService.PlayAtAcoustic(FireEventId, spawnPosition);
            LogProjectileSpawned(projectile, spawnPosition, direction);

            CompleteAttackCycle();
        }

        private void CompleteAttackCycle()
        {
            attackCooldownTimer = attackCooldown;
            TransitionTo(hasTargetThisFrame
                ? AerialSentryState.Pursuing
                : AerialSentryState.Searching);
        }

        private void TransitionTo(AerialSentryState nextState)
        {
            AerialSentryState previousState = CurrentState;
            if (previousState == AerialSentryState.Charging &&
                nextState != AerialSentryState.Charging)
            {
                StopLoop(ref chargeLoopHandle, 0.05f);
            }

            if (previousState != nextState &&
                (nextState == AerialSentryState.Acquiring ||
                 nextState == AerialSentryState.Searching) &&
                (previousState == AerialSentryState.Patrolling ||
                 previousState == AerialSentryState.Returning))
            {
                PrepareAlertReturnPath(previousState);
            }

            CurrentState = nextState;
            stateTimer = 0f;
            lostSightTimer = 0f;
            if (previousState != nextState && nextState != AerialSentryState.Returning)
                navigationBlockedTimer = 0f;

            if (nextState == AerialSentryState.Charging ||
                nextState == AerialSentryState.Patrolling)
            {
                pursuitVelocity = Vector2.zero;
            }

            LogStateTransition(previousState, nextState);

            if (previousState != nextState && nextState == AerialSentryState.Acquiring)
                AudioService.PlayAtAcoustic(DetectEventId, transform.position);

            if (previousState != nextState && nextState == AerialSentryState.Charging)
            {
                AudioService.PlayAtAcoustic(AlertEventId, transform.position);
                EnsureLoop(ref chargeLoopHandle, ChargeLoopEventId);
                LogChargeStart();
            }
            else if (nextState == AerialSentryState.Searching &&
                     previousState != AerialSentryState.Searching)
                BeginSearch();
            else if (nextState == AerialSentryState.Patrolling)
            {
                vision.SetViewDirection(Vector2.down);
                returnBreadcrumbIndex = -1;
                returnBreadcrumbs.Clear();
            }
        }

        private void PrepareAlertReturnPath(AerialSentryState previousState)
        {
            if (previousState == AerialSentryState.Returning &&
                returnBreadcrumbs.Count > 0 &&
                returnBreadcrumbIndex >= 0)
            {
                int keepCount = Mathf.Clamp(
                    returnBreadcrumbIndex + 1,
                    0,
                    returnBreadcrumbs.Count);
                if (keepCount < returnBreadcrumbs.Count)
                {
                    returnBreadcrumbs.RemoveRange(
                        keepCount,
                        returnBreadcrumbs.Count - keepCount);
                }
            }
            else
            {
                returnBreadcrumbs.Clear();
            }

            RecordReturnBreadcrumb(body.position, true);
            returnBreadcrumbIndex = -1;
            returnBlockedTimer = 0f;
            returnBlockedTotalTimer = 0f;
        }

        private bool TryHearPlayer(out Vector2 heardPosition)
        {
            heardPosition = default;
            ResolvePlayerRuntimeComponents();
            if (cachedPlayerController == null || cachedPlayerBody == null)
                return false;
            if (cachedPlayerController.IsSilentForEnemyHearing)
                return false;
            if (cachedPlayerBody.velocity.magnitude < hearingMovementSpeedThreshold)
                return false;

            heardPosition = cachedPlayerBody.worldCenterOfMass;
            Vector2 offset = heardPosition - vision.Origin;
            if (Mathf.Abs(offset.x) > hearingHorizontalRange ||
                Mathf.Abs(offset.y) > hearingVerticalRange)
            {
                return false;
            }

            return !vision.IsPathOccluded(heardPosition);
        }

        private void ReactToPlayerHearing(Vector2 heardPosition)
        {
            lastKnownPosition = heardPosition;
            hasLastKnownPosition = true;
            timeWithoutSight = 0f;

            float horizontalOffset = heardPosition.x - body.position.x;
            if (Mathf.Abs(horizontalOffset) > 0.05f)
                lastKnownHorizontalSide = horizontalOffset < 0f ? -1 : 1;

            switch (CurrentState)
            {
                case AerialSentryState.Patrolling:
                case AerialSentryState.Acquiring:
                case AerialSentryState.Returning:
                    AudioService.PlayAtAcoustic(DetectEventId, transform.position);
                    AudioService.PlayAtAcoustic(AlertEventId, transform.position);
                    TransitionTo(AerialSentryState.Searching);
                    break;
                case AerialSentryState.Searching:
                    // Update the last known position without restarting the search
                    // sweep every frame while the moving Player remains audible.
                    break;
            }
        }

        private void ResolvePlayerRuntimeComponents()
        {
            Transform playerTransform = vision != null ? vision.PlayerTransform : null;
            if (playerTransform == null)
                return;

            if (cachedPlayerController == null)
            {
                cachedPlayerController =
                    playerTransform.GetComponent<Game.Player.PlayerController>() ??
                    playerTransform.GetComponentInParent<Game.Player.PlayerController>() ??
                    playerTransform.GetComponentInChildren<Game.Player.PlayerController>(true);
            }

            if (cachedPlayerBody == null)
            {
                cachedPlayerBody =
                    playerTransform.GetComponent<Rigidbody2D>() ??
                    playerTransform.GetComponentInParent<Rigidbody2D>() ??
                    playerTransform.GetComponentInChildren<Rigidbody2D>(true);
            }

            if (cachedStealthDebugIndicator == null)
            {
                cachedStealthDebugIndicator =
                    playerTransform.GetComponent<Game.Player.StealthDebugIndicator>() ??
                    playerTransform.GetComponentInParent<Game.Player.StealthDebugIndicator>() ??
                    playerTransform.GetComponentInChildren<Game.Player.StealthDebugIndicator>(true);
            }
        }

        private void ReportPlayerVisible()
        {
            ResolvePlayerRuntimeComponents();
            cachedStealthDebugIndicator?.ReportVisualDetection();
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.OnDamaged -= HandleDamaged;
                health.OnDied -= HandleDied;
            }
            StopAllEnemyLoops();
            hasTargetThisFrame = false;
            playerSupportedThisFrame = false;
            if (standableSurface != null)
                standableSurface.SetMovementDelta(Vector2.zero);
        }

        private void EnsureLoop(ref AudioPlaybackHandle handle, string eventId)
        {
            if (handle.IsValid)
                return;

            AudioService service = AudioService.Instance;
            AudioEventCatalog catalog = service != null ? service.Catalog : null;
            if (catalog != null && catalog.TryGet(eventId, out AudioEventDefinition definition))
                handle = AudioService.StartAcousticLoop(definition, transform);
        }

        private static void StopLoop(ref AudioPlaybackHandle handle, float fadeOutSeconds)
        {
            AudioService.Stop(handle, fadeOutSeconds);
            handle = default;
        }

        private void StopAllEnemyLoops()
        {
            StopLoop(ref hoverLoopHandle, 0.08f);
            StopLoop(ref chargeLoopHandle, 0.05f);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogDetected()
        {
            if (debugLogs)
                Debug.Log($"[SENTRY][DETECTED] name={name} player={vision.PlayerTransform?.name}", this);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogLost(AerialSentrySightFailure reason)
        {
            if (debugLogs)
                Debug.Log($"[SENTRY][LOST] name={name} reason={reason}", this);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogStateTransition(AerialSentryState previousState, AerialSentryState nextState)
        {
            if (debugLogs && previousState != nextState)
                Debug.Log($"[SENTRY][STATE] {previousState} -> {nextState}", this);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogChargeStart()
        {
            if (debugLogs)
                Debug.Log($"[SENTRY][CHARGE START] duration={chargeDuration:0.###}", this);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogChargeCancel(AerialSentrySightFailure failure)
        {
            if (debugLogs)
                Debug.Log($"[SENTRY][CHARGE CANCEL] reason=LostSight detail={failure}", this);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogFireRequest(Vector2 targetAtFireTime)
        {
            if (!debugLogs)
                return;

            string muzzleName = muzzle != null ? muzzle.name : "None";
            string prefabName = projectilePrefab != null ? projectilePrefab.name : "None";
            Debug.Log(
                $"[SENTRY][FIRE REQUEST] target={targetAtFireTime} muzzle={muzzleName} " +
                $"projectilePrefab={prefabName}",
                this);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogFireBlocked(string reason)
        {
            if (debugLogs)
                Debug.LogWarning($"[SENTRY][FIRE BLOCKED] reason={reason}", this);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogProjectileSpawned(
            AerialSentryProjectile projectile,
            Vector2 spawnPosition,
            Vector2 direction)
        {
            if (debugLogs)
            {
                Debug.Log(
                    $"[SENTRY][PROJECTILE SPAWNED] instance={projectile.name} " +
                    $"position={spawnPosition} direction={direction}",
                    projectile);
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogSearchPhase(AerialSentrySearchPhase phase)
        {
            if (!debugLogs || phase == AerialSentrySearchPhase.AlignDown)
                return;

            Debug.Log(
                $"[SENTRY][SEARCH] phase={phase} angle=" +
                $"{Vector2.SignedAngle(Vector2.down, vision.ViewDirection):0.#}",
                this);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogReacquiredDuringSearch()
        {
            if (debugLogs)
                Debug.Log("[SENTRY][REACQUIRED DURING SEARCH]", this);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogHit(int remainingHealth)
        {
            if (debugLogs)
            {
                Debug.Log(
                    $"[SENTRY][HIT] hp={remainingHealth} stun={hitStunDuration:0.###}",
                    this);
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogPlayerAttack(
            int healthBefore,
            int healthAfter,
            PlayerAttackDirection direction,
            int attackId,
            bool applied)
        {
            if (debugLogs)
            {
                Debug.Log(
                    $"[AERIAL HIT] hpBefore={healthBefore} hpAfter={healthAfter} " +
                    $"direction={direction} attackId={attackId} applied={applied}",
                    this);
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogDownAttack(bool supportReleased)
        {
            if (!debugLogs)
                return;

            float knockbackDirection = pursuitVelocity.x == 0f
                ? pendingPlayerAttack.AttackerFacing
                : Mathf.Sign(pursuitVelocity.x);
            Debug.Log(
                $"[AERIAL DOWN HIT] supportReleased={supportReleased} " +
                $"knockbackDirection={knockbackDirection:0}",
                this);
        }

        private void OnValidate()
        {
            patrolDistance = Mathf.Max(0f, patrolDistance);
            patrolSpeed = Mathf.Max(0f, patrolSpeed);
            endWaitDuration = Mathf.Max(0f, endWaitDuration);
            chaseSpeed = Mathf.Max(0f, chaseSpeed);
            chaseAcceleration = Mathf.Max(0f, chaseAcceleration);
            chaseDeceleration = Mathf.Max(0f, chaseDeceleration);
            chaseTurnAcceleration = Mathf.Max(0f, chaseTurnAcceleration);
            hoverHeight = Mathf.Max(0f, hoverHeight);
            horizontalDeadZone = Mathf.Max(0f, horizontalDeadZone);
            verticalDeadZone = Mathf.Max(0f, verticalDeadZone);
            maxHorizontalChaseSpeed = Mathf.Max(0f, maxHorizontalChaseSpeed);
            maxVerticalChaseSpeed = Mathf.Max(0f, maxVerticalChaseSpeed);
            minimumGroundClearance = Mathf.Max(0f, minimumGroundClearance);
            lostTargetTime = Mathf.Max(0.1f, lostTargetTime);
            searchSweepStartDelay = Mathf.Max(0f, searchSweepStartDelay);
            searchArrivalRadius = Mathf.Max(0.01f, searchArrivalRadius);
            searchSweepSpeed = Mathf.Max(1f, searchSweepSpeed);
            returnSpeed = Mathf.Max(0f, returnSpeed);
            navigationSkin = Mathf.Max(0f, navigationSkin);
            blockedPursuitTimeout = Mathf.Max(0.1f, blockedPursuitTimeout);
            blockedSearchTimeout = Mathf.Max(0.1f, blockedSearchTimeout);
            returnBlockedRetryDuration = Mathf.Max(0.1f, returnBlockedRetryDuration);
            returnBlockedTimeout = Mathf.Max(
                returnBlockedRetryDuration,
                returnBlockedTimeout);
            breadcrumbSpacing = Mathf.Max(0.05f, breadcrumbSpacing);
            blockedReacquireDelay = Mathf.Max(0f, blockedReacquireDelay);
            hearingHorizontalRange = Mathf.Max(0f, hearingHorizontalRange);
            hearingVerticalRange = Mathf.Max(0f, hearingVerticalRange);
            hearingMovementSpeedThreshold = Mathf.Max(0f, hearingMovementSpeedThreshold);
            effectiveFireRange = Mathf.Max(0.1f, effectiveFireRange);
            projectilePathRadius = Mathf.Max(0.01f, projectilePathRadius);
            chargeDuration = Mathf.Max(0f, chargeDuration);
            projectileSpeed = Mathf.Max(0.1f, projectileSpeed);
            attackCooldown = Mathf.Max(0f, attackCooldown);
            damage = 1;
            stunDuration = Mathf.Max(0f, stunDuration);
            projectileLifetime = Mathf.Max(0.1f, projectileLifetime);
            hitStunDuration = Mathf.Max(0f, hitStunDuration);
            hitKnockbackHorizontal = Mathf.Max(0f, hitKnockbackHorizontal);
            hitKnockbackVertical = Mathf.Max(0f, hitKnockbackVertical);
            downHitSeparationMultiplier = Mathf.Max(1f, downHitSeparationMultiplier);
            downHitSeparationSafetyMargin = Mathf.Max(0f, downHitSeparationSafetyMargin);
            downHitSeparationTimeout = Mathf.Max(0.05f, downHitSeparationTimeout);
        }

        private void OnDrawGizmosSelected()
        {
            Vector2 center = Application.isPlaying && body != null
                ? patrolCenter
                : (Vector2)transform.position;
            Vector2 patrolDirection = axis == AerialSentryPatrolAxis.Horizontal
                ? Vector2.right
                : Vector2.up;
            Vector2 offset = patrolDirection * (Mathf.Max(0f, patrolDistance) * 0.5f);

            Gizmos.color = new Color(1f, 0.65f, 0.1f, 0.9f);
            Gizmos.DrawLine(center - offset, center + offset);
            Gizmos.DrawWireSphere(center - offset, 0.12f);
            Gizmos.DrawWireSphere(center + offset, 0.12f);

            if (Application.isPlaying && hasLastKnownPosition)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(lastKnownPosition, 0.08f);
                Gizmos.DrawLine(lastKnownPosition, SearchHoverPosition);
                Gizmos.DrawWireSphere(SearchHoverPosition, searchArrivalRadius);
                Gizmos.DrawLine(transform.position, SearchHoverPosition);
            }

            if (Application.isPlaying && hasTargetThisFrame)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(visibleTarget, CombatHoverPosition);
                Gizmos.DrawWireSphere(CombatHoverPosition, 0.12f);
            }

            Vector2 clearanceOrigin = Application.isPlaying
                ? GetGroundClearanceProbeOrigin()
                : (Vector2)transform.position + Vector2.down * 0.55f;
            Gizmos.color = Color.green;
            Gizmos.DrawLine(
                clearanceOrigin,
                clearanceOrigin + Vector2.down * Mathf.Max(0f, minimumGroundClearance));

            if (Application.isPlaying && vision != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(
                    vision.Origin,
                    vision.Origin + vision.ViewDirection * 1.25f);
            }
        }
    }
}

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
        [SerializeField, Min(0f)] private float chaseSpeed = 4f;
        [SerializeField, Min(0f)] private float chaseAcceleration = 12f;
        [SerializeField, Min(0f)] private float hoverHeight = 2.5f;
        [SerializeField, Min(0f)] private float horizontalDeadZone = 0.4f;
        [SerializeField, Min(0f)] private float verticalDeadZone = 0.35f;
        [SerializeField, Min(0f)] private float maxHorizontalChaseSpeed = 4.5f;
        [SerializeField, Min(0f)] private float maxVerticalChaseSpeed = 3.5f;
        [SerializeField, Min(0f)] private float minimumGroundClearance = 1.25f;
        [SerializeField, Min(0.1f)] private float lostTargetTime = 4f;
        [SerializeField, Min(0f)] private float searchSweepStartDelay = 0.35f;
        [SerializeField, Min(0.01f)] private float searchArrivalRadius = 0.4f;
        [SerializeField, Min(1f)] private float searchSweepSpeed = 150f;
        [SerializeField, Min(0f)] private float returnSpeed = 3f;

        [Header("Hearing")]
        [SerializeField, Min(0f)] private float hearingHorizontalRange = 3f;
        [SerializeField, Min(0f)] private float hearingVerticalRange = 3.5f;
        [SerializeField, Min(0f)] private float hearingMovementSpeedThreshold = 0.5f;

        [Header("Attack")]
        [SerializeField] private Transform muzzle;
        [SerializeField] private AerialSentryProjectile projectilePrefab;
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
        private Vector2 visibleTarget;
        private Vector2 lastKnownPosition;
        private Vector2 returnTarget;
        private Vector2 pursuitVelocity;
        private bool hasSightThisFrame;
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
        private readonly RaycastHit2D[] groundClearanceHits = new RaycastHit2D[8];
        private readonly RaycastHit2D[] separationHits = new RaycastHit2D[8];
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

            searchSweepCompleteThisFrame = false;
            if (CurrentState == AerialSentryState.Stunned)
            {
                hasSightThisFrame = false;
                stateTimer += Time.deltaTime;
                if (stateTimer >= hitStunDuration && !downHitSeparationActive)
                    RecoverFromStun();
                return;
            }

            UpdateVisionDirection(Time.deltaTime);
            bool hasSight = vision.TryGetVisibleTarget(out Vector2 targetPoint, out AerialSentrySightFailure failure);
            hasSightThisFrame = hasSight;

            if (hasSight)
            {
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

            if (!hasSight && TryHearPlayer(out Vector2 heardPosition))
                ReactToPlayerHearing(heardPosition);

            switch (CurrentState)
            {
                case AerialSentryState.Patrolling:
                    if (hasSight)
                    {
                        TransitionTo(AerialSentryState.Acquiring);
                        FocusVisionOn(targetPoint, Time.deltaTime);
                        LogDetected();
                    }
                    break;

                case AerialSentryState.Acquiring:
                    if (!hasSight)
                    {
                        LogLost(failure);
                        hasLastKnownPosition = false;
                        timeWithoutSight = 0f;
                        TransitionTo(AerialSentryState.Patrolling);
                        break;
                    }

                    stateTimer += Time.deltaTime;
                    if (stateTimer >= vision.DetectionAcquireTime)
                        TransitionTo(AerialSentryState.Charging);
                    break;

                case AerialSentryState.Pursuing:
                    if (!hasSight)
                    {
                        TransitionTo(AerialSentryState.Searching);
                        break;
                    }

                    if (attackCooldownTimer <= 0f)
                        TransitionTo(AerialSentryState.Charging);
                    break;

                case AerialSentryState.Charging:
                    stateTimer += Time.deltaTime;
                    if (hasSight)
                    {
                        lostSightTimer = 0f;
                    }
                    else
                    {
                        lostSightTimer += Time.deltaTime;
                        if (lostSightTimer > vision.LostSightGrace)
                        {
                            LogChargeCancel(failure);
                            TransitionTo(AerialSentryState.Searching);
                            break;
                        }
                    }

                    if (stateTimer >= chargeDuration && hasSight)
                        FireAt(visibleTarget);
                    break;

                case AerialSentryState.Cooldown:
                    TransitionTo(hasSight
                        ? AerialSentryState.Pursuing
                        : AerialSentryState.Searching);
                    break;

                case AerialSentryState.Searching:
                    if (hasSight)
                    {
                        LogReacquiredDuringSearch();
                        FocusVisionOn(targetPoint, Time.deltaTime);
                        TransitionTo(attackCooldownTimer <= 0f
                            ? AerialSentryState.Charging
                            : AerialSentryState.Pursuing);
                        break;
                    }

                    if (searchSweepCompleteThisFrame)
                        BeginReturnToPatrol();
                    break;

                case AerialSentryState.Returning:
                    if (hasSight)
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

            MoveBody(nextPosition);
            if ((nextPosition - destination).sqrMagnitude > 0.000001f)
                return;

            movingToPositiveEnd = !movingToPositiveEnd;
            AudioService.PlayAtAcoustic(PatrolTurnEventId, transform.position);
            if (waitAtEnds)
                endWaitTimer = endWaitDuration;
        }

        private void UpdatePursuit(Vector2 target)
        {
            Vector2 desiredVelocity = GetHoverDesiredVelocity(GetHoverPosition(target));
            MoveWithPursuitAcceleration(desiredVelocity);
        }

        private void UpdateAlertMovement()
        {
            if (hasSightThisFrame)
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
                MoveWithPursuitAcceleration(Vector2.zero);
                return;
            }

            Vector2 searchHoverPosition = SearchHoverPosition;
            Vector2 desiredVelocity = Vector2.Distance(body.position, searchHoverPosition) > searchArrivalRadius
                ? GetHoverDesiredVelocity(searchHoverPosition)
                : Vector2.zero;
            MoveWithPursuitAcceleration(desiredVelocity);
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

        private void BeginReturnToPatrol()
        {
            hasLastKnownPosition = false;
            timeWithoutSight = 0f;
            vision.SetViewDirection(Vector2.down);
            returnTarget = GetNearestPointOnPatrolRoute(body.position);
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
            Vector2 toRoute = returnTarget - body.position;
            if (toRoute.magnitude <= searchArrivalRadius)
            {
                pursuitVelocity = Vector2.zero;
                TransitionTo(AerialSentryState.Patrolling);
                return;
            }

            MoveWithPursuitAcceleration(toRoute.normalized * returnSpeed);
        }

        private void MoveWithPursuitAcceleration(Vector2 desiredVelocity)
        {
            desiredVelocity = ApplyMinimumGroundClearance(desiredVelocity);
            float accelerationStep = chaseAcceleration * Time.fixedDeltaTime;
            pursuitVelocity.x = Mathf.MoveTowards(
                pursuitVelocity.x,
                desiredVelocity.x,
                accelerationStep);
            pursuitVelocity.y = Mathf.MoveTowards(
                pursuitVelocity.y,
                desiredVelocity.y,
                accelerationStep);
            MoveBody(body.position + pursuitVelocity * Time.fixedDeltaTime);
        }

        private void UpdateStunMovement()
        {
            Vector2 movementVelocity = ApplyMinimumGroundClearance(pursuitVelocity);
            bool maintainHorizontalSeparation = TryApplyDownHitSeparation(ref movementVelocity);
            MoveBody(body.position + movementVelocity * Time.fixedDeltaTime);

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

            int worldMask = vision.OcclusionMask.value;
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
            int worldMask = vision.OcclusionMask.value;
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

        private void MoveBody(Vector2 nextPosition)
        {
            if (standableSurface != null)
                standableSurface.SetMovementDelta(nextPosition - body.position);

            body.MovePosition(nextPosition);
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
            hasSightThisFrame = false;
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
            hasSightThisFrame = hasSight;
            pursuitVelocity = Vector2.zero;
            downHitSeparationActive = false;

            if (hasSight)
            {
                ReportPlayerVisible();
                visibleTarget = targetPoint;
                lastKnownPosition = targetPoint;
                hasLastKnownPosition = true;
                timeWithoutSight = 0f;
                FocusVisionOn(targetPoint, Time.deltaTime);
                TransitionTo(wasAlertBeforeStun
                    ? (attackCooldownTimer <= 0f
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
            TransitionTo(hasSightThisFrame
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

            CurrentState = nextState;
            stateTimer = 0f;
            lostSightTimer = 0f;

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
                vision.SetViewDirection(Vector2.down);
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
                    BeginSearch();
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
            hearingHorizontalRange = Mathf.Max(0f, hearingHorizontalRange);
            hearingVerticalRange = Mathf.Max(0f, hearingVerticalRange);
            hearingMovementSpeedThreshold = Mathf.Max(0f, hearingMovementSpeedThreshold);
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

            if (Application.isPlaying && hasSightThisFrame)
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

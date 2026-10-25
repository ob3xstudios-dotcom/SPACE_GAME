using System.Collections.Generic;
using Game.Combat;
using Game.Player;
using UnityEngine;

namespace Game.Enemies.CaveBat
{
    public enum CaveBatState
    {
        Patrol,
        Alert,
        Planning,
        Approach,
        UManeuver,
        Dive,
        Recover,
        Return
    }

    internal enum CaveBatMoveResult
    {
        Moving,
        Reached,
        Blocked
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CircleCollider2D))]
    [RequireComponent(typeof(Health))]
    public sealed class CaveBatController : MonoBehaviour, IPlayerDownAttackContactSuppressor
    {
        private const int QueryCapacity = 24;
        private const int MinimumCurveSamples = 10;
        private const int MaximumCurveSamples = 96;
        private const float ArrivalTolerance = 0.015f;

        [Header("References")]
        [SerializeField] private EnemyPerception2D perception;
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private CircleCollider2D bodyCollider;
        [SerializeField] private CaveBatPrototypeVisual prototypeVisual;

        [Header("Patrol Points (Local To Spawn)")]
        [SerializeField] private Vector2 patrolPointA = new Vector2(-2f, 0f);
        [SerializeField] private Vector2 patrolPointB = new Vector2(2f, 0f);
        [SerializeField, Min(0f)] private float patrolSpeed = 2f;
        [SerializeField, Min(0f)] private float patrolWaitDuration = 0.25f;

        [Header("Perception Reaction")]
        [SerializeField, Min(0f)] private float reactionDelay = 0.35f;

        [Header("Ambush Path")]
        [SerializeField, Min(0.1f)] private float approachSpeed = 3.5f;
        [SerializeField, Min(0.2f)] private float uWidth = 3.2f;
        [SerializeField, Min(0.1f)] private float uDepth = 1.6f;
        [SerializeField, Min(0.1f)] private float uDuration = 1.05f;
        [SerializeField, Min(0.2f)] private float diveStartHeight = 3f;
        [SerializeField, Min(0.1f)] private float diveSpeed = 8.5f;

        [Header("Combat")]
        [SerializeField, Min(1)] private int attackDamage = 1;
        [SerializeField] private LayerMask playerMask = 1 << 9;
        [SerializeField, Min(0.05f)] private float contactDamageRetryInterval = 0.15f;
        [SerializeField, Min(0f)] private float playerHitContactGraceDuration = 0.1f;

        [Header("Recovery")]
        [SerializeField, Min(0.1f)] private float recoverySpeed = 5f;
        [SerializeField, Min(0.1f)] private float returnSpeed = 3.5f;
        [SerializeField, Min(0.1f)] private float blockedRetryDuration = 0.75f;

        [Header("Navigation")]
        [SerializeField] private LayerMask worldMask = 10312;
        [SerializeField, Min(0f)] private float navigationSkin = 0.06f;
        [SerializeField, Min(0.05f)] private float pathValidationStep = 0.18f;

        [Header("Debug")]
        [SerializeField] private bool debugGizmos = true;

        private readonly RaycastHit2D[] castHits = new RaycastHit2D[QueryCapacity];
        private readonly Collider2D[] overlapHits = new Collider2D[QueryCapacity];
        private readonly List<Vector2> plannedUSamples = new List<Vector2>(48);
        private readonly List<Vector2> travelledPath = new List<Vector2>(192);
        private readonly HashSet<PlayerHealth> pendingContactDamage = new HashSet<PlayerHealth>();
        private readonly HashSet<Collider2D> activePlayerContacts = new HashSet<Collider2D>();
        private readonly Dictionary<PlayerHealth, float> nextContactDamageTime =
            new Dictionary<PlayerHealth, float>();

        private Health health;
        private Vector2 patrolOrigin;
        private Quaternion patrolRotation = Quaternion.identity;
        private bool patrolFrameCached;
        private int patrolTargetIndex = 1;
        private float patrolWaitTimer;
        private float stateTimer;
        private float blockedTimer;
        private Vector2 initialNoisePosition;
        private Vector2 latestNoisePosition;
        private Vector2 lockedDiveNoisePosition;
        private Vector2 initialAttackTargetPosition;
        private Vector2 latestAttackTargetPosition;
        private Vector2 lockedDiveTargetPosition;
        private Vector2 initialTargetExtents;
        private Vector2 latestTargetExtents;
        private bool hasRecordedNoise;
        private bool offensiveHitResolved;
        private Vector2 uEntry;
        private Vector2 uControlA;
        private Vector2 uBottom;
        private Vector2 uControlB;
        private Vector2 uExit;
        private Vector2 plannedDiveTarget;
        private float plannedDiveDistance;
        private float uProgress;
        private bool uFinishedPending;
        private bool diveFinishedPending;
        private int diveStartBreadcrumbIndex;
        private int returnBreadcrumbIndex;
        private Vector2 movementDirection = Vector2.right;
        private float suppressContactDamageUntil;
        private bool suppressContactDamageUntilSeparation;
        private int lastSuppressedDownAttackId;

        public CaveBatState CurrentState { get; private set; } = CaveBatState.Patrol;
        public bool HasRecordedNoise => hasRecordedNoise;
        public Vector2 RecordedNoisePosition => latestNoisePosition;
        public Vector2 InitialNoisePosition => initialNoisePosition;
        public Vector2 LatestNoisePosition => latestNoisePosition;
        public Vector2 LockedDiveNoisePosition => lockedDiveNoisePosition;
        public Vector2 MovementDirection => movementDirection;
        public float UManeuverProgress => uProgress;
        public bool IsOffensivePhase =>
            CurrentState == CaveBatState.UManeuver ||
            CurrentState == CaveBatState.Dive;
        public bool IsAttackActive =>
            isActiveAndEnabled &&
            (health == null || health.CurrentHP > 0);

        private Vector2 PatrolPointAWorld => TransformPatrolPoint(patrolPointA);
        private Vector2 PatrolPointBWorld => TransformPatrolPoint(patrolPointB);

        private void Awake()
        {
            ResolveReferences();
            ConfigureBody();
            CachePatrolFrame();
            ResetRuntimeState();
        }

        private void OnEnable()
        {
            ResolveReferences();
            ConfigureBody();
            CachePatrolFrame();
            Subscribe();
            ResetRuntimeState();
        }

        private void FixedUpdate()
        {
            if (health != null && health.CurrentHP <= 0)
                return;

            ConsumePerception();

            switch (CurrentState)
            {
                case CaveBatState.Patrol:
                    UpdatePatrol();
                    break;
                case CaveBatState.Alert:
                    UpdateAlert();
                    break;
                case CaveBatState.Planning:
                    UpdatePlanning();
                    break;
                case CaveBatState.Approach:
                    UpdateApproach();
                    break;
                case CaveBatState.UManeuver:
                    UpdateUManeuver();
                    break;
                case CaveBatState.Dive:
                    UpdateDive();
                    break;
                case CaveBatState.Recover:
                    UpdateRecover();
                    break;
                case CaveBatState.Return:
                    UpdateReturn();
                    break;
            }

            if (perception != null && movementDirection.sqrMagnitude > 0.0001f)
                perception.SetFacingDirection(movementDirection);
        }

        private void LateUpdate()
        {
            ResolvePendingContactDamage();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            QueueContactDamage(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            QueueContactDamage(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (other != null)
                activePlayerContacts.Remove(other);
            if (activePlayerContacts.Count == 0)
                suppressContactDamageUntilSeparation = false;
        }

        private void QueueContactDamage(Collider2D other)
        {
            if (!isActiveAndEnabled ||
                other == null ||
                !IsLayerAllowed(other.gameObject.layer, playerMask) ||
                (health != null && health.CurrentHP <= 0))
            {
                return;
            }

            PlayerHealth playerHealth = ResolvePlayerHealth(other);
            if (playerHealth != null && playerHealth.isActiveAndEnabled)
            {
                activePlayerContacts.Add(other);
                pendingContactDamage.Add(playerHealth);
            }
        }

        private void ResolvePendingContactDamage()
        {
            activePlayerContacts.RemoveWhere(
                contact => contact == null ||
                           !contact.enabled ||
                           !contact.gameObject.activeInHierarchy ||
                           (bodyCollider != null && !bodyCollider.IsTouching(contact)));
            if (activePlayerContacts.Count == 0)
                suppressContactDamageUntilSeparation = false;

            if (pendingContactDamage.Count == 0)
                return;

            if (!isActiveAndEnabled ||
                (health != null && health.CurrentHP <= 0) ||
                suppressContactDamageUntilSeparation ||
                Time.time < suppressContactDamageUntil)
            {
                pendingContactDamage.Clear();
                return;
            }

            float now = Time.time;
            foreach (PlayerHealth playerHealth in pendingContactDamage)
            {
                if (playerHealth == null ||
                    !playerHealth.isActiveAndEnabled ||
                    playerHealth.IsInvulnerable)
                {
                    continue;
                }

                if (CurrentState != CaveBatState.Patrol && offensiveHitResolved)
                    continue;
                if (nextContactDamageTime.TryGetValue(playerHealth, out float nextAllowedTime) &&
                    now < nextAllowedTime)
                {
                    continue;
                }

                Vector2 sourcePosition = body != null
                    ? body.position
                    : (Vector2)transform.position;
                bool damageApplied = playerHealth.TryTakeDamage(
                    attackDamage,
                    sourcePosition,
                    0f);
                if (!damageApplied)
                    continue;

                nextContactDamageTime[playerHealth] = now + contactDamageRetryInterval;
                if (CurrentState != CaveBatState.Patrol)
                    offensiveHitResolved = true;
            }

            pendingContactDamage.Clear();
        }

        private static PlayerHealth ResolvePlayerHealth(Collider2D other)
        {
            if (other == null)
                return null;

            return other.GetComponent<PlayerHealth>() ??
                   other.GetComponentInParent<PlayerHealth>();
        }

        private void ConsumePerception()
        {
            if (perception == null || !perception.isActiveAndEnabled)
                return;

            bool canStartManeuver = CurrentState == CaveBatState.Patrol;
            bool canUpdateDiveTarget =
                CurrentState == CaveBatState.Approach ||
                CurrentState == CaveBatState.UManeuver;
            if (!canStartManeuver && !canUpdateDiveTarget)
                return;

            bool heard = perception.HasActiveNoiseStimulus;
            bool seen = perception.IsPlayerVisible;
            if (!heard && !seen)
                return;

            Vector2 heardPosition = heard
                ? perception.LastHeardPosition
                : perception.LastSeenPosition;
            ResolveAttackTargetSnapshot(
                perception.Target != null ? perception.Target.gameObject : null,
                heardPosition,
                out Vector2 attackTargetPosition,
                out Vector2 targetExtents);

            if (canUpdateDiveTarget)
            {
                latestNoisePosition = heardPosition;
                latestAttackTargetPosition = attackTargetPosition;
                latestTargetExtents = targetExtents;
                return;
            }

            initialNoisePosition = heardPosition;
            latestNoisePosition = heardPosition;
            lockedDiveNoisePosition = default;
            initialAttackTargetPosition = attackTargetPosition;
            latestAttackTargetPosition = attackTargetPosition;
            lockedDiveTargetPosition = default;
            initialTargetExtents = targetExtents;
            latestTargetExtents = targetExtents;
            hasRecordedNoise = true;
            offensiveHitResolved = false;
            stateTimer = 0f;
            blockedTimer = 0f;
            movementDirection = Vector2.zero;
            CurrentState = CaveBatState.Alert;
        }

        private void ResolveAttackTargetSnapshot(
            GameObject emitter,
            Vector2 noisePosition,
            out Vector2 attackTargetPosition,
            out Vector2 targetExtents)
        {
            attackTargetPosition = noisePosition;
            targetExtents = Vector2.zero;
            if (emitter == null)
                return;

            Collider2D targetCollider = emitter.GetComponent<Collider2D>();
            if (targetCollider == null)
                targetCollider = emitter.GetComponentInChildren<Collider2D>();
            if (targetCollider == null || !targetCollider.enabled || targetCollider.isTrigger)
                return;

            Bounds targetBounds = targetCollider.bounds;
            Vector2 closestPoint = targetCollider.ClosestPoint(noisePosition);
            float alignmentTolerance = Mathf.Max(0.05f, navigationSkin);
            if ((closestPoint - noisePosition).sqrMagnitude >
                alignmentTolerance * alignmentTolerance)
            {
                return;
            }

            attackTargetPosition = targetBounds.center;
            targetExtents = targetBounds.extents;
        }

        private void UpdatePatrol()
        {
            if (patrolSpeed <= 0f)
            {
                movementDirection = Vector2.zero;
                return;
            }

            if (patrolWaitTimer > 0f)
            {
                patrolWaitTimer = Mathf.Max(0f, patrolWaitTimer - Time.fixedDeltaTime);
                movementDirection = Vector2.zero;
                return;
            }

            Vector2 target = patrolTargetIndex == 0 ? PatrolPointAWorld : PatrolPointBWorld;
            CaveBatMoveResult result = MoveTowardsSafely(target, patrolSpeed, false);
            if (result == CaveBatMoveResult.Reached)
            {
                patrolTargetIndex = patrolTargetIndex == 0 ? 1 : 0;
                patrolWaitTimer = patrolWaitDuration;
                blockedTimer = 0f;
                return;
            }

            if (result != CaveBatMoveResult.Blocked)
            {
                blockedTimer = 0f;
                return;
            }

            movementDirection = Vector2.zero;
            blockedTimer += Time.fixedDeltaTime;
            if (blockedTimer < blockedRetryDuration)
                return;

            blockedTimer = 0f;
            patrolTargetIndex = patrolTargetIndex == 0 ? 1 : 0;
            patrolWaitTimer = patrolWaitDuration;
        }

        private void UpdateAlert()
        {
            movementDirection = Vector2.zero;
            stateTimer += Time.fixedDeltaTime;
            if (stateTimer < reactionDelay)
                return;

            CurrentState = CaveBatState.Planning;
            stateTimer = 0f;
        }

        private void UpdatePlanning()
        {
            movementDirection = Vector2.zero;
            if (!hasRecordedNoise || !TryBuildAttackPlan())
            {
                ResumePatrol();
                return;
            }

            travelledPath.Clear();
            RecordBreadcrumb(body.position);
            CurrentState = CaveBatState.Approach;
            stateTimer = 0f;
            blockedTimer = 0f;
        }

        private void UpdateApproach()
        {
            CaveBatMoveResult result = MoveTowardsSafely(uEntry, approachSpeed, true);
            if (result == CaveBatMoveResult.Blocked)
            {
                BeginRecovery(false);
                return;
            }

            if (result != CaveBatMoveResult.Reached)
                return;

            CurrentState = CaveBatState.UManeuver;
            stateTimer = 0f;
            uProgress = 0f;
            uFinishedPending = false;
        }

        private void UpdateUManeuver()
        {
            if (uFinishedPending)
            {
                BeginDive();
                return;
            }

            stateTimer += Time.fixedDeltaTime;
            float requestedProgress = uDuration <= 0f
                ? 1f
                : Mathf.Clamp01(stateTimer / uDuration);
            if (uProgress < 0.5f && requestedProgress > 0.5f)
                requestedProgress = 0.5f;

            uProgress = requestedProgress;
            Vector2 nextPoint = EvaluateUManeuver(uProgress);

            CaveBatMoveResult result = MoveTowardsSafely(nextPoint, float.PositiveInfinity, true);
            if (result == CaveBatMoveResult.Blocked)
            {
                BeginRecovery(false);
                return;
            }

            if (uProgress >= 1f && result == CaveBatMoveResult.Reached)
                uFinishedPending = true;
        }

        private void BeginDive()
        {
            if (!TryCalculateDiveTarget(
                    body.position,
                    latestAttackTargetPosition,
                    latestTargetExtents,
                    out plannedDiveTarget,
                    out plannedDiveDistance))
            {
                BeginRecovery(false);
                return;
            }

            lockedDiveNoisePosition = latestNoisePosition;
            lockedDiveTargetPosition = latestAttackTargetPosition;
            CurrentState = CaveBatState.Dive;
            stateTimer = 0f;
            diveFinishedPending = false;
            diveStartBreadcrumbIndex = Mathf.Max(0, travelledPath.Count - 1);
        }

        private void UpdateDive()
        {
            if (diveFinishedPending)
            {
                BeginRecovery(false);
                return;
            }

            stateTimer += Time.fixedDeltaTime;
            float maximumDiveTime = plannedDiveDistance / Mathf.Max(0.1f, diveSpeed) + 0.5f;
            if (stateTimer >= maximumDiveTime)
            {
                BeginRecovery(false);
                return;
            }

            CaveBatMoveResult result = MoveTowardsSafely(plannedDiveTarget, diveSpeed, true);
            if (result == CaveBatMoveResult.Blocked)
            {
                BeginRecovery(false);
                return;
            }

            if (result == CaveBatMoveResult.Reached)
                diveFinishedPending = true;
        }

        private void BeginRecovery(bool playerWasHit)
        {
            CaveBatState interruptedState = CurrentState;
            CurrentState = CaveBatState.Recover;
            stateTimer = playerWasHit ? 0.05f : 0f;
            blockedTimer = 0f;
            returnBreadcrumbIndex = travelledPath.Count - 2;
            if (interruptedState != CaveBatState.Dive)
                diveStartBreadcrumbIndex = Mathf.Max(0, travelledPath.Count - 1);
            if (diveStartBreadcrumbIndex < 0 || diveStartBreadcrumbIndex >= travelledPath.Count)
                diveStartBreadcrumbIndex = Mathf.Max(0, travelledPath.Count - 1);
        }

        private void UpdateRecover()
        {
            if (stateTimer > 0f)
            {
                stateTimer = Mathf.Max(0f, stateTimer - Time.fixedDeltaTime);
                movementDirection = Vector2.zero;
                return;
            }

            if (returnBreadcrumbIndex < diveStartBreadcrumbIndex)
            {
                CurrentState = CaveBatState.Return;
                blockedTimer = 0f;
                return;
            }

            UpdateReversePath(recoverySpeed, diveStartBreadcrumbIndex);
        }

        private void UpdateReturn()
        {
            if (returnBreadcrumbIndex < 0)
            {
                ResumePatrol();
                return;
            }

            UpdateReversePath(returnSpeed, 0);
        }

        private void UpdateReversePath(float speed, int minimumIndex)
        {
            if (returnBreadcrumbIndex < minimumIndex || returnBreadcrumbIndex < 0)
                return;

            Vector2 target = travelledPath[returnBreadcrumbIndex];
            CaveBatMoveResult result = MoveTowardsSafely(target, speed, false);
            if (result == CaveBatMoveResult.Reached)
            {
                returnBreadcrumbIndex--;
                blockedTimer = 0f;
                return;
            }

            if (result == CaveBatMoveResult.Moving)
            {
                blockedTimer = 0f;
                return;
            }

            movementDirection = Vector2.zero;
            blockedTimer += Time.fixedDeltaTime;
            if (blockedTimer < blockedRetryDuration)
                return;

            blockedTimer = 0f;
            TrySelectEarlierReachableBreadcrumb(minimumIndex);
        }

        private bool TrySelectEarlierReachableBreadcrumb(int minimumIndex)
        {
            Vector2 origin = body.position;
            for (int index = returnBreadcrumbIndex - 1; index >= minimumIndex; index--)
            {
                if (!IsSegmentClear(origin, travelledPath[index]))
                    continue;

                returnBreadcrumbIndex = index;
                return true;
            }

            return false;
        }

        private bool TryBuildAttackPlan()
        {
            plannedUSamples.Clear();
            Vector2 start = body.position;
            if (!TryResolveOffensiveBottom(
                    initialAttackTargetPosition,
                    initialTargetExtents,
                    out uBottom))
                return false;

            BuildManeuverGeometry(
                start,
                uBottom,
                out uEntry,
                out uControlA,
                out uControlB,
                out uExit);

            if (!IsPositionClear(uEntry) || !IsPositionClear(uExit))
                return false;
            if (!IsSegmentClear(start, uEntry))
                return false;

            float approximateLength =
                Vector2.Distance(uEntry, uControlA) +
                Vector2.Distance(uControlA, uBottom) +
                Vector2.Distance(uBottom, uControlB) +
                Vector2.Distance(uControlB, uExit);
            int sampleCount = Mathf.Clamp(
                Mathf.CeilToInt(approximateLength / Mathf.Max(0.05f, pathValidationStep)),
                MinimumCurveSamples,
                MaximumCurveSamples);
            if (sampleCount % 2 != 0)
                sampleCount = Mathf.Min(MaximumCurveSamples, sampleCount + 1);

            Vector2 previous = uEntry;
            for (int index = 1; index <= sampleCount; index++)
            {
                float progress = index / (float)sampleCount;
                Vector2 sample = EvaluateUManeuver(progress);
                if (!IsPositionClear(sample) || !IsSegmentClear(previous, sample))
                    return false;

                plannedUSamples.Add(sample);
                previous = sample;
            }

            return TryCalculateDiveTarget(
                uExit,
                initialAttackTargetPosition,
                initialTargetExtents,
                out plannedDiveTarget,
                out plannedDiveDistance);
        }

        private void BuildManeuverGeometry(
            Vector2 start,
            Vector2 bottom,
            out Vector2 entry,
            out Vector2 controlA,
            out Vector2 controlB,
            out Vector2 exit)
        {
            float horizontalOffset = bottom.x - start.x;
            float side = Mathf.Abs(horizontalOffset) > 0.05f
                ? Mathf.Sign(horizontalOffset)
                : ResolvePatrolHorizontalDirection();
            Vector2 approachDirection = Vector2.right * side;

            entry = bottom - approachDirection * (uWidth * 0.5f) + Vector2.up * uDepth;
            exit = bottom + approachDirection * (uWidth * 0.5f) + Vector2.up * diveStartHeight;
            controlA = bottom - approachDirection * (uWidth * 0.25f);
            controlB = bottom + approachDirection * (uWidth * 0.25f);
        }

        private bool TryResolveOffensiveBottom(
            Vector2 attackTargetPosition,
            Vector2 targetExtents,
            out Vector2 safeBottom)
        {
            safeBottom = ApplyGroundClearance(
                attackTargetPosition,
                targetExtents);
            float step = Mathf.Max(0.05f, pathValidationStep);
            float maximumLift =
                Mathf.Max(uDepth, diveStartHeight) +
                GetBodyRadius() +
                navigationSkin +
                step;
            int sampleCount = Mathf.Max(1, Mathf.CeilToInt(maximumLift / step));
            for (int index = 0; index <= sampleCount; index++)
            {
                Vector2 candidate = safeBottom + Vector2.up * (index * step);
                if (!IsPositionClear(candidate))
                    continue;

                safeBottom = candidate;
                return true;
            }

            return false;
        }

        private Vector2 ApplyGroundClearance(
            Vector2 attackTargetPosition,
            Vector2 targetExtents)
        {
            if (worldMask.value == 0)
                return attackTargetPosition;

            float bodyClearance = GetBodyRadius() + navigationSkin;
            float probeDistance =
                Mathf.Max(bodyClearance, targetExtents.y) +
                bodyClearance +
                Mathf.Max(0.1f, pathValidationStep);
            int hitCount = Physics2D.RaycastNonAlloc(
                attackTargetPosition,
                Vector2.down,
                castHits,
                probeDistance,
                worldMask);
            RaycastHit2D nearestFloor = default;
            float nearestDistance = float.PositiveInfinity;
            for (int index = 0; index < hitCount; index++)
            {
                RaycastHit2D candidate = castHits[index];
                if (!IsRelevantWorldCollider(candidate.collider) ||
                    candidate.normal.y <= 0.2f ||
                    candidate.distance >= nearestDistance)
                {
                    continue;
                }

                nearestFloor = candidate;
                nearestDistance = candidate.distance;
            }

            if (nearestFloor.collider == null)
                return attackTargetPosition;

            float currentClearance = Vector2.Dot(
                attackTargetPosition - nearestFloor.point,
                nearestFloor.normal);
            if (currentClearance >= bodyClearance)
                return attackTargetPosition;

            return attackTargetPosition +
                   nearestFloor.normal * (bodyClearance - currentClearance);
        }

        private bool TryCalculateDiveTarget(
            Vector2 diveOrigin,
            Vector2 attackTarget,
            Vector2 targetExtents,
            out Vector2 safeDiveTarget,
            out float safeDiveDistance)
        {
            safeDiveTarget = ApplyGroundClearance(attackTarget, targetExtents);
            Vector2 diveVector = safeDiveTarget - diveOrigin;
            safeDiveDistance = diveVector.magnitude;
            if (safeDiveDistance <= ArrivalTolerance)
                return false;

            Vector2 diveDirection = diveVector / safeDiveDistance;
            if (TryGetNearestWorldSweep(
                    diveOrigin,
                    diveDirection,
                    safeDiveDistance,
                    out RaycastHit2D diveHit))
            {
                float allowedEndpointContact = GetBodyRadius() + navigationSkin + 0.1f;
                float remainingAfterHit = safeDiveDistance - diveHit.distance;
                if (remainingAfterHit > allowedEndpointContact)
                    return false;

                float clearDistance = Mathf.Max(0f, diveHit.distance - navigationSkin);
                safeDiveTarget = diveOrigin + diveDirection * clearDistance;
            }

            safeDiveDistance = Vector2.Distance(diveOrigin, safeDiveTarget);
            return safeDiveDistance > ArrivalTolerance;
        }

        private float ResolvePatrolHorizontalDirection()
        {
            Vector2 target = patrolTargetIndex == 0 ? PatrolPointAWorld : PatrolPointBWorld;
            Vector2 currentPosition = body != null
                ? body.position
                : (Vector2)transform.position;
            float direction = target.x - currentPosition.x;
            if (Mathf.Abs(direction) > 0.05f)
                return Mathf.Sign(direction);
            if (Mathf.Abs(movementDirection.x) > 0.05f)
                return Mathf.Sign(movementDirection.x);
            return 1f;
        }

        private CaveBatMoveResult MoveTowardsSafely(
            Vector2 target,
            float speed,
            bool recordBreadcrumb)
        {
            Vector2 origin = body.position;
            Vector2 offset = target - origin;
            float distance = offset.magnitude;
            if (distance <= ArrivalTolerance)
            {
                movementDirection = Vector2.zero;
                return CaveBatMoveResult.Reached;
            }

            Vector2 direction = offset / distance;
            float desiredDistance = float.IsPositiveInfinity(speed)
                ? distance
                : Mathf.Min(distance, Mathf.Max(0f, speed) * Time.fixedDeltaTime);
            float allowedDistance = GetAllowedMovementDistance(origin, direction, desiredDistance);
            if (allowedDistance <= 0.0001f)
            {
                movementDirection = Vector2.zero;
                return CaveBatMoveResult.Blocked;
            }

            Vector2 nextPosition = origin + direction * allowedDistance;
            body.MovePosition(nextPosition);
            movementDirection = direction;
            if (recordBreadcrumb)
                RecordBreadcrumb(nextPosition);

            if (allowedDistance + 0.0001f < desiredDistance)
                return CaveBatMoveResult.Blocked;
            return Vector2.Distance(nextPosition, target) <= ArrivalTolerance
                ? CaveBatMoveResult.Reached
                : CaveBatMoveResult.Moving;
        }

        private float GetAllowedMovementDistance(
            Vector2 origin,
            Vector2 direction,
            float desiredDistance)
        {
            if (desiredDistance <= 0f || worldMask.value == 0)
                return desiredDistance;

            if (!TryGetNearestWorldSweep(
                    origin,
                    direction,
                    desiredDistance + navigationSkin,
                    out RaycastHit2D hit))
            {
                return desiredDistance;
            }

            return Mathf.Min(
                desiredDistance,
                Mathf.Max(0f, hit.distance - navigationSkin));
        }

        private bool IsSegmentClear(Vector2 start, Vector2 end)
        {
            Vector2 offset = end - start;
            float distance = offset.magnitude;
            if (distance <= ArrivalTolerance || worldMask.value == 0)
                return true;

            return !TryGetNearestWorldSweep(
                start,
                offset / distance,
                distance + navigationSkin,
                out _);
        }

        private bool TryGetNearestWorldSweep(
            Vector2 origin,
            Vector2 direction,
            float distance,
            out RaycastHit2D nearestHit)
        {
            nearestHit = default;
            if (worldMask.value == 0 || distance <= 0f)
                return false;

            int hitCount = Physics2D.CircleCastNonAlloc(
                origin,
                GetBodyRadius() + navigationSkin,
                direction,
                castHits,
                distance,
                worldMask);
            float nearestDistance = float.PositiveInfinity;
            for (int index = 0; index < hitCount; index++)
            {
                RaycastHit2D candidate = castHits[index];
                if (!IsRelevantWorldCollider(candidate.collider) ||
                    candidate.distance >= nearestDistance)
                {
                    continue;
                }

                nearestDistance = candidate.distance;
                nearestHit = candidate;
            }

            return nearestHit.collider != null;
        }

        private bool IsPositionClear(Vector2 position)
        {
            if (worldMask.value == 0)
                return true;

            int count = Physics2D.OverlapCircleNonAlloc(
                position,
                GetBodyRadius() + navigationSkin,
                overlapHits,
                worldMask);
            for (int index = 0; index < count; index++)
            {
                if (IsRelevantWorldCollider(overlapHits[index]))
                    return false;
            }

            return true;
        }

        private bool IsRelevantWorldCollider(Collider2D candidate)
        {
            if (candidate == null || candidate.isTrigger)
                return false;
            if (candidate == bodyCollider)
                return false;
            Transform candidateTransform = candidate.transform;
            return candidateTransform != transform &&
                   !candidateTransform.IsChildOf(transform);
        }

        private float GetBodyRadius()
        {
            if (bodyCollider == null)
                return 0.45f;

            Vector3 scale = bodyCollider.transform.lossyScale;
            float largestScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            return Mathf.Max(0.01f, bodyCollider.radius * largestScale);
        }

        private void RecordBreadcrumb(Vector2 position)
        {
            if (travelledPath.Count > 0 &&
                (travelledPath[travelledPath.Count - 1] - position).sqrMagnitude < 0.000001f)
            {
                return;
            }

            travelledPath.Add(position);
        }

        private void ResumePatrol()
        {
            CurrentState = CaveBatState.Patrol;
            hasRecordedNoise = false;
            initialNoisePosition = default;
            latestNoisePosition = default;
            lockedDiveNoisePosition = default;
            initialAttackTargetPosition = default;
            latestAttackTargetPosition = default;
            lockedDiveTargetPosition = default;
            initialTargetExtents = default;
            latestTargetExtents = default;
            offensiveHitResolved = false;
            plannedUSamples.Clear();
            travelledPath.Clear();
            stateTimer = 0f;
            blockedTimer = 0f;
            uProgress = 0f;
            uFinishedPending = false;
            diveFinishedPending = false;
            movementDirection = Vector2.zero;
        }

        private void ResetRuntimeState()
        {
            CurrentState = CaveBatState.Patrol;
            hasRecordedNoise = false;
            initialNoisePosition = default;
            latestNoisePosition = default;
            lockedDiveNoisePosition = default;
            initialAttackTargetPosition = default;
            latestAttackTargetPosition = default;
            lockedDiveTargetPosition = default;
            initialTargetExtents = default;
            latestTargetExtents = default;
            offensiveHitResolved = false;
            plannedUSamples.Clear();
            travelledPath.Clear();
            patrolWaitTimer = 0f;
            stateTimer = 0f;
            blockedTimer = 0f;
            uProgress = 0f;
            uFinishedPending = false;
            diveFinishedPending = false;
            diveStartBreadcrumbIndex = 0;
            returnBreadcrumbIndex = -1;
            movementDirection = Vector2.zero;
            pendingContactDamage.Clear();
            nextContactDamageTime.Clear();
            activePlayerContacts.Clear();
            suppressContactDamageUntil = 0f;
            suppressContactDamageUntilSeparation = false;
        }

        private void ConfigureBody()
        {
            if (body == null)
                return;

            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.constraints |= RigidbodyConstraints2D.FreezeRotation;
        }

        private void CachePatrolFrame()
        {
            if (patrolFrameCached)
                return;

            Vector2 origin = body != null ? body.position : (Vector2)transform.position;
            patrolOrigin = origin;
            patrolRotation = transform.rotation;
            patrolFrameCached = true;
        }

        private Vector2 TransformPatrolPoint(Vector2 localPoint)
        {
            return patrolOrigin + (Vector2)(patrolRotation * (Vector3)localPoint);
        }

        private void ResolveReferences()
        {
            if (body == null)
                body = GetComponent<Rigidbody2D>();
            if (bodyCollider == null)
                bodyCollider = GetComponent<CircleCollider2D>();
            if (prototypeVisual == null)
                prototypeVisual = GetComponent<CaveBatPrototypeVisual>();
            if (perception == null)
                perception = GetComponent<EnemyPerception2D>();
            if (health == null)
                health = GetComponent<Health>();
        }

        private void Subscribe()
        {
            if (health != null)
            {
                health.OnDamaged -= HandleDamaged;
                health.OnDamaged += HandleDamaged;
                health.OnDied -= HandleDied;
                health.OnDied += HandleDied;
            }
        }

        private void Unsubscribe()
        {
            if (health != null)
            {
                health.OnDamaged -= HandleDamaged;
                health.OnDied -= HandleDied;
            }
        }

        private void HandleDamaged(
            Health damagedHealth,
            int receivedDamage,
            Vector2 sourcePosition)
        {
            suppressContactDamageUntilSeparation =
                activePlayerContacts.Count > 0 ||
                pendingContactDamage.Count > 0;
            pendingContactDamage.Clear();
            suppressContactDamageUntil = Mathf.Max(
                suppressContactDamageUntil,
                Time.time + playerHitContactGraceDuration);
            if (damagedHealth == null || damagedHealth.CurrentHP <= 0)
                return;

            switch (CurrentState)
            {
                case CaveBatState.Alert:
                case CaveBatState.Planning:
                    ResumePatrol();
                    break;
                case CaveBatState.Approach:
                case CaveBatState.UManeuver:
                case CaveBatState.Dive:
                    BeginRecovery(false);
                    break;
            }
        }

        public void SuppressContactDamageForDownAttack(int attackId)
        {
            if (attackId <= 0 || attackId == lastSuppressedDownAttackId)
                return;

            lastSuppressedDownAttackId = attackId;
            suppressContactDamageUntilSeparation = activePlayerContacts.Count > 0;
            suppressContactDamageUntil = Mathf.Max(
                suppressContactDamageUntil,
                Time.time + playerHitContactGraceDuration);
            pendingContactDamage.Clear();
        }

        private void HandleDied(Health deadHealth)
        {
            Unsubscribe();
            pendingContactDamage.Clear();
            nextContactDamageTime.Clear();
            activePlayerContacts.Clear();
            suppressContactDamageUntilSeparation = false;
            hasRecordedNoise = false;
            initialNoisePosition = default;
            latestNoisePosition = default;
            lockedDiveNoisePosition = default;
            initialAttackTargetPosition = default;
            latestAttackTargetPosition = default;
            lockedDiveTargetPosition = default;
            initialTargetExtents = default;
            latestTargetExtents = default;
            offensiveHitResolved = false;
            plannedUSamples.Clear();
            travelledPath.Clear();
            uFinishedPending = false;
            diveFinishedPending = false;
            movementDirection = Vector2.zero;
        }

        private static bool IsLayerAllowed(int layer, LayerMask mask)
        {
            return mask.value == 0 || (mask.value & (1 << layer)) != 0;
        }

        private Vector2 EvaluateUManeuver(float progress)
        {
            float t = Mathf.Clamp01(progress);
            if (t <= 0.5f)
                return EvaluateQuadratic(uEntry, uControlA, uBottom, t * 2f);

            return EvaluateQuadratic(uBottom, uControlB, uExit, (t - 0.5f) * 2f);
        }

        private static Vector2 EvaluateQuadratic(
            Vector2 start,
            Vector2 control,
            Vector2 end,
            float progress)
        {
            float t = Mathf.Clamp01(progress);
            float inverse = 1f - t;
            return inverse * inverse * start +
                   2f * inverse * t * control +
                   t * t * end;
        }

        private void OnDisable()
        {
            Unsubscribe();
            pendingContactDamage.Clear();
            nextContactDamageTime.Clear();
            activePlayerContacts.Clear();
            suppressContactDamageUntilSeparation = false;
            plannedUSamples.Clear();
            travelledPath.Clear();
            hasRecordedNoise = false;
            initialNoisePosition = default;
            latestNoisePosition = default;
            lockedDiveNoisePosition = default;
            initialAttackTargetPosition = default;
            latestAttackTargetPosition = default;
            lockedDiveTargetPosition = default;
            initialTargetExtents = default;
            latestTargetExtents = default;
            offensiveHitResolved = false;
            uFinishedPending = false;
            diveFinishedPending = false;
            movementDirection = Vector2.zero;
        }

        private void OnDestroy()
        {
            Unsubscribe();
            pendingContactDamage.Clear();
            nextContactDamageTime.Clear();
            activePlayerContacts.Clear();
            suppressContactDamageUntilSeparation = false;
        }

        private void OnValidate()
        {
            patrolSpeed = Mathf.Max(0f, patrolSpeed);
            patrolWaitDuration = Mathf.Max(0f, patrolWaitDuration);
            reactionDelay = Mathf.Max(0f, reactionDelay);
            approachSpeed = Mathf.Max(0.1f, approachSpeed);
            uWidth = Mathf.Max(0.2f, uWidth);
            uDepth = Mathf.Max(0.1f, uDepth);
            uDuration = Mathf.Max(0.1f, uDuration);
            diveStartHeight = Mathf.Max(0.2f, diveStartHeight);
            diveSpeed = Mathf.Max(0.1f, diveSpeed);
            attackDamage = Mathf.Max(1, attackDamage);
            contactDamageRetryInterval = Mathf.Max(0.05f, contactDamageRetryInterval);
            playerHitContactGraceDuration = Mathf.Max(0f, playerHitContactGraceDuration);
            recoverySpeed = Mathf.Max(0.1f, recoverySpeed);
            returnSpeed = Mathf.Max(0.1f, returnSpeed);
            blockedRetryDuration = Mathf.Max(0.1f, blockedRetryDuration);
            navigationSkin = Mathf.Max(0f, navigationSkin);
            pathValidationStep = Mathf.Max(0.05f, pathValidationStep);
            ResolveReferences();
        }

        private void OnDrawGizmosSelected()
        {
            if (!debugGizmos)
                return;

            Vector2 previewOrigin;
            Quaternion previewRotation;
            if (Application.isPlaying && patrolFrameCached)
            {
                previewOrigin = patrolOrigin;
                previewRotation = patrolRotation;
            }
            else
            {
                previewOrigin = transform.position;
                previewRotation = transform.rotation;
            }

            Vector2 patrolA = previewOrigin + (Vector2)(previewRotation * (Vector3)patrolPointA);
            Vector2 patrolB = previewOrigin + (Vector2)(previewRotation * (Vector3)patrolPointB);
            Gizmos.color = new Color(0.2f, 0.9f, 0.45f, 1f);
            Gizmos.DrawLine(patrolA, patrolB);
            Gizmos.DrawWireSphere(patrolA, 0.12f);
            Gizmos.DrawWireSphere(patrolB, 0.12f);

            Gizmos.color = Color.yellow;
            Gizmos.color = new Color(0.25f, 0.8f, 1f, 1f);
            Gizmos.DrawWireSphere(transform.position, GetBodyRadius() + navigationSkin);

            Vector2 pathStart = Application.isPlaying && body != null
                ? body.position
                : previewOrigin;
            Vector2 noisePreview = hasRecordedNoise
                ? initialNoisePosition
                : previewOrigin + Vector2.right * uWidth + Vector2.down * 0.5f;
            Vector2 attackTargetPreview = hasRecordedNoise
                ? initialAttackTargetPosition
                : noisePreview;
            bool hasBuiltPlan =
                Application.isPlaying &&
                hasRecordedNoise &&
                CurrentState != CaveBatState.Alert &&
                CurrentState != CaveBatState.Planning;
            Vector2 bottom = hasBuiltPlan ? uBottom : attackTargetPreview;
            BuildManeuverGeometry(pathStart, bottom, out Vector2 entry,
                out Vector2 controlA, out Vector2 controlB, out Vector2 exit);
            if (hasBuiltPlan)
            {
                entry = uEntry;
                controlA = uControlA;
                bottom = uBottom;
                controlB = uControlB;
                exit = uExit;
            }

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(pathStart, entry);
            Gizmos.DrawWireSphere(entry, 0.1f);

            Gizmos.color = new Color(0.85f, 0.2f, 1f, 1f);
            Vector2 previous = entry;
            const int gizmoSamples = 24;
            for (int index = 1; index <= gizmoSamples; index++)
            {
                float progress = index / (float)gizmoSamples;
                Vector2 point = progress <= 0.5f
                    ? EvaluateQuadratic(entry, controlA, bottom, progress * 2f)
                    : EvaluateQuadratic(bottom, controlB, exit, (progress - 0.5f) * 2f);
                Gizmos.DrawLine(previous, point);
                previous = point;
            }

            Gizmos.color = new Color(1f, 0.35f, 0.75f, 1f);
            Gizmos.DrawWireSphere(bottom, 0.14f);
            if (hasRecordedNoise)
            {
                Gizmos.color = new Color(1f, 0.75f, 0.15f, 1f);
                Gizmos.DrawWireSphere(initialNoisePosition, 0.11f);
                Gizmos.DrawLine(initialNoisePosition, initialAttackTargetPosition);
                Gizmos.color = new Color(0.35f, 1f, 0.45f, 1f);
                Gizmos.DrawWireSphere(initialAttackTargetPosition, 0.11f);
                Gizmos.DrawLine(initialAttackTargetPosition, bottom);
            }

            Vector2 divePreview = hasRecordedNoise
                ? latestAttackTargetPosition
                : attackTargetPreview;
            if (CurrentState == CaveBatState.Dive)
                divePreview = lockedDiveTargetPosition;
            Gizmos.color = Color.red;
            Gizmos.DrawLine(exit, divePreview);
            Gizmos.DrawWireSphere(divePreview, 0.12f);

            if (!Application.isPlaying || travelledPath.Count < 2)
                return;

            Gizmos.color = new Color(0.65f, 0.65f, 0.65f, 0.8f);
            for (int index = 1; index < travelledPath.Count; index++)
                Gizmos.DrawLine(travelledPath[index - 1], travelledPath[index]);
        }
    }
}

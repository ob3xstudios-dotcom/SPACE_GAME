using UnityEngine;
using Game.Enemies.Combat;

namespace Game.Enemies
{
    public enum EnemyMoveMode { Platformer, TopDown }

    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(EnemySensors))]
    [RequireComponent(typeof(EnemyMeleeAttack))]
    public class EnemyBase : MonoBehaviour
    {
        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        [Header("Mode")]
        [SerializeField] private EnemyMoveMode mode = EnemyMoveMode.TopDown;
        public EnemyMoveMode Mode => mode;

        [Header("Facing")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private bool flipVisualWithFacing = true;
        [SerializeField, Range(0.001f, 0.5f)] private float facingDeadzone = 0.05f;

        public int FacingDir { get; private set; } = 1;
        public Vector2 Forward => FacingDir < 0 ? Vector2.left : Vector2.right;

        [Header("Movement - Patrol")]
        [SerializeField] private Transform patrolPointA;
        [SerializeField] private Transform patrolPointB;
        [SerializeField] private Transform patrolCenterOverride;
        [SerializeField, Range(0.1f, 50f)] private float patrolFallbackHalfRange = 3f;
        [SerializeField, Range(0.1f, 12f)] private float patrolSpeed = 1.5f;
        [SerializeField, Range(0.1f, 50f)] private float patrolAcceleration = 14f;
        [SerializeField, Range(0f, 5f)] private float patrolWaitSeconds = 2f;
        [SerializeField] private bool patrolLoopAtoB = false;

        public float PatrolSpeed => patrolSpeed;
        public float PatrolAcceleration => patrolAcceleration;
        public float PatrolWaitSeconds => patrolWaitSeconds;
        public bool PatrolLoopAtoB => patrolLoopAtoB;
        public bool HasPatrolPoints => patrolPointA != null || patrolPointB != null;

        [Header("Movement - Chase")]
        [SerializeField, Range(0.5f, 20f)] private float chaseSpeed = 3.5f;
        [SerializeField, Range(0.1f, 80f)] private float chaseAcceleration = 20f;
        [SerializeField, Range(0f, 10f)] private float memorySeconds = 2.5f;
        public float ChaseSpeed => chaseSpeed;
        public float ChaseAcceleration => chaseAcceleration;
        public float MemorySeconds => memorySeconds;

        [Header("Combat - Attack Spacing")]
        [SerializeField, Range(0f, 5f)] private float attackRequiredDistanceX = 0.75f;
        [SerializeField, Range(0f, 5f)] private float attackAdvanceDistance = 0.45f;
        [SerializeField, Range(0f, 5f)] private float attackBackstepDistance = 0.6f;
        [SerializeField, Range(0.1f, 20f)] private float attackSpacingSpeed = 2.4f;
        [SerializeField, Range(0.1f, 30f)] private float attackAdvanceSpeed = 5.0f;
        [SerializeField, Range(0.1f, 80f)] private float attackSpacingAcceleration = 30f;
        [SerializeField, Range(0.1f, 80f)] private float attackAdvanceAcceleration = 45f;

        public float AttackRequiredDistanceX => attackRequiredDistanceX;
        public float AttackAdvanceDistance => attackAdvanceDistance;
        public float AttackBackstepDistance => attackBackstepDistance;
        public float AttackSpacingSpeed => attackSpacingSpeed;
        public float AttackAdvanceSpeed => attackAdvanceSpeed;
        public float AttackSpacingAcceleration => attackSpacingAcceleration;
        public float AttackAdvanceAcceleration => attackAdvanceAcceleration;

        [Header("Movement - Search")]
        [SerializeField, Range(0.1f, 20f)] private float searchSpeed = 2.5f;
        [SerializeField, Range(0.1f, 80f)] private float searchAcceleration = 18f;
        [SerializeField, Range(0.1f, 30f)] private float searchSeconds = 1.5f;
        [SerializeField, Range(0.05f, 5f)] private float searchArriveDistance = 0.35f;

        public float SearchSpeed => searchSpeed;
        public float SearchAcceleration => searchAcceleration;
        public float SearchSeconds => searchSeconds;
        public float SearchArriveDistance => searchArriveDistance;

        [Header("Movement - Return")]
        [SerializeField, Range(0.1f, 20f)] private float returnSpeed = 2.6f;
        [SerializeField, Range(0.1f, 80f)] private float returnAcceleration = 18f;
        [SerializeField, Range(0.05f, 5f)] private float returnArriveDistance = 0.35f;

        public float ReturnSpeed => returnSpeed;
        public float ReturnAcceleration => returnAcceleration;
        public float ReturnArriveDistance => returnArriveDistance;

        [Header("Jump Navigation")]
        [SerializeField, Min(0f)] private float enemyJumpForce = 8f;
        [SerializeField, Range(0.1f, 8f)] private float enemyMaxJumpDistance = 2.2f;
        [SerializeField, Range(0f, 3f)] private float enemyJumpCooldown = 0.8f;
        [SerializeField, Range(0.05f, 2f)] private float enemyObstacleCheckDistance = 0.45f;
        [SerializeField] private Transform enemyGroundCheck;
        [SerializeField] private Transform enemyGapCheck;
        [SerializeField, Range(0.05f, 1f)] private float enemyGroundCheckDistance = 0.2f;
        [SerializeField, Range(0.05f, 2f)] private float enemyLandingCheckHeight = 1.2f;
        [SerializeField, Range(0.05f, 1f)] private float enemyLowObstacleHeight = 0.25f;
        [SerializeField, Range(0.1f, 2f)] private float enemyHighObstacleHeight = 1.0f;
        [SerializeField, Range(0.2f, 6f)] private float playerAboveJumpHeight = 1.0f;
        [SerializeField, Range(0.2f, 5f)] private float playerAboveHorizontalRange = 1.4f;
        [SerializeField] private LayerMask enemyGroundLayer;
        [SerializeField] private LayerMask enemyObstacleLayer;
        [SerializeField] private bool debugJumpGizmos = true;

        public float EnemyJumpForce => enemyJumpForce;

        private Rigidbody2D rb;
        private Collider2D bodyCollider;
        private EnemySensors sensors;
        private EnemyMeleeAttack melee;

        [Header("Combat - Animator Driver")]
        [SerializeField] private EnemyMeleeAnimatorDriver meleeDriver;

        [Header("Animator")]
        [SerializeField] private Animator anim;
        [SerializeField] private string speedParam = "Speed";
        [SerializeField] private string isMovingParam = "IsMoving";
        [SerializeField, Range(0f, 1f)] private float movingThreshold = 0.05f;

        private int speedHash;
        private int isMovingHash;
        private IEnemyState currentState;

        public Rigidbody2D RB => rb;
        public EnemySensors Sensors => sensors;
        public EnemyMeleeAttack Melee => melee;
        public EnemyMeleeAnimatorDriver MeleeDriver => meleeDriver;
        public Transform Player => sensors != null ? sensors.Player : null;

        public bool IsParryable { get; private set; }
        public void SetParryable(bool v) => IsParryable = v;

        private float externalLockTimer;
        private Vector2 externalVelocity;

        [Header("Parry - Stun")]
        [SerializeField, Range(0f, 1f)] private float parryStunDecel = 60f;
        private float parryStunTimer;
        public bool IsStunned => parryStunTimer > 0f;
        private float jumpCooldownTimer;
        private float failedJumpRecoveryTimer;

        public Vector2 LastKnownPlayerPos { get; private set; }
        public Vector2 SearchTargetPos { get; private set; }
        public float TimeSinceLastSeen { get; private set; } = Mathf.Infinity;

        private Vector2 patrolCenter;

        public Vector2 PatrolCenter =>
            patrolCenterOverride != null ? (Vector2)patrolCenterOverride.position : patrolCenter;

        public Vector2 PatrolA =>
            patrolPointA != null ? (Vector2)patrolPointA.position : PatrolCenter + Vector2.left * patrolFallbackHalfRange;

        public Vector2 PatrolB =>
            patrolPointB != null ? (Vector2)patrolPointB.position : PatrolCenter + Vector2.right * patrolFallbackHalfRange;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            bodyCollider = GetComponent<Collider2D>();
            sensors = GetComponent<EnemySensors>();
            melee = GetComponent<EnemyMeleeAttack>();

            if (visualRoot == null) visualRoot = transform;
            if (meleeDriver == null) meleeDriver = GetComponentInChildren<EnemyMeleeAnimatorDriver>(true);
            if (anim == null) anim = GetComponentInChildren<Animator>(true);

            speedHash = Animator.StringToHash(speedParam);
            isMovingHash = Animator.StringToHash(isMovingParam);

            patrolCenter = rb.position;
            ApplyFacing(1);
        }

        private void Start()
        {
            sensors.ResolvePlayer();
            SetState(new States.EnemyPatrolState());
        }

        private void Update()
        {
            sensors.TickVision(rb.position);

            if (sensors.HasLineOfSight && Player != null)
            {
                LastKnownPlayerPos = Player.position;
                TimeSinceLastSeen = 0f;
            }
            else
            {
                TimeSinceLastSeen += Time.deltaTime;
            }

            if (jumpCooldownTimer > 0f)
                jumpCooldownTimer -= Time.deltaTime;

            if (failedJumpRecoveryTimer > 0f)
                failedJumpRecoveryTimer -= Time.deltaTime;

            UpdateAnimatorLocomotion();

            if (TryApplyGlobalStateLogic())
                return;

            currentState?.Tick(this);
        }

        private void FixedUpdate()
        {
            if (externalLockTimer > 0f)
            {
                externalLockTimer -= Time.fixedDeltaTime;
                rb.velocity = externalVelocity;
                SetFacingFromVelocity(rb.velocity.x);
                return;
            }

            if (parryStunTimer > 0f)
            {
                parryStunTimer -= Time.fixedDeltaTime;
                StopSmooth(parryStunDecel);
                return;
            }

            currentState?.FixedTick(this);
            SetFacingFromVelocity(rb.velocity.x);
        }

        private void UpdateAnimatorLocomotion()
        {
            if (anim == null || rb == null) return;

            float spd = rb.velocity.magnitude;
            anim.SetFloat(speedHash, spd);
            anim.SetBool(isMovingHash, spd > movingThreshold);
        }

        public void SetState(IEnemyState next)
        {
            if (next == null) return;
            if (currentState != null && currentState.GetType() == next.GetType()) return;

            string oldName = currentState != null ? currentState.GetType().Name : "None";
            string newName = next.GetType().Name;
            if (debugLogs)
                Debug.Log($"[STATE] {oldName} -> {newName}");

            currentState?.Exit(this);
            currentState = next;
            currentState.Enter(this);

            if (debugLogs)
                Debug.Log($"[ENEMY] {name} -> State: {currentState.GetType().Name}");
        }

        private bool TryApplyGlobalStateLogic()
        {
            if (!AllowsGlobalStateLogic())
                return false;

            if (CanSeePlayer())
            {
                if (currentState is States.EnemyChaseState)
                    return false;

                SetState(new States.EnemyChaseState());
                return true;
            }

            if (currentState is States.EnemyChaseState)
            {
                CaptureSearchTargetFromLastKnown();
                SetState(new States.EnemySearchState());
                return true;
            }

            return false;
        }

        private bool AllowsGlobalStateLogic()
        {
            if (currentState == null) return false;

            return currentState is States.EnemyIdleState
                || currentState is States.EnemyPatrolState
                || currentState is States.EnemyChaseState
                || currentState is States.EnemySearchState
                || currentState is States.EnemyReturnToPatrolState;
        }

        public void SetFacingFromVelocity(float vx)
        {
            if (Mathf.Abs(vx) < facingDeadzone) return;
            ApplyFacing(vx < 0f ? -1 : 1);
        }

        public void SetFacingTowards(Vector2 worldPos)
        {
            float dx = worldPos.x - rb.position.x;
            if (Mathf.Abs(dx) < facingDeadzone) return;
            ApplyFacing(dx < 0f ? -1 : 1);
        }

        private void ApplyFacing(int dir)
        {
            if (dir == 0) return;

            FacingDir = dir;

            if (!flipVisualWithFacing || visualRoot == null) return;

            Vector3 s = visualRoot.localScale;
            s.x = Mathf.Abs(s.x) * FacingDir;
            visualRoot.localScale = s;
        }

        public bool CanSeePlayer() => sensors != null && sensors.HasLineOfSight;
        public bool HasTargetInMemory => TimeSinceLastSeen <= memorySeconds;

        public void CaptureSearchTargetFromLastKnown()
        {
            float targetX = Player != null ? Player.position.x : LastKnownPlayerPos.x;
            SearchTargetPos = new Vector2(targetX, rb.position.y);
        }

        public bool IsPlayerInAttackRange()
        {
            return sensors != null && sensors.IsPlayerInAttackRange(rb.position);
        }

        public bool HasAttackSpacingToPlayer()
        {
            if (Player == null) return false;
            return Mathf.Abs(Player.position.x - rb.position.x) >= attackRequiredDistanceX;
        }

        public int DirectionToPlayerX()
        {
            if (Player == null) return FacingDir;

            float dx = Player.position.x - rb.position.x;
            if (Mathf.Abs(dx) < facingDeadzone) return FacingDir;
            return dx < 0f ? -1 : 1;
        }

        public void ApplyParryPush(Vector2 dir, float pushSpeed, float lockSeconds)
        {
            if (dir.sqrMagnitude < 0.0001f) dir = Forward;
            externalVelocity = dir.normalized * pushSpeed;
            externalLockTimer = Mathf.Max(externalLockTimer, lockSeconds);
            SetFacingFromVelocity(externalVelocity.x);
        }

        public void ApplyParryStun(float seconds)
        {
            if (seconds <= 0f) return;
            parryStunTimer = Mathf.Max(parryStunTimer, seconds);
        }

        public void MoveTowards(Vector2 targetPos, float maxSpeed, float accel)
        {
            Vector2 desired;

            if (Mode == EnemyMoveMode.Platformer)
            {
                int dir = targetPos.x < rb.position.x ? -1 : 1;
                if (IsGroundedForJump() && !HasGroundAhead(dir))
                {
                    StopSmooth(30f);
                    return;
                }

                desired = new Vector2(targetPos.x - rb.position.x, 0f);
                desired = desired.sqrMagnitude < 0.0001f ? Vector2.zero : desired.normalized * maxSpeed;

                float newVX = Mathf.MoveTowards(rb.velocity.x, desired.x, accel * Time.fixedDeltaTime);
                rb.velocity = new Vector2(newVX, rb.velocity.y);
                SetFacingFromVelocity(newVX);
                return;
            }

            desired = targetPos - rb.position;
            desired = desired.sqrMagnitude < 0.0001f ? Vector2.zero : desired.normalized * maxSpeed;

            float newVx = Mathf.MoveTowards(rb.velocity.x, desired.x, accel * Time.fixedDeltaTime);
            float newVy = Mathf.MoveTowards(rb.velocity.y, desired.y, accel * Time.fixedDeltaTime);

            rb.velocity = new Vector2(newVx, newVy);
            SetFacingFromVelocity(newVx);
        }

        public void MoveHorizontallyTo(float targetX, float maxSpeed, float accel)
        {
            float deltaX = targetX - rb.position.x;
            float desiredX = Mathf.Abs(deltaX) < 0.0001f ? 0f : Mathf.Sign(deltaX) * maxSpeed;
            float newVX = Mathf.MoveTowards(rb.velocity.x, desiredX, accel * Time.fixedDeltaTime);

            if (Mathf.Abs(deltaX) >= facingDeadzone)
            {
                int dir = deltaX < 0f ? -1 : 1;
                if (!CanMoveHorizontally(dir))
                {
                    StopSmooth(30f);
                    return;
                }
            }

            rb.velocity = Mode == EnemyMoveMode.Platformer
                ? new Vector2(newVX, rb.velocity.y)
                : new Vector2(newVX, 0f);

            SetFacingFromVelocity(newVX);
        }

        public bool CanMoveHorizontally(int dir)
        {
            if (dir == 0) return false;

            int moveDir = dir < 0 ? -1 : 1;
            if (HasWallAhead(moveDir)) return false;

            if (Mode == EnemyMoveMode.Platformer && IsGroundedForJump() && !HasGroundAhead(moveDir))
                return false;

            return true;
        }

        public bool MoveHorizontallyInDirection(int dir, float maxSpeed, float accel)
        {
            if (!CanMoveHorizontally(dir))
            {
                StopSmooth(30f);
                return false;
            }

            int moveDir = dir < 0 ? -1 : 1;
            float desiredX = moveDir * Mathf.Max(0f, maxSpeed);
            float newVX = Mathf.MoveTowards(rb.velocity.x, desiredX, Mathf.Max(0f, accel) * Time.fixedDeltaTime);

            rb.velocity = Mode == EnemyMoveMode.Platformer
                ? new Vector2(newVX, rb.velocity.y)
                : new Vector2(newVX, 0f);

            SetFacingFromVelocity(newVX);
            return true;
        }

        public bool TryStartNavigationJump(Vector2 targetPos, IEnemyState returnState)
        {
            if (Mode != EnemyMoveMode.Platformer) return false;
            if (jumpCooldownTimer > 0f) return false;
            if (failedJumpRecoveryTimer > 0f) return false;
            if (!IsGroundedForJump()) return false;

            float deltaX = targetPos.x - rb.position.x;
            bool playerIsAbove = ShouldJumpTowardHigherTarget(targetPos);
            if (Mathf.Abs(deltaX) < facingDeadzone && !playerIsAbove) return false;

            int dir = Mathf.Abs(deltaX) < facingDeadzone ? FacingDir : (deltaX < 0f ? -1 : 1);
            bool obstacleJump = RequiresNavigationJump(dir);
            if (!obstacleJump && !playerIsAbove) return false;

            if (playerIsAbove && !obstacleJump)
            {
                jumpCooldownTimer = enemyJumpCooldown;
                SetState(new States.EnemyJumpState(returnState, dir, rb.position.x));
                return true;
            }

            if (!TryFindJumpLanding(dir, targetPos, out Vector2 landingPos))
                return false;

            float jumpDistance = Mathf.Abs(landingPos.x - rb.position.x);
            if (jumpDistance > enemyMaxJumpDistance) return false;

            jumpCooldownTimer = enemyJumpCooldown;
            SetState(new States.EnemyJumpState(returnState, dir, landingPos.x));
            return true;
        }

        public void NotifyNavigationJumpFailed()
        {
            failedJumpRecoveryTimer = Mathf.Max(failedJumpRecoveryTimer, enemyJumpCooldown * 1.5f);
            jumpCooldownTimer = Mathf.Max(jumpCooldownTimer, enemyJumpCooldown);
        }

        public bool RequiresNavigationJump(Vector2 targetPos)
        {
            if (Mode != EnemyMoveMode.Platformer) return false;

            float deltaX = targetPos.x - rb.position.x;
            if (Mathf.Abs(deltaX) < facingDeadzone) return false;

            int dir = deltaX < 0f ? -1 : 1;
            return RequiresNavigationJump(dir) || ShouldJumpTowardHigherTarget(targetPos);
        }

        private bool RequiresNavigationJump(int dir)
        {
            return HasLowObstacleAhead(dir) || HasWallAhead(dir) || HasGapAhead(dir);
        }

        public bool IsGroundedForJump()
        {
            Vector2 origin = GroundProbeOrigin();
            return Physics2D.Raycast(origin, Vector2.down, enemyGroundCheckDistance, GroundMask()).collider != null;
        }

        private bool HasLowObstacleAhead(int dir)
        {
            Vector2 origin = rb.position;
            Vector2 lowOrigin = origin + Vector2.up * enemyLowObstacleHeight;
            Vector2 highOrigin = origin + Vector2.up * enemyHighObstacleHeight;
            Vector2 rayDir = dir < 0 ? Vector2.left : Vector2.right;

            bool lowBlocked = Physics2D.Raycast(lowOrigin, rayDir, enemyObstacleCheckDistance, ObstacleMask()).collider != null;
            bool highBlocked = Physics2D.Raycast(highOrigin, rayDir, enemyObstacleCheckDistance, ObstacleMask()).collider != null;

            return lowBlocked && !highBlocked;
        }

        private bool HasWallAhead(int dir)
        {
            Vector2 origin = bodyCollider != null ? (Vector2)bodyCollider.bounds.center : rb.position;
            Vector2 rayDir = dir < 0 ? Vector2.left : Vector2.right;
            return Physics2D.Raycast(origin, rayDir, enemyObstacleCheckDistance, ObstacleMask()).collider != null;
        }

        private bool ShouldJumpTowardHigherTarget(Vector2 targetPos)
        {
            float deltaY = targetPos.y - rb.position.y;
            if (deltaY < playerAboveJumpHeight) return false;
            if (Mathf.Abs(targetPos.x - rb.position.x) > playerAboveHorizontalRange) return false;

            Vector2 origin = rb.position + Vector2.up * enemyHighObstacleHeight;
            float checkDistance = Mathf.Max(0.05f, deltaY);
            return Physics2D.Raycast(origin, Vector2.up, checkDistance, ObstacleMask()).collider == null;
        }

        private bool HasGapAhead(int dir)
        {
            Vector2 basePos = GapProbeOrigin();
            Vector2 origin = basePos + Vector2.right * dir * enemyObstacleCheckDistance;
            return Physics2D.Raycast(origin, Vector2.down, enemyLandingCheckHeight, GroundMask()).collider == null;
        }

        public bool HasGroundAhead(int dir)
        {
            Vector2 basePos = GapProbeOrigin();
            Vector2 origin = basePos + Vector2.right * dir * enemyObstacleCheckDistance;
            return Physics2D.Raycast(origin, Vector2.down, enemyLandingCheckHeight, GroundMask()).collider != null;
        }

        private Vector2 GroundProbeOrigin()
        {
            if (enemyGroundCheck != null)
                return enemyGroundCheck.position;

            if (bodyCollider != null)
                return new Vector2(bodyCollider.bounds.center.x, bodyCollider.bounds.min.y + 0.03f);

            return rb.position;
        }

        private Vector2 GapProbeOrigin()
        {
            if (enemyGapCheck != null)
                return enemyGapCheck.position;

            if (bodyCollider != null)
                return new Vector2(bodyCollider.bounds.center.x, bodyCollider.bounds.min.y + 0.08f);

            return rb.position;
        }

        private bool TryFindJumpLanding(int dir, Vector2 targetPos, out Vector2 landingPos)
        {
            float desiredDistance = Mathf.Min(Mathf.Abs(targetPos.x - rb.position.x), enemyMaxJumpDistance);
            desiredDistance = Mathf.Max(desiredDistance, enemyObstacleCheckDistance);

            int steps = 4;
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float distance = Mathf.Lerp(desiredDistance, enemyObstacleCheckDistance, t);
                if (distance <= enemyObstacleCheckDistance * 1.25f)
                    continue;

                Vector2 origin = rb.position + Vector2.right * dir * distance + Vector2.up * enemyLandingCheckHeight;
                RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, enemyLandingCheckHeight * 2f, GroundMask());

                if (hit.collider == null) continue;

                landingPos = hit.point;
                return true;
            }

            landingPos = rb.position;
            return false;
        }

        private int GroundMask()
        {
            if (enemyGroundLayer.value != 0)
                return enemyGroundLayer.value;

            int mask = LayerMask.GetMask("Ground", "Wall");
            return mask != 0 ? mask : Physics2D.DefaultRaycastLayers;
        }

        private int ObstacleMask()
        {
            if (enemyObstacleLayer.value != 0)
                return enemyObstacleLayer.value;

            int mask = LayerMask.GetMask("Ground", "Wall");
            return mask != 0 ? mask : GroundMask();
        }

        public void StopSmooth(float decel = 20f)
        {
            float newVX = Mathf.MoveTowards(rb.velocity.x, 0f, decel * Time.fixedDeltaTime);

            if (Mode == EnemyMoveMode.Platformer)
            {
                rb.velocity = new Vector2(newVX, rb.velocity.y);
                return;
            }

            float newVY = Mathf.MoveTowards(rb.velocity.y, 0f, decel * Time.fixedDeltaTime);
            rb.velocity = new Vector2(newVX, newVY);
        }

        public bool IsAt(Vector2 pos, float dist) => Vector2.Distance(rb.position, pos) <= dist;

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!debugJumpGizmos) return;

            Vector2 origin = Application.isPlaying && rb != null ? rb.position : (Vector2)transform.position;
            int dir = Application.isPlaying ? FacingDir : 1;
            Vector2 rayDir = dir < 0 ? Vector2.left : Vector2.right;

            Gizmos.color = Color.green;
            Vector2 groundOrigin = Application.isPlaying && rb != null ? GroundProbeOrigin() : PreviewGroundProbeOrigin(origin);
            Gizmos.DrawLine(groundOrigin, groundOrigin + Vector2.down * enemyGroundCheckDistance);

            Gizmos.color = Color.red;
            Vector2 lowOrigin = origin + Vector2.up * enemyLowObstacleHeight;
            Vector2 highOrigin = origin + Vector2.up * enemyHighObstacleHeight;
            Gizmos.DrawLine(lowOrigin, lowOrigin + rayDir * enemyObstacleCheckDistance);
            Gizmos.DrawLine(highOrigin, highOrigin + rayDir * enemyObstacleCheckDistance);

            Gizmos.color = Color.yellow;
            Vector2 gapBase = Application.isPlaying && rb != null ? GapProbeOrigin() : PreviewGapProbeOrigin(origin);
            Vector2 gapOrigin = gapBase + Vector2.right * dir * enemyObstacleCheckDistance;
            Gizmos.DrawLine(gapOrigin, gapOrigin + Vector2.down * enemyLandingCheckHeight);

            Gizmos.color = Color.cyan;
            Vector2 landingProbe = origin + Vector2.right * dir * enemyMaxJumpDistance + Vector2.up * enemyLandingCheckHeight;
            Gizmos.DrawLine(landingProbe, landingProbe + Vector2.down * enemyLandingCheckHeight * 2f);
        }

        private Vector2 PreviewGroundProbeOrigin(Vector2 fallback)
        {
            if (enemyGroundCheck != null)
                return enemyGroundCheck.position;

            var col = GetComponent<Collider2D>();
            return col != null ? new Vector2(col.bounds.center.x, col.bounds.min.y + 0.03f) : fallback;
        }

        private Vector2 PreviewGapProbeOrigin(Vector2 fallback)
        {
            if (enemyGapCheck != null)
                return enemyGapCheck.position;

            var col = GetComponent<Collider2D>();
            return col != null ? new Vector2(col.bounds.center.x, col.bounds.min.y + 0.08f) : fallback;
        }
#endif
    }
}

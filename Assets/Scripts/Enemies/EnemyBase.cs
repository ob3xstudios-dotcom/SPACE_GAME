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
        [SerializeField] private bool debugLogs = true;

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
        [SerializeField, Range(0.1f, 12f)] private float patrolSpeed = 2.2f;
        [SerializeField, Range(0.1f, 50f)] private float patrolAcceleration = 14f;
        [SerializeField, Range(0f, 5f)] private float patrolWaitSeconds = 0.7f;
        [SerializeField] private bool patrolLoopAtoB = false;

        public float PatrolSpeed => patrolSpeed;
        public float PatrolAcceleration => patrolAcceleration;
        public float PatrolWaitSeconds => patrolWaitSeconds;
        public bool PatrolLoopAtoB => patrolLoopAtoB;

        [Header("Movement - Chase")]
        [SerializeField, Range(0.5f, 20f)] private float chaseSpeed = 3.5f;
        [SerializeField, Range(0.1f, 80f)] private float chaseAcceleration = 20f;
        public float ChaseSpeed => chaseSpeed;
        public float ChaseAcceleration => chaseAcceleration;

        [Header("Movement - Search")]
        [SerializeField, Range(0.1f, 20f)] private float searchSpeed = 2.8f;
        [SerializeField, Range(0.1f, 80f)] private float searchAcceleration = 18f;
        [SerializeField, Range(0.1f, 30f)] private float searchMaxSeconds = 4.0f;
        [SerializeField, Range(0.05f, 5f)] private float searchArriveDistance = 0.35f;

        public float SearchSpeed => searchSpeed;
        public float SearchAcceleration => searchAcceleration;
        public float SearchMaxSeconds => searchMaxSeconds;
        public float SearchArriveDistance => searchArriveDistance;

        [Header("Movement - Return")]
        [SerializeField, Range(0.1f, 20f)] private float returnSpeed = 2.6f;
        [SerializeField, Range(0.1f, 80f)] private float returnAcceleration = 18f;
        [SerializeField, Range(0.05f, 5f)] private float returnArriveDistance = 0.35f;

        public float ReturnSpeed => returnSpeed;
        public float ReturnAcceleration => returnAcceleration;
        public float ReturnArriveDistance => returnArriveDistance;

        private Rigidbody2D rb;
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

        public Vector2 LastKnownPlayerPos { get; private set; }
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

            UpdateAnimatorLocomotion();
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

            currentState?.Exit(this);
            currentState = next;
            currentState.Enter(this);

            if (debugLogs)
                Debug.Log($"[ENEMY] {name} -> State: {currentState.GetType().Name}");
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
        public bool HasTargetInMemory => sensors != null && sensors.HasTargetInMemory;

        public bool IsPlayerInAttackRange()
        {
            return sensors != null && sensors.IsPlayerInAttackRange(rb.position);
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
    }
}
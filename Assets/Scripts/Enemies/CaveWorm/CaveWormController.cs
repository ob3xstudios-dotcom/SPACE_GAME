using Game.Combat;
using Game.Enemies.Combat;
using Game.Events;
using UnityEngine;

namespace Game.Enemies.CaveWorm
{
    public enum CaveWormAttackDirection
    {
        Up,
        Down,
        Left,
        Right
    }

    public enum CaveWormState
    {
        Hidden,
        Alert,
        Emerging,
        Attack,
        Exposed,
        Retreating
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health))]
    public sealed class CaveWormController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameplayNoiseEventChannelSO noiseChannel;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Collider2D exposedBodyCollider;
        [SerializeField] private EnemyAttackHitbox attackHitbox;
        [SerializeField] private CaveWormPrototypeVisual prototypeVisual;

        [Header("Orientation")]
        [SerializeField] private CaveWormAttackDirection attackDirection =
            CaveWormAttackDirection.Up;

        [Header("Hearing")]
        [SerializeField, Min(0.1f)] private float hearingRadius = 8f;
        [SerializeField, Range(0f, 2f)] private float noiseSensitivity = 1f;

        [Header("Ambush Activation")]
        [SerializeField, Min(0.1f)] private float alertDuration = 4f;
        [Tooltip("Total size across the selected attack direction.")]
        [SerializeField, Min(0.1f)] private float activationZoneWidth = 5.5f;
        [Tooltip("Distance outward from the hole in the selected attack direction.")]
        [SerializeField, Min(0.1f)] private float activationZoneDepth = 3f;

        [Header("Timing")]
        [SerializeField, Min(0f)] private float attackTelegraphDuration = 0.2f;
        [SerializeField, Min(0.01f)] private float emergeDuration = 0.14f;
        [SerializeField, Min(0.02f)] private float attackActiveDuration = 0.2f;
        [SerializeField, Min(0f)] private float hideDelay = 0.15f;
        [SerializeField, Min(0.01f)] private float retreatDuration = 0.38f;

        [Header("Offensive Zone")]
        [SerializeField, Min(0.1f)] private float offensiveZoneWidth = 1.2f;
        [SerializeField, Min(1)] private int attackDamage = 1;
        [SerializeField, Min(0.1f)] private float offensiveZoneDepth = 1.6f;
        [SerializeField] private float offensiveZoneForwardOffset = 0.65f;
        [SerializeField] private LayerMask playerLayerMask = 1 << 9;

        [Header("Emergence")]
        [SerializeField, Min(0.05f)] private float emergeDistance = 2.25f;

        [Header("Debug")]
        [SerializeField] private bool debugGizmos = true;

        private Health health;
        private Vector3 exposedLocalPosition;
        private Vector3 transitionStartLocalPosition;
        private Vector3 visualBaseScale = Vector3.one;
        private Transform currentTarget;
        private float stateTimer;
        private bool impactFeedbackPlayed;
        private bool visualDefaultsCached;

        public CaveWormState CurrentState { get; private set; } = CaveWormState.Hidden;
        public CaveWormAttackDirection AttackDirection => attackDirection;
        public Vector2 LocalAttackVector
        {
            get
            {
                switch (attackDirection)
                {
                    case CaveWormAttackDirection.Up:
                        return Vector2.up;
                    case CaveWormAttackDirection.Down:
                        return Vector2.down;
                    case CaveWormAttackDirection.Left:
                        return Vector2.left;
                    case CaveWormAttackDirection.Right:
                        return Vector2.right;
                    default:
                        return Vector2.up;
                }
            }
        }
        public Vector2 WorldAttackVector =>
            ((Vector2)transform.TransformDirection(LocalAttackVector)).normalized;
        public float AttackRotationDegrees
        {
            get
            {
                switch (attackDirection)
                {
                    case CaveWormAttackDirection.Up:
                        return 0f;
                    case CaveWormAttackDirection.Down:
                        return 180f;
                    case CaveWormAttackDirection.Left:
                        return 90f;
                    case CaveWormAttackDirection.Right:
                        return -90f;
                    default:
                        return 0f;
                }
            }
        }
        public bool IsAttackActive =>
            CurrentState == CaveWormState.Emerging &&
            attackHitbox != null &&
            attackHitbox.IsActive;
        public float AttackTelegraphProgress =>
            CurrentState != CaveWormState.Attack
                ? 0f
                : attackTelegraphDuration <= 0f
                    ? 1f
                    : Mathf.Clamp01(stateTimer / attackTelegraphDuration);

        private Vector3 HiddenLocalPosition =>
            exposedLocalPosition - (Vector3)LocalAttackVector * emergeDistance;

        private void Awake()
        {
            ResolveReferences();
            CacheVisualDefaults();
            ApplyVisualOrientation();
            ConfigureAttackHitbox();
            ResetToHidden();
        }

        private void OnEnable()
        {
            ResolveReferences();
            CacheVisualDefaults();
            ApplyVisualOrientation();
            ConfigureAttackHitbox();
            Subscribe();
            ResetToHidden();
        }

        private void Update()
        {
            if (health != null && health.CurrentHP <= 0)
                return;

            switch (CurrentState)
            {
                case CaveWormState.Alert:
                    UpdateAlert();
                    break;
                case CaveWormState.Attack:
                    UpdateAttackTelegraph();
                    break;
                case CaveWormState.Emerging:
                    UpdateEmerging();
                    break;
                case CaveWormState.Exposed:
                    UpdateExposed();
                    break;
                case CaveWormState.Retreating:
                    UpdateRetreating();
                    break;
            }
        }

        private void HandleNoise(GameplayNoiseEvent noiseEvent)
        {
            if (!isActiveAndEnabled || noiseEvent.Emitter == null)
                return;
            if (CurrentState != CaveWormState.Hidden && CurrentState != CaveWormState.Alert)
                return;
            if (!IsLayerAllowed(noiseEvent.Emitter.layer))
                return;

            float effectiveRadius = hearingRadius *
                                    Mathf.Clamp01(noiseEvent.Intensity * noiseSensitivity);
            if (effectiveRadius <= 0f ||
                ((Vector2)transform.position - noiseEvent.Position).sqrMagnitude >
                effectiveRadius * effectiveRadius)
            {
                return;
            }

            currentTarget = noiseEvent.Emitter.transform;
            if (CurrentState == CaveWormState.Hidden)
                BeginAlert();
            else
                stateTimer = 0f;
        }

        private void BeginAlert()
        {
            DisableAttackHitbox();
            SetBodyColliderEnabled(false);
            SetVisualPosition(HiddenLocalPosition);
            CurrentState = CaveWormState.Alert;
            stateTimer = 0f;
        }

        private void UpdateAlert()
        {
            stateTimer += Time.deltaTime;
            if (IsTargetWithinAmbushZone())
            {
                BeginAttackTelegraph();
                return;
            }

            if (stateTimer >= alertDuration)
                ResetToHidden();
        }

        private void BeginAttackTelegraph()
        {
            DisableAttackHitbox();
            CurrentState = CaveWormState.Attack;
            stateTimer = 0f;
            impactFeedbackPlayed = false;
        }

        private void UpdateAttackTelegraph()
        {
            stateTimer += Time.deltaTime;
            if (stateTimer >= attackTelegraphDuration)
                BeginEmerging();
        }

        private void BeginEmerging()
        {
            CurrentState = CaveWormState.Emerging;
            stateTimer = 0f;
            transitionStartLocalPosition = visualRoot != null
                ? visualRoot.localPosition
                : HiddenLocalPosition;
            SetBodyColliderEnabled(true);
            ConfigureAttackHitbox();
            if (attackHitbox != null)
                attackHitbox.Activate(WorldAttackVector);
        }

        private void UpdateEmerging()
        {
            stateTimer += Time.deltaTime;
            float progress = emergeDuration <= 0f
                ? 1f
                : Mathf.Clamp01(stateTimer / emergeDuration);
            SetVisualPosition(Vector3.Lerp(
                transitionStartLocalPosition,
                exposedLocalPosition,
                FastLaunchProgress(progress)));

            TryPlayConfirmedImpactFeedback();

            if (attackHitbox != null &&
                attackHitbox.IsActive &&
                stateTimer >= attackActiveDuration)
            {
                DisableAttackHitbox();
            }

            float strikeDuration = Mathf.Max(emergeDuration, attackActiveDuration);
            if (stateTimer < strikeDuration)
                return;

            SetVisualPosition(exposedLocalPosition);
            DisableAttackHitbox();
            BeginExposed();
        }

        private void BeginExposed()
        {
            CurrentState = CaveWormState.Exposed;
            stateTimer = 0f;
        }

        private void UpdateExposed()
        {
            stateTimer += Time.deltaTime;
            if (stateTimer >= hideDelay)
                BeginRetreat();
        }

        private void BeginRetreat()
        {
            DisableAttackHitbox();
            CurrentState = CaveWormState.Retreating;
            stateTimer = 0f;
            transitionStartLocalPosition = visualRoot != null
                ? visualRoot.localPosition
                : exposedLocalPosition;
        }

        private void UpdateRetreating()
        {
            stateTimer += Time.deltaTime;
            float progress = retreatDuration <= 0f
                ? 1f
                : Mathf.Clamp01(stateTimer / retreatDuration);
            SetVisualPosition(Vector3.Lerp(
                transitionStartLocalPosition,
                HiddenLocalPosition,
                SmoothProgress(progress)));

            if (progress < 1f)
                return;

            ResetToHidden();
        }

        private bool IsTargetWithinAmbushZone()
        {
            if (currentTarget == null || !currentTarget.gameObject.activeInHierarchy)
                return false;

            Vector2 localOffset = transform.InverseTransformPoint(currentTarget.position);
            Vector2 forward = LocalAttackVector;
            Vector2 lateral = new Vector2(-forward.y, forward.x);
            float forwardDistance = Vector2.Dot(localOffset, forward);
            float lateralDistance = Mathf.Abs(Vector2.Dot(localOffset, lateral));
            return forwardDistance >= 0f &&
                   forwardDistance <= activationZoneDepth &&
                   lateralDistance <= activationZoneWidth * 0.5f;
        }

        private void TryPlayConfirmedImpactFeedback()
        {
            if (impactFeedbackPlayed ||
                attackHitbox == null ||
                attackHitbox.HitCountThisActivation <= 0)
            {
                return;
            }

            impactFeedbackPlayed = true;
            prototypeVisual?.PlayConfirmedImpact(currentTarget);
        }

        private void ConfigureAttackHitbox()
        {
            if (attackHitbox == null)
                return;

            Transform damageSource = visualRoot != null ? visualRoot : transform;
            attackHitbox.Configure(damageSource, attackDamage, playerLayerMask, false);
            attackHitbox.SetBox(
                new Vector2(0f, offensiveZoneForwardOffset),
                new Vector2(offensiveZoneWidth, offensiveZoneDepth));
            attackHitbox.DisableHitbox();
        }

        private void ApplyVisualOrientation()
        {
            if (visualRoot == null)
                return;

            visualRoot.localRotation = Quaternion.Euler(0f, 0f, AttackRotationDegrees);
            visualRoot.localScale = visualBaseScale;
        }

        private void DisableAttackHitbox()
        {
            if (attackHitbox != null)
                attackHitbox.DisableHitbox();
        }

        private void ResolveReferences()
        {
            if (visualRoot == null)
                visualRoot = transform.Find("Visual");
            if (exposedBodyCollider == null && visualRoot != null)
                exposedBodyCollider = visualRoot.GetComponent<Collider2D>();
            if (exposedBodyCollider == null)
                exposedBodyCollider = GetComponent<Collider2D>();
            if (attackHitbox == null)
                attackHitbox = GetComponentInChildren<EnemyAttackHitbox>(true);
            if (prototypeVisual == null)
                prototypeVisual = GetComponent<CaveWormPrototypeVisual>();
            if (health == null)
                health = GetComponent<Health>();
        }

        private void CacheVisualDefaults()
        {
            if (visualDefaultsCached || visualRoot == null)
                return;

            exposedLocalPosition = visualRoot.localPosition;
            visualBaseScale = visualRoot.localScale;
            visualDefaultsCached = true;
        }

        private void Subscribe()
        {
            noiseChannel?.Subscribe(HandleNoise);
            if (health != null)
            {
                health.OnDied -= HandleDied;
                health.OnDied += HandleDied;
            }
        }

        private void Unsubscribe()
        {
            noiseChannel?.Unsubscribe(HandleNoise);
            if (health != null)
                health.OnDied -= HandleDied;
        }

        private void HandleDied(Health deadHealth)
        {
            DisableAttackHitbox();
            SetBodyColliderEnabled(false);
            prototypeVisual?.ClearConfirmedImpact();
            Unsubscribe();
            currentTarget = null;
            CurrentState = CaveWormState.Hidden;
        }

        private void ResetToHidden()
        {
            DisableAttackHitbox();
            SetBodyColliderEnabled(false);
            if (visualDefaultsCached)
            {
                SetVisualPosition(HiddenLocalPosition);
                ApplyVisualOrientation();
            }

            CurrentState = CaveWormState.Hidden;
            stateTimer = 0f;
            impactFeedbackPlayed = false;
            currentTarget = null;
        }

        private void SetBodyColliderEnabled(bool enabled)
        {
            if (exposedBodyCollider != null)
                exposedBodyCollider.enabled = enabled;
        }

        private void SetVisualPosition(Vector3 localPosition)
        {
            if (visualRoot != null)
                visualRoot.localPosition = localPosition;
        }

        private bool IsLayerAllowed(int layer)
        {
            return playerLayerMask.value == 0 ||
                   (playerLayerMask.value & (1 << layer)) != 0;
        }

        private static float FastLaunchProgress(float progress)
        {
            return 1f - (1f - progress) * (1f - progress);
        }

        private static float SmoothProgress(float progress)
        {
            return progress * progress * (3f - 2f * progress);
        }

        private void OnDisable()
        {
            Unsubscribe();
            prototypeVisual?.ClearConfirmedImpact();
            ResetToHidden();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            DisableAttackHitbox();
        }

        private void OnValidate()
        {
            hearingRadius = Mathf.Max(0.1f, hearingRadius);
            noiseSensitivity = Mathf.Clamp(noiseSensitivity, 0f, 2f);
            alertDuration = Mathf.Max(0.1f, alertDuration);
            activationZoneWidth = Mathf.Max(0.1f, activationZoneWidth);
            activationZoneDepth = Mathf.Max(0.1f, activationZoneDepth);
            attackTelegraphDuration = Mathf.Max(0f, attackTelegraphDuration);
            emergeDuration = Mathf.Max(0.01f, emergeDuration);
            attackActiveDuration = Mathf.Max(0.02f, attackActiveDuration);
            hideDelay = Mathf.Max(0f, hideDelay);
            retreatDuration = Mathf.Max(0.01f, retreatDuration);
            offensiveZoneWidth = Mathf.Max(0.1f, offensiveZoneWidth);
            attackDamage = Mathf.Max(1, attackDamage);
            offensiveZoneDepth = Mathf.Max(0.1f, offensiveZoneDepth);
            emergeDistance = Mathf.Max(0.05f, emergeDistance);
            ResolveReferences();
            CacheVisualDefaults();
            ApplyVisualOrientation();
            ConfigureAttackHitbox();
        }

        private void OnDrawGizmosSelected()
        {
            if (!debugGizmos)
                return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, hearingRadius);

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix *
                            Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, AttackRotationDegrees));

            Gizmos.color = new Color(1f, 0.55f, 0.05f, 1f);
            Gizmos.DrawWireCube(
                Vector3.up * (activationZoneDepth * 0.5f),
                new Vector3(activationZoneWidth, activationZoneDepth, 0f));

            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(
                Vector3.up * offensiveZoneForwardOffset,
                new Vector3(offensiveZoneWidth, offensiveZoneDepth, 0f));

            Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.75f);
            Gizmos.DrawLine(Vector3.down * emergeDistance, Vector3.zero);
            Gizmos.matrix = previousMatrix;
        }
    }
}

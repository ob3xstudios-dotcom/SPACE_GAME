using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Game.Barrels;
using Game.Interaction;
using Game.Input;
using Game.Progression;
using Game.Systems;
using Game.UI;
using Game.World;
using Game.World.Factory.WoodProcessing;
using Game.World.Traversal;

namespace Game.Player
{
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CapsuleCollider2D))]
    public class PlayerController : MonoBehaviour
    {
        [Serializable]
        private struct PostureColliderProfile
        {
            public Vector2 size;
            public Vector2 offset;
            public CapsuleDirection2D direction;

            public PostureColliderProfile(
                Vector2 size,
                Vector2 offset,
                CapsuleDirection2D direction)
            {
                this.size = size;
                this.offset = offset;
                this.direction = direction;
            }
        }

        public enum SprintSlideDiveState
        {
            None,
            Slide,
            Dive,
            ProneSlide
        }

        [Header("Referencias")]
        public InputReader input;
        public CharacterStats stats;

        [Header("Debug")]
        public bool debugLogs = false;
        [SerializeField] private bool showSprintDebugOverlay = true;

        [Header("Stamina")]
        [SerializeField] private PlayerStamina stamina;
        [SerializeField, Min(0)] private int dashStaminaCost = 1;

        [Header("Facing / Visual")]
        [SerializeField] private Transform visualRoot;

        [Header("Posture Colliders")]
        [SerializeField] private PostureColliderProfile standingColliderProfile =
            new PostureColliderProfile(
                new Vector2(1.019415f, 1.820116f),
                new Vector2(-0.009708f, -0.0023412f),
                CapsuleDirection2D.Vertical);
        [SerializeField] private PostureColliderProfile crouchingColliderProfile =
            new PostureColliderProfile(
                new Vector2(1.019415f, 1.42f),
                new Vector2(-0.009708f, -0.2023992f),
                CapsuleDirection2D.Vertical);
        [SerializeField] private PostureColliderProfile layDownColliderProfile =
            new PostureColliderProfile(
                new Vector2(1.60f, 0.76f),
                new Vector2(-0.009708f, -0.5323992f),
                CapsuleDirection2D.Horizontal);
        [SerializeField] private PostureColliderProfile slideColliderProfile =
            new PostureColliderProfile(
                new Vector2(1.60f, 0.76f),
                new Vector2(-0.009708f, -0.5323992f),
                CapsuleDirection2D.Horizontal);
        [SerializeField] private PostureColliderProfile diveColliderProfile =
            new PostureColliderProfile(
                new Vector2(1.80f, 0.70f),
                new Vector2(0.08f, -0.20f),
                CapsuleDirection2D.Horizontal);
        [SerializeField, Min(0f)] private float postureClearanceSkin = 0.02f;

        // --------------------
        // GROUND / WALL
        // --------------------
        [Header("Ground Check")]
        public Transform groundCheck;
        public float groundCheckRadius = 0.2f;
        [SerializeField] private Vector2 groundCheckBoxSize = new Vector2(0.70f, 0.08f);
        [SerializeField, Range(0.01f, 0.25f)] private float groundCheckDistance = 0.08f;
        public LayerMask groundLayer;
        [Tooltip("Capas de actores que pueden servir como suelo solo si llevan PlayerStandableSurface.")]
        [SerializeField] private LayerMask standableActorMask = 256;
        [Tooltip("Solo cuenta como suelo si la normal apunta hacia arriba (evita que pared lateral te ponga grounded).")]
        [SerializeField, Range(0f, 1f)] private float groundNormalMinY = 0.55f;
        [SerializeField, Min(0f)] private float slopeStickSpeed = 2f;
        private const float SlopeNormalMaxY = 0.999f;

        [Header("Standable Surface Stability")]
        [SerializeField, Range(0.01f, 0.25f)] private float standableIdleInputThreshold = 0.05f;
        [SerializeField, Min(0f)] private float standableDriftCorrectionSpeed = 2f;
        [SerializeField, Range(0.08f, 0.15f)] private float releasedSupportIgnoreDuration = 0.15f;

        [Header("Wall Check")]
        public Transform wallCheck;
        public float wallCheckDistance = 0.25f;
        public LayerMask wallLayer;
        [Tooltip("Solo cuenta como pared si la normal apunta lateralmente.")]
        [SerializeField, Range(0f, 1f)] private float wallNormalMinX = 0.55f;

        [Header("Tuning (Designer) - Wall Feel")]
        [SerializeField, Range(0.1f, 3f)] private float wallSlideSpeedMultiplier = 0.35f;
        [SerializeField, Range(0.4f, 4f)] private float wallSlideTargetFallSpeed = 1.2f;
        [SerializeField, Range(0f, 0.3f)] private float wallSlideExitFastFallTime = 0.10f;
        [SerializeField, Range(0f, 20f)] private float wallSlideExitFastFallSpeed = 7f;
        [SerializeField, Range(0f, 0.3f)] private float wallStickTime = 0.08f;
        [SerializeField, Range(0f, 0.3f)] private float wallCoyoteTime = 0.14f;
        [SerializeField, Range(0.05f, 0.3f)] private float wallJumpLockTime = 0.10f;
        [SerializeField, Range(0f, 0.3f)] private float wallJumpRegrabDelay = 0.12f;
        [SerializeField, Range(0f, 2f)] private float narrowWallProbeDistance = 1.2f;
        [SerializeField, Range(0.3f, 1f)] private float narrowWallJumpForceXMultiplier = 0.65f;
        [SerializeField, Range(0f, 1f)] private float wallSlideEdgeVisibleFractionThreshold = 0.5f;

        [Header("Tuning (Designer) - Wall Input")]
        [SerializeField, Range(0.01f, 0.5f)] private float wallInputDeadzone = 0.2f;
        [SerializeField, Range(0.01f, 0.2f)] private float facingInputDeadzone = 0.15f;
        [SerializeField, Range(0.2f, 1f)] private float airborneFacingInputThreshold = 0.55f;

        [Header("Wall Debug Temporal")]
        [SerializeField] private bool logWallDebug = false;
        [SerializeField, Range(0.05f, 2f)] private float wallDebugLogInterval = 0.25f;

        [Header("Corner Anti-Stuck")]
        [SerializeField] private bool enableCornerAntiStuck = true;
        [SerializeField, Range(0.01f, 0.3f)] private float cornerStuckVerticalSpeedThreshold = 0.08f;
        [SerializeField, Range(0.02f, 0.5f)] private float cornerStuckTime = 0.12f;
        [SerializeField, Range(0f, 0.5f)] private float cornerPushOutSpeed = 0.8f;
        [SerializeField, Range(-5f, 0f)] private float cornerForceFallSpeed = -1.2f;
        [SerializeField, Range(0.02f, 0.5f)] private float cornerStuckProbeDistance = 0.12f;
        [SerializeField, Range(0f, 1f)] private float cornerStuckHorizontalSpeedThreshold = 0.15f;
        [SerializeField, Range(0f, 0.2f)] private float recentlyGroundedAntiStuckGraceTime = 0.08f;
        [SerializeField] private bool logCornerStuckDebug = false;
        [SerializeField] private Vector3[] cornerCorrectionOffsets =
        {
            new Vector3(0f, 0.05f, 0f),
            new Vector3(0f, 0.10f, 0f),
            new Vector3(0f, 0.15f, 0f),
            new Vector3(-0.05f, 0.05f, 0f),
            new Vector3(-0.10f, 0.05f, 0f),
            new Vector3(-0.05f, 0.10f, 0f),
            new Vector3(-0.10f, 0.10f, 0f),
            new Vector3(-0.05f, 0.15f, 0f),
            new Vector3(-0.10f, 0.15f, 0f)
        };

        // --------------------
        // LEDGE GRAB / HANG
        // --------------------
        [Header("Ledge Grab (Designer)")]
        [SerializeField] private bool enableLedgeGrab = true;
        [SerializeField, Range(0.05f, 0.30f)] private float ledgeGrabCooldown = 0.12f;
        [Tooltip("Permite agarrar si vy <= este valor. (10 = permite también subiendo).")]
        [SerializeField, Range(-5f, 20f)] private float ledgeGrabMaxVerticalSpeedToAllow = 10f;

        [Tooltip("Capas que cuentan como 'suelo' arriba del borde (normalmente Ground + Wall).")]
        [SerializeField] private LayerMask ledgeTopLayer;

        [Header("Ledge Probe (unidades mundo)")]
        [Tooltip("Distancia hacia delante desde el borde del collider para buscar pared (manos).")]
        [SerializeField, Range(0.05f, 0.6f)] private float ledgeForwardCheck = 0.20f;
        [Tooltip("Altura desde el TOP del capsule hacia ABAJO donde están las manos (0.15–0.35 suele ir bien).")]
        [SerializeField, Range(0.05f, 0.6f)] private float ledgeHandsFromTop = 0.22f;
        [Tooltip("Altura extra por encima de manos para comprobar espacio libre (cabeza).")]
        [SerializeField, Range(0.05f, 0.8f)] private float ledgeHeadClearance = 0.30f;
        [Tooltip("Adelante extra para tirar el ray down y encontrar la plataforma superior.")]
        [SerializeField, Range(0.00f, 0.6f)] private float ledgeTopForward = 0.12f;
        [Tooltip("Cuánto baja el ray para encontrar la plataforma superior.")]
        [SerializeField, Range(0.05f, 0.8f)] private float ledgeTopDown = 0.30f;
        [Tooltip("Separación mínima respecto a la pared al colgarse (evita meterse hacia dentro / centrarse).")]
        [SerializeField, Range(0.00f, 0.20f)] private float hangWallSkin = 0.04f;
        [Tooltip("Máxima distancia que puede 'snapear' al entrar en hang (para que no se vaya al centro).")]
        [SerializeField, Range(0.05f, 1.0f)] private float maxHangSnapDistance = 0.35f;
        [Tooltip("Tiempo que se conserva una esquina valida aunque el ray la pierda durante un instante.")]
        [SerializeField, Range(0f, 0.25f)] private float ledgeGrabGraceTime = 0.12f;
        [Tooltip("Distancia maxima para hacer snap claro al punto fijo del hang.")]
        [SerializeField, Range(0.05f, 1.5f)] private float ledgeSnapDistance = 0.75f;
        [Tooltip("Margen vertical alrededor de las manos para encontrar esquina.")]
        [SerializeField, Range(0f, 0.6f)] private float ledgeVerticalTolerance = 0.28f;

        [Tooltip("Offset final aplicado al punto corner (X se multiplica por facingDir).")]
        [SerializeField] private Vector2 ledgeHangOffset = new Vector2(0.10f, -0.22f);

        [Header("Ledge Hang / Exit")]
        [SerializeField] private float ledgeClimbJumpY = 10f;
        [SerializeField] private float ledgeClimbJumpX = 1.5f;
        [SerializeField, Range(0f, 0.4f)] private float ledgeClimbDuration = 0.18f;
        [SerializeField, Range(0f, 0.3f)] private float ledgeClimbColliderDisableTime = 0.10f;

        [Tooltip("Si ON, exige empujar hacia la pared para poder agarrar. En prototipo suele ir mejor OFF.")]
        [SerializeField] private bool requirePushIntoWallToGrab = false;

        [Header("Hang - PM Rules")]
        [Tooltip("Umbral para considerar DOWN (MoveInput.y <= -threshold).")]
        [SerializeField, Range(0.1f, 1f)] private float hangDownThreshold = 0.5f;

        [Tooltip("Si mantienes DOWN tras soltarte del hang, se cancela el wall check hasta soltar DOWN.")]
        [SerializeField] private bool disableWallWhileDownHeldAfterDrop = true;

        [Header("Ledge Debug Temporal")]
        [SerializeField] private bool drawLedgeDebug = true;
        [SerializeField] private bool logLedgeDebug = false;
        [SerializeField, Range(0.05f, 2f)] private float ledgeDebugLogInterval = 0.25f;

        [Header("Wall Jump Anti-Climb")]
        [SerializeField] private bool preventSameWallClimb = true;
        [SerializeField, Range(0f, 2f)] private float sameWallJumpYReleaseMargin = 0.8f;

        // --------------------
        // COMBAT
        // --------------------
        [Header("Combat")]
        public Transform attackPoint;
        public float attackRadius = 0.5f;
        public LayerMask enemyLayer;

        [Header("Combat - Aim")]
        [SerializeField] private bool allowVerticalAim = true;
        [SerializeField, Range(0f, 2f)] private float attackOffset = 0f;

        [Header("Combat - Up Attack")]
        [SerializeField, Range(0f, 2f)] private float upAttackOffset = 0.62f;
        [SerializeField, Range(0.01f, 2f)] private float upAttackRadius = 0.5f;

        [Header("Combat - Direction thresholds")]
        [SerializeField, Range(0.1f, 1f)] private float attackDownThreshold = 0.35f;
        [SerializeField, Range(0.1f, 1f)] private float attackUpThreshold = 0.35f;

        [Header("Combat Timing")]
        public float attackCooldown = 0.3f;
        [SerializeField, Range(0.05f, 0.6f)] private float attackLockSeconds = 0.22f;
        [SerializeField, Range(0f, 0.5f)] private float attackCancelEnableSeconds = 0.06f;

        [Header("Combat Feel")]
        [SerializeField, Range(0f, 0.12f)] private float hitStopSeconds = 0.05f;
        [SerializeField, Range(0f, 25f)] private float pogoBounceForce = 12f;

        [Header("Combat - Enemy Down Hit Bounce")]
        [SerializeField, Range(0f, 25f)] private float downHitBounceVertical = 14f;
        [SerializeField, Range(0f, 10f)] private float downHitBounceHorizontal = 3.5f;
        [SerializeField, Range(0f, 25f)] private float tankDownHitBounceVertical = 8f;
        [SerializeField, Range(0f, 10f)] private float tankDownHitBounceHorizontal = 5f;
        [SerializeField, Range(0.15f, 0.5f)] private float tankDownHitSupportIgnoreMaxDuration = 0.35f;

        // --------------------
        // STEALTH MOVEMENT
        // --------------------
        [Header("Stealth Movement")]
        [SerializeField, Range(0.1f, 1f)] private float crouchSpeedMultiplier = 0.6f;
        [SerializeField, Range(0.1f, 1f)] private float layDownSpeedMultiplier = 0.35f;
        [SerializeField, Range(0.1f, 1f)] private float stealthWalkSpeedMultiplier = 0.5f;
        [SerializeField, Range(0.1f, 0.95f)] private float analogStealthWalkMaxMagnitude = 0.55f;
        [SerializeField, Min(1f)] private float sprintSpeedMultiplier = 1.4f;
        [SerializeField, Min(0f)] private float sprintMaxDuration = 1f;
        [SerializeField, Min(0f)] private float sprintRegenDelay = 1f;
        [SerializeField, Min(0.01f)] private float sprintFullRechargeTime = 4f;
        [SerializeField, Min(0f)] private float sprintMomentumDuration = 0.7f;
        [SerializeField, Min(0f)] private float sprintLandingMomentumDuration = 0.7f;

        [Header("Sprint Slide / Forward Dive")]
        [SerializeField, Min(0.05f)] private float slideDuration = 0.65f;
        [SerializeField, Min(0f)] private float slideInitialHorizontalSpeed = 10.5f;
        [SerializeField, Min(0f)] private float slideDeceleration = 8f;
        [SerializeField, Min(0f)] private float diveHorizontalImpulse = 3f;
        [SerializeField, Min(0f)] private float diveVerticalImpulse = 5.2f;
        [SerializeField, Min(0.01f)] private float postDiveSlideDeceleration = 12f;
        [SerializeField, Min(0f)] private float postDiveSlideStopSpeed = 0.35f;
        [Tooltip("Tolerancia breve para ignorar falsos negativos de suelo durante ProneSlide. No aplica fuerza de adherencia.")]
        [SerializeField, Min(0f)] private float postDiveSlideGroundedGraceTime = 0.08f;
        [Tooltip("Tiempo que se reintenta Dive -> Prone antes de recuperar una postura estrecha segura.")]
        [SerializeField, Min(0f)] private float diveBlockedRecoveryDelay = 0.15f;
        [Tooltip("Distancia corta usada por el collider de Dive para reconocer apoyo o contacto frontal real.")]
        [SerializeField, Min(0.005f)] private float diveContactProbeDistance = 0.04f;

        // --------------------
        // PARRY / MOVE LOCK
        // --------------------
        [Header("Parry / Move Lock")]
        [SerializeField, Range(5f, 200f)] private float parryMoveStopDecel = 90f;
        private float parryMoveLockTimer;

        public bool IsMoveLockedByParry => parryMoveLockTimer > 0f;

        /// <summary>Bloquea SOLO el movimiento (input X) durante seconds. Ideal para Parry.</summary>
        public void LockMovement(float seconds)
        {
            parryMoveLockTimer = Mathf.Max(parryMoveLockTimer, Mathf.Max(0f, seconds));
            rb.velocity = new Vector2(0f, rb.velocity.y);
        }

        [Header("Carry")]
        [SerializeField, Range(0.1f, 1f)] private float carryingMoveSpeedMultiplier = 0.65f;
        [SerializeField, Range(0.1f, 1f)] private float carryingJumpForceMultiplier = 0.5f;
        [SerializeField, Range(0f, 0.5f)] private float interactLockSecondsAfterPickup = 0.15f;
        [SerializeField, Range(0f, 0.5f)] private float interactLockSecondsAfterDrop = 0.25f;
        private BarrelCarryable carriedObject;
        private float interactLockTimer;
        private bool wasCrouching;
        private bool wasLayDown;

        [Header("Swing")]
        [SerializeField, Min(0f)] private float swingMotorForce = 38f;
        [SerializeField, Range(0f, 1f)] private float swingTangentialAssistMultiplier = 0.2f;
        [SerializeField, Range(1f, 2f)] private float swingCounterInputMultiplier = 1.15f;
        [SerializeField, Min(0f)] private float swingRestSpeedThreshold = 0.35f;
        [SerializeField, Range(0f, 0.95f)] private float swingAssistFadeStartRatio = 0.35f;
        [SerializeField, Range(0.05f, 1f)] private float swingAssistStopRatio = 0.75f;
        [SerializeField, Min(0f)] private float swingIdleTangentialDamping = 1.25f;
        [SerializeField, Range(0f, 10f)] private float swingIdleSettleAngle = 1.5f;
        [SerializeField, Min(0f)] private float swingIdleSettleSpeed = 0.15f;
        [SerializeField, Min(0f)] private float swingJumpForceX = 8f;
        [SerializeField, Min(0f)] private float swingJumpForceY = 12f;
        [SerializeField, Range(0f, 0.5f)] private float swingExitCooldown = 0.12f;
        [SerializeField, Range(0f, 0.5f)] private float swingGrabCoyoteTime = 0.10f;
        [SerializeField, Range(0.1f, 1f)] private float swingDownThreshold = 0.5f;
        [SerializeField, Range(0f, 1f)] private float swingVelocityKeepMultiplier = 0.85f;
        [SerializeField, Range(-89f, 0f)] private float swingMinAngle = -75f;
        [SerializeField, Range(0f, 89f)] private float swingMaxAngle = 75f;
        [SerializeField, Min(0.01f)] private float swingMinBelowAnchor = 0.05f;

        [Header("Swing - Rope Jump Assist")]
        [SerializeField, Range(0f, 0.95f)] private float swingTopExitAssistStartRatio = 0.65f;
        [SerializeField, Min(0f)] private float swingTopExitTargetHorizontalSpeed = 12f;
        [SerializeField, Min(0f)] private float swingTopExitMaxAdditionalSpeed = 5f;

        [Header("Swing - HandPivot")]
        [SerializeField] private Transform swingHandAnchor;
        [SerializeField, Min(0f)] private float handPivotMotorForce = 38f;
        [SerializeField, Range(0f, 1f)] private float handPivotTangentialAssistMultiplier = 0.2f;
        [SerializeField, Range(1f, 2f)] private float handPivotCounterInputMultiplier = 1.15f;
        [SerializeField, Min(0f)] private float handPivotRestSpeedThreshold = 0.35f;
        [SerializeField, Range(0f, 0.95f)] private float handPivotAssistFadeStartRatio = 0.35f;
        [SerializeField, Range(0.05f, 1f)] private float handPivotAssistStopRatio = 0.75f;
        [SerializeField, Min(0f)] private float handPivotIdleTangentialDamping = 1.25f;
        [SerializeField, Min(0f)] private float handPivotJumpForceX = 8f;
        [SerializeField, Min(0f)] private float handPivotJumpForceY = 12f;
        [SerializeField, Range(0f, 1f)] private float handPivotVelocityKeepMultiplier = 0.85f;
        [SerializeField, Range(-179f, 0f)] private float handPivotMinAngle = -75f;
        [SerializeField, Range(0f, 179f)] private float handPivotMaxAngle = 75f;
        [SerializeField, Min(1f)] private float handPivotUprightDegreesPerSecond = 540f;
        [SerializeField, Range(0.1f, 10f)] private float handPivotUprightTolerance = 1f;
        private DistanceJoint2D swingJoint;
        private HingeJoint2D handPivotJoint;
        private SwingPole currentSwingPole;
        private SwingAttachmentMode activeSwingMode = SwingAttachmentMode.Rope;
        private float activeRopeMinDistance = 0.05f;
        private float activeRopeMaxDistance = 0.05f;
        private float swingCooldownTimer;
        private int swingGrabTemporalVersion;
        private bool isSwinging;
        private bool isHandPivotRecovering;
        private RigidbodyType2D handPivotCachedBodyType;
        private RigidbodyConstraints2D handPivotCachedConstraints;
        private float handPivotUprightRotation;
        private int handPivotExitFacing;

        [Header("Overhead Rail Stealth")]
        [SerializeField, Range(0f, 1f)] private float overheadRailStealthEnterThreshold = 0.55f;
        [SerializeField, Range(0f, 1f)] private float overheadRailStealthExitThreshold = 0.25f;
        [SerializeField, Range(0.1f, 1f)] private float overheadRailStealthMoveMultiplier = 0.5f;

        private SliderJoint2D overheadRailJoint;
        private OverheadHangRail currentOverheadRail;
        private bool isOverheadRailHanging;
        private bool isOverheadRailStealthHanging;
        private float overheadRailAlongSpeed;
        private RigidbodyType2D overheadRailCachedBodyType;
        private RigidbodyConstraints2D overheadRailCachedConstraints;
        private float overheadRailCachedGravityScale;

        private FlagSpringPole currentFlagSpringPole;
        private bool isFlagSpringPoleAttached;
        private Vector2 flagSpringPoleHandOffset;
        private RigidbodyType2D flagSpringPoleCachedBodyType;
        private RigidbodyConstraints2D flagSpringPoleCachedConstraints;
        private float flagSpringPoleCachedGravityScale;

        // --------------------
        // RUNTIME STATE
        // --------------------
        private float attackCooldownTimer;

        private Rigidbody2D rb;
        private CapsuleCollider2D capsule;
        private PlayerAnimatorDriver animDriver;
        private PlayerHealth playerHealth;
        private PlayerStatsRuntime progressionStats;
        private ProgressionService progressionService;
        private bool doubleJumpEquipped;
        private bool doubleJumpAvailable;

        [SerializeField] private PlayerStealthKill stealthKill;

        [Header("Jump Anticipation")]
        [SerializeField, Min(0f)] private float jumpStartDelay = 0f;
        [SerializeField, Range(0f, 1f)] private float jumpAnticipationMoveSpeedMultiplier = 0.2f;

        private bool isGrounded;
        private Vector2 groundNormal = Vector2.up;
        private bool slopeMovementSuppressedUntilAirborne;
        private bool wasUsingSlopeMovementLastMove;
        private float slopeHorizontalSpeed;
        private bool isTouchingWall;
        private bool isWallSliding;
        private bool isWallLosingContact;
        private RaycastHit2D wallSlideVisualHit;
        private Collider2D wallSlideVisualCollider;
        private Collider2D currentGroundCollider;
        private HorizontalMovingPlatform currentGroundPlatform;
        private PlayerStandableSurface currentGroundStandableSurface;
        private PlayerStandableSurface idleAnchorStandableSurface;
        private PlayerStandableSurface ignoredStandableSurface;
        private float idleStandableOffsetX;
        private float ignoredStandableTimer;
        private float ignoredStandableMinimumTimer;
        private bool ignoredStandableWaitForHorizontalSeparation;
        private ConveyorBelt currentGroundConveyor;
        private WoodWorkpiece currentGroundWoodWorkpiece;
        private float appliedConveyorVelocityX;
        private bool suppressConveyorUntilAirborne;
        private bool horizontalAirCurrentInfluence;
        private float horizontalAirCurrentDirectionX;
        private float horizontalAirCurrentAgainstFlowControl = 1f;
        private bool horizontalAirCurrentMomentumActive;
        private float horizontalAirCurrentMomentumDirectionX;
        private int horizontalAirCurrentMomentumGraceFixedSteps;
        private bool upwardAirCurrentMomentumProtected;
        private float slipperyTimer;
        private float slipperyGroundAccelerationMultiplier = 1f;
        private float slipperyGroundDecelerationMultiplier = 1f;
        private float slipperyTurnResponsivenessMultiplier = 1f;
        private PlayerSlipperyVisual slipperyVisual;
        private int wallSlideVisualSide;
        private bool wallSlideVisualHasEdge;
        private float wallSlideVisualEdgeY;
        private float wallSlideVisualVisibleFraction = 1f;

        private float coyoteTimer;
        private float jumpBufferTimer;
        private bool hasPendingNormalJump;
        private float pendingNormalJumpTimer;
        private float normalJumpAnticipationAge;

        private bool isDashing;
        private float dashTimer;
        private float dashCooldownTimer;
        private int airDashesRemaining;
        private bool dashStartedGrounded;
        private int dashHorizontalSign;
        private bool dashFollowingSlope;
        private float sprintRemaining;
        private float sprintRegenDelayTimer;
        private bool sprintActive;
        private bool sprintExhaustedUntilRelease;
        private bool sprintWasGrounded;
        private float sprintMomentumTimer;
        private float sprintLandingMomentumTimer;
        private float sprintMomentumSpeed;
        private int sprintMomentumDirection;
        private SprintSlideDiveState sprintSlideDiveState;
        private float slideElapsed;
        private float slideCurrentSpeed;
        private float diveElapsed;
        private int sprintSlideDiveDirection;
        private bool diveHasLeftGround;
        private float diveProneRejectedSince = -1f;
        private bool diveFrontContactWasActive;
        private float lastDiveAirborneVelocityX;
        private float preservedDiveAirborneVelocityX;
        private bool hasDiveAirborneVelocitySample;
        private float postDiveSlideCurrentSpeed;
        private float postDiveSlideEntryVelocityX;
        private float postDiveSlideGroundedGraceTimer;
        private float postDiveSlideDebugLogTimer;
        private float slideManeuverStartX;
        private float slideManeuverSprintEntryVelocityX;
        private float slideManeuverInitialSpeed;
        private float forwardDiveStartX;
        private float forwardDiveTakeoffX;
        private bool hasForwardDiveTakeoffSample;
        private float postDiveSlideLandingX;
        private float combinedSlideToTakeoffDistance;
        private float combinedDiveAirDistance;
        private bool isUsingDiveColliderRecoveryProfile;
        private string lastPostureClearanceFailureReason = "none";

        private float wallJumpLockTimer;
        private float defaultGravityScale;

        private readonly Collider2D[] attackHits = new Collider2D[16];
        private readonly RaycastHit2D[] groundHits = new RaycastHit2D[8];
        private readonly RaycastHit2D[] wallHits = new RaycastHit2D[8];
        private readonly RaycastHit2D[] wallEdgeHits = new RaycastHit2D[8];
        private readonly Collider2D[] cornerCorrectionHits = new Collider2D[8];
        private readonly Collider2D[] postureClearanceHits = new Collider2D[16];
        private readonly HashSet<UnityEngine.Object> attackDamageTargets = new HashSet<UnityEngine.Object>();
        private readonly List<IInteractable> interactablesInRange = new List<IInteractable>();
        private bool lastAttackHitEnemy;
        private bool lastAttackAllowsDownBounce;
        private bool lastAttackUsesDirectionalDownHitBounce;
        private bool lastAttackUsesTankDownHitBounce;
        private int lastDownHitPlayerSeparationDirection;
        private bool pendingDownHitBounce;
        private bool pendingDownHitBounceOverridesHorizontal;
        private float pendingDownHitBounceHorizontal;
        private float pendingDownHitBounceVertical;
        private int lastQueuedDownHitBounceAttackId;
        private bool downHitBounceAscentProtected;
        private int attackSequenceId;

        private int facingDir = 1;
        private float attackPointLocalXAbs;
        private float wallCheckLocalXAbs;

        private Vector2 lockedAttackDir = Vector2.right;
        private int lockedAttackDirType = 0;

        private bool isAttacking;
        private bool isSustainedAirDownAttack;
        private int sustainedDownAttackStartActionPressVersion;
        private bool canCancelAttack;
        private float attackStateTimer;
        private float cancelEnableTimer;

        private int wallSide;
        private bool hasNarrowWallContact;
        private int wallStickSide;
        private int wallCoyoteSide;
        private float wallStickTimer;
        private float wallCoyoteTimer;
        private float wallJumpRegrabTimer;
        private float wallDebugLogTimer;
        private float wallHitDebugLogTimer;
        private float wallSlideBlockDebugLogTimer;
        private float wallSlideExitFastFallTimer;
        private float recentlyGroundedTimer;
        private float cornerStuckTimer;
        private float cornerStuckDebugLogTimer;
        private bool suppressWallSlideThisFixed;

        // HANG
        private bool isHanging;
        private float ledgeGrabCooldownTimer;
        private float ledgeCandidateTimer;
        private Vector2 hangPositionLocked;
        private Vector2 climbPositionLocked;
        private bool hasClimbPositionLocked;
        private bool isLedgeClimbing;
        private Coroutine ledgeClimbRoutine;
        private Vector2 ledgeCandidateHangPos;
        private Vector2 ledgeCandidateCorner;
        private int ledgeCandidateSide;
        private LedgeGrabPoint activeLedgePoint;
        private Vector2 ledgeDebugWallStart;
        private Vector2 ledgeDebugWallEnd;
        private Vector2 ledgeDebugHeadStart;
        private Vector2 ledgeDebugHeadEnd;
        private Vector2 ledgeDebugTopStart;
        private Vector2 ledgeDebugTopEnd;
        private Vector2 ledgeDebugCorner;
        private Vector2 ledgeDebugHangPos;
        private bool ledgeDebugHasProbe;
        private bool ledgeDebugHasCorner;
        private bool ledgeDebugHasHangPos;
        private string ledgeDebugFailReason = "";
        private string lastLoggedLedgeFailReason = "";
        private float ledgeDebugLogTimer;
        private Collider2D lastGroundDebugCollider;
        private Collider2D lastTopGroundDebugCollider;
        private int lastWallJumpSide;
        private float lastWallJumpY;
        private Collider2D lastWallJumpCollider;
        private Collider2D sameWallClimbCollider;
        private int sameWallClimbSide;
        private float sameWallClimbCeilingY;

        private RigidbodyType2D cachedBodyType;
        private RigidbodyConstraints2D cachedConstraints;
        private float cachedGravityScale;

        private bool suppressWallWhileDownHeld;
        private bool externalControlLocked;

        public bool FacingLeft => facingDir < 0;
        public bool IsGrounded => isGrounded;
        public Collider2D CurrentGroundCollider => currentGroundCollider;
        public bool IsGroundedOnMovingSupport =>
            currentGroundPlatform != null ||
            currentGroundStandableSurface != null ||
            currentGroundWoodWorkpiece != null;
        public bool IsAttacking => isAttacking;
        public bool IsDashing => isDashing;
        public bool IsExternalControlLocked => externalControlLocked;
        public bool IsHanging => isHanging || isFlagSpringPoleAttached;
        public bool IsWallSliding => isWallSliding;
        public bool IsWallLosingContact => isWallSliding && isWallLosingContact;
        public int WallSide => wallSide;
        public bool IsTouchingWall => isTouchingWall;
        public bool IsPushingIntoWall => IsInputPushingIntoWall(wallSide);
        public bool IsCarrying => carriedObject != null;
        public bool IsSwinging => isSwinging || isHandPivotRecovering;
        public SwingAttachmentMode ActiveSwingMode => activeSwingMode;
        public float SwingGrabCoyoteTime => Mathf.Max(0f, swingGrabCoyoteTime);
        public int SwingGrabTemporalVersion => swingGrabTemporalVersion;
        public SprintSlideDiveState SlideDiveState => sprintSlideDiveState;
        public bool IsSliding => sprintSlideDiveState == SprintSlideDiveState.Slide;
        public bool IsDiving => sprintSlideDiveState == SprintSlideDiveState.Dive;
        public bool IsPostDiveSliding => sprintSlideDiveState == SprintSlideDiveState.ProneSlide;
        public bool IsSlippery => slipperyTimer > 0f;
        public bool IsOverheadRailHanging => isOverheadRailHanging;
        public bool IsOverheadRailStealthHanging => isOverheadRailStealthHanging;
        public bool IsFlagSpringPoleAttached => isFlagSpringPoleAttached;
        public bool IsNormalJumpAnticipating => hasPendingNormalJump;
        public bool CanUseUpdraftCapeMotion =>
            isActiveAndEnabled &&
            rb != null &&
            rb.bodyType == RigidbodyType2D.Dynamic &&
            !externalControlLocked &&
            !isGrounded &&
            !isDashing &&
            !isWallSliding &&
            wallJumpLockTimer <= 0f &&
            !hasPendingNormalJump &&
            !isHanging &&
            !isLedgeClimbing &&
            !IsSwinging &&
            !isOverheadRailHanging &&
            !isFlagSpringPoleAttached &&
            !IsSliding &&
            !IsDiving &&
            !IsPostDiveSliding &&
            !IsMoveLockedByParry &&
            (swingJoint == null || !swingJoint.enabled) &&
            (handPivotJoint == null || !handPivotJoint.enabled) &&
            (overheadRailJoint == null || !overheadRailJoint.enabled);
        public BarrelCarryable CarriedObject => carriedObject;
        public Vector2 Velocity => rb != null ? rb.velocity : Vector2.zero;
        public Vector2 SwingHandPosition => swingHandAnchor != null
            ? swingHandAnchor.position
            : transform.position;

        public event Action NormalJumpPerformed;
        public event Action<int> WallJumped;

        // Para Animator (si lo usáis)
        public bool IsCrouching => input != null && input.IsCrouching;
        public bool IsLayDown => input != null && input.IsLayDown;
        public bool IsStealthWalking =>
            !IsCrouching &&
            !IsLayDown &&
            IsStealthWalkRequested();
        public bool IsStealthModeActive =>
            IsCrouching ||
            IsLayDown ||
            IsStealthWalking ||
            IsOverheadRailStealthHanging;
        public bool IsSilentForEnemyHearing => IsStealthModeActive;

        internal void SetHorizontalAirCurrentInfluence(
            bool active,
            float flowDirectionX,
            float againstFlowControl,
            bool preserveMomentumOnRelease)
        {
            bool wasActive = horizontalAirCurrentInfluence;
            bool nextActive = active && Mathf.Abs(flowDirectionX) > 0.5f;
            if (wasActive && !nextActive)
            {
                if (preserveMomentumOnRelease)
                    CaptureHorizontalAirCurrentMomentum();
                else
                    ClearHorizontalAirCurrentMomentum();
            }
            else if (nextActive)
            {
                ClearHorizontalAirCurrentMomentum();
            }

            horizontalAirCurrentInfluence = nextActive;
            horizontalAirCurrentDirectionX = horizontalAirCurrentInfluence
                ? Mathf.Sign(flowDirectionX)
                : 0f;
            horizontalAirCurrentAgainstFlowControl = horizontalAirCurrentInfluence
                ? Mathf.Clamp01(againstFlowControl)
                : 1f;
        }

        internal void ProtectUpwardAirCurrentMomentum()
        {
            upwardAirCurrentMomentumProtected = true;
        }

        public void FaceDirection(int dir)
        {
            if (IsSliding || IsDiving) return;
            if (activeSwingMode == SwingAttachmentMode.HandPivot && IsSwinging) return;
            ApplyFacing(dir < 0 ? -1 : 1);
        }

        public void SetExternalControlLocked(bool locked)
        {
            if (locked && isFlagSpringPoleAttached)
                ForceEndFlagSpringPoleForReset();

            if (locked && isOverheadRailHanging)
                ExitOverheadRail(false);

            if (locked)
            {
                if (isAttacking)
                    CancelAttack("external control");
            }

            externalControlLocked = locked;
        }

        public void RefillSprintReserve()
        {
            sprintRemaining = Mathf.Max(0f, sprintMaxDuration);
            sprintRegenDelayTimer = 0f;
            sprintExhaustedUntilRelease = false;
            sprintActive = false;
        }

        public void ApplySlipperyEffect(
            float duration,
            float accelerationMultiplier,
            float decelerationMultiplier,
            float turnMultiplier)
        {
            float safeDuration = Mathf.Max(0f, duration);
            if (safeDuration <= 0f)
                return;

            slipperyTimer = safeDuration;
            slipperyGroundAccelerationMultiplier = Mathf.Max(0f, accelerationMultiplier);
            slipperyGroundDecelerationMultiplier = Mathf.Max(0f, decelerationMultiplier);
            slipperyTurnResponsivenessMultiplier = Mathf.Max(0f, turnMultiplier);

            if (slipperyVisual == null)
                slipperyVisual = GetComponent<PlayerSlipperyVisual>();
            if (slipperyVisual == null)
                slipperyVisual = gameObject.AddComponent<PlayerSlipperyVisual>();

            slipperyVisual.SetVisible(true);
        }

        private void UpdateSlipperyEffect(float deltaTime)
        {
            if (!IsSlippery)
                return;

            slipperyTimer = Mathf.Max(0f, slipperyTimer - Mathf.Max(0f, deltaTime));
            if (slipperyTimer <= 0f)
                ClearSlipperyEffect();
        }

        private void ClearSlipperyEffect()
        {
            slipperyTimer = 0f;
            slipperyGroundAccelerationMultiplier = 1f;
            slipperyGroundDecelerationMultiplier = 1f;
            slipperyTurnResponsivenessMultiplier = 1f;
            slipperyVisual?.SetVisible(false);
        }

        public void ResetTransientState()
        {
            InvalidateSwingGrabTemporalState();
            ClearSlipperyEffect();
            ForceEndFlagSpringPoleForReset();
            ForceEndOverheadRailForReset();

            attackCooldownTimer = 0f;
            coyoteTimer = 0f;
            jumpBufferTimer = 0f;
            dashTimer = 0f;
            dashCooldownTimer = 0f;
            CancelSprintSlideDiveForReset();
            ResetSprintState(true);
            wallJumpLockTimer = 0f;
            wallStickTimer = 0f;
            wallCoyoteTimer = 0f;
            wallStickSide = 0;
            wallCoyoteSide = 0;
            ledgeGrabCooldownTimer = 0f;
            ledgeCandidateTimer = 0f;
            ClearSameWallClimbBlock();
            parryMoveLockTimer = 0f;
            attackStateTimer = 0f;
            cancelEnableTimer = 0f;

            isDashing = false;
            isWallSliding = false;
            isAttacking = false;
            isSustainedAirDownAttack = false;
            sustainedDownAttackStartActionPressVersion = 0;
            canCancelAttack = false;
            suppressWallWhileDownHeld = false;
            slopeMovementSuppressedUntilAirborne = false;
            groundNormal = Vector2.up;
            currentGroundCollider = null;
            currentGroundPlatform = null;
            currentGroundStandableSurface = null;
            idleAnchorStandableSurface = null;
            ignoredStandableSurface = null;
            idleStandableOffsetX = 0f;
            ignoredStandableTimer = 0f;
            ignoredStandableMinimumTimer = 0f;
            ignoredStandableWaitForHorizontalSeparation = false;
            pendingDownHitBounce = false;
            pendingDownHitBounceOverridesHorizontal = false;
            pendingDownHitBounceHorizontal = 0f;
            pendingDownHitBounceVertical = 0f;
            lastQueuedDownHitBounceAttackId = 0;
            downHitBounceAscentProtected = false;
            wasUsingSlopeMovementLastMove = false;
            slopeHorizontalSpeed = 0f;
            currentGroundConveyor = null;
            currentGroundWoodWorkpiece = null;
            appliedConveyorVelocityX = 0f;
            suppressConveyorUntilAirborne = false;
            horizontalAirCurrentInfluence = false;
            horizontalAirCurrentDirectionX = 0f;
            horizontalAirCurrentAgainstFlowControl = 1f;
            ClearHorizontalAirCurrentMomentum();
            upwardAirCurrentMomentumProtected = false;

            if (carriedObject != null)
                carriedObject.Drop();

            if (IsSwinging)
                ForceEndSwingForReset();

            if (isHanging)
            {
                isHanging = false;
                isLedgeClimbing = false;
                hasClimbPositionLocked = false;
                rb.bodyType = cachedBodyType;
                rb.constraints = cachedConstraints;
            }

            if (ledgeClimbRoutine != null)
            {
                StopCoroutine(ledgeClimbRoutine);
                ledgeClimbRoutine = null;
            }

            if (capsule != null)
                capsule.enabled = true;

            rb.gravityScale = defaultGravityScale;
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;

            ForceStandingPosture("PlayerController.ResetTransientState");
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            capsule = GetComponent<CapsuleCollider2D>();
            animDriver = GetComponent<PlayerAnimatorDriver>();
            playerHealth = GetComponent<PlayerHealth>();

            if (input == null) input = GetComponent<InputReader>();
            if (stamina == null) stamina = GetComponent<PlayerStamina>();
            if (visualRoot == null) visualRoot = transform;

            defaultGravityScale = rb.gravityScale;

            if (stats != null)
            {
                rb.gravityScale = stats.gravityScale;
                defaultGravityScale = rb.gravityScale;
                airDashesRemaining = stats.airDashesMax;
            }

            ResetSprintState(true);

            if (attackPoint != null)
                attackPointLocalXAbs = Mathf.Abs(attackPoint.localPosition.x);

            if (wallCheck != null)
                wallCheckLocalXAbs = Mathf.Abs(wallCheck.localPosition.x);

            swingJoint = GetComponent<DistanceJoint2D>();
            if (swingJoint == null)
                swingJoint = gameObject.AddComponent<DistanceJoint2D>();

            swingJoint.enabled = false;
            swingJoint.autoConfigureDistance = false;
            swingJoint.autoConfigureConnectedAnchor = false;
            swingJoint.maxDistanceOnly = false;

            handPivotJoint = GetComponent<HingeJoint2D>();
            if (handPivotJoint == null)
                handPivotJoint = gameObject.AddComponent<HingeJoint2D>();

            handPivotJoint.enabled = false;
            handPivotJoint.autoConfigureConnectedAnchor = false;
            handPivotJoint.useMotor = false;

            overheadRailJoint = GetComponent<SliderJoint2D>();
            if (overheadRailJoint == null)
                overheadRailJoint = gameObject.AddComponent<SliderJoint2D>();

            overheadRailJoint.enabled = false;
            overheadRailJoint.autoConfigureAngle = false;
            overheadRailJoint.autoConfigureConnectedAnchor = false;
            overheadRailJoint.useMotor = false;
            overheadRailJoint.useLimits = true;

            if (ledgeTopLayer.value == 0)
                ledgeTopLayer = groundLayer | wallLayer;

            ApplyFacing(1);

            if (stealthKill == null)
                stealthKill = GetComponent<PlayerStealthKill>();
        }

        private void OnEnable()
        {
            if (playerHealth == null)
                playerHealth = GetComponent<PlayerHealth>();
            if (playerHealth != null)
                playerHealth.OnDamaged += HandlePlayerDamaged;

            SubscribeToProgression();
        }

        private void OnDisable()
        {
            horizontalAirCurrentInfluence = false;
            horizontalAirCurrentDirectionX = 0f;
            horizontalAirCurrentAgainstFlowControl = 1f;
            ClearHorizontalAirCurrentMomentum();
            upwardAirCurrentMomentumProtected = false;
            pendingDownHitBounce = false;
            downHitBounceAscentProtected = false;
            ForceEndFlagSpringPoleForReset();

            if (isAttacking)
                CancelAttack("controller disabled");

            if (playerHealth != null)
                playerHealth.OnDamaged -= HandlePlayerDamaged;

            UnsubscribeFromProgression();

            if (isOverheadRailHanging)
                ForceEndOverheadRailForReset();
        }

        private void SubscribeToProgression()
        {
            progressionService = ProgressionService.Instance;
            progressionService.OnProgressionChanged -= RefreshEquippedAbilities;
            progressionService.OnProgressionChanged += RefreshEquippedAbilities;
            RefreshEquippedAbilities();
        }

        private void UnsubscribeFromProgression()
        {
            if (progressionService != null)
                progressionService.OnProgressionChanged -= RefreshEquippedAbilities;
        }

        private void RefreshEquippedAbilities()
        {
            bool wasDoubleJumpEquipped = doubleJumpEquipped;
            doubleJumpEquipped = progressionService != null &&
                progressionService.IsAbilityEquipped(ProgressionAbilityIds.DoubleJump);

            if (!doubleJumpEquipped)
                doubleJumpAvailable = false;
            else if (!wasDoubleJumpEquipped && isGrounded)
                doubleJumpAvailable = true;
        }

        private bool TryApplyPostureCommand(
            InputReader.CrouchCommand command,
            string source)
        {
            if (!command.IsValid || input == null || capsule == null) return false;
            if (!CanAdoptPosture(command.TargetPosture, source)) return false;

            // Physical shape first; the logical/animated posture is committed only after it fits.
            ApplyPostureColliderProfile(command.TargetPosture);
            input.CommitPostureTransition(command, source);
            return true;
        }

        private bool TryApplyPosture(InputReader.PlayerPosture targetPosture, string source)
        {
            if (input == null || capsule == null) return false;
            if (!CanAdoptPosture(targetPosture, source)) return false;

            ApplyPostureColliderProfile(targetPosture);
            input.CommitPosture(targetPosture, source);
            return true;
        }

        private bool TryStandingPostureAndCancelGesture(string source)
        {
            if (input == null) return false;

            input.CancelCrouchGesture(source);
            return TryApplyPosture(InputReader.PlayerPosture.Standing, source);
        }

        private void ForceStandingPosture(string source)
        {
            if (capsule == null || input == null) return;

            input.CancelCrouchGesture(source);
            ApplyPostureColliderProfile(InputReader.PlayerPosture.Standing);
            input.CommitPosture(InputReader.PlayerPosture.Standing, source);
        }

        private bool CanAdoptPosture(
            InputReader.PlayerPosture targetPosture,
            string source,
            bool logBlocked = true)
        {
            PostureColliderProfile targetProfile = GetPostureColliderProfile(targetPosture);
            if (!IsPostureColliderProfileValid(targetProfile))
            {
                if (debugLogs && logBlocked)
                    Debug.LogWarning($"[POSTURE] BLOCKED source={source} target={targetPosture} reason=invalid-profile");
                return false;
            }

            InputReader.PlayerPosture currentPosture = input.CurrentPosture;
            if (currentPosture == targetPosture && !isUsingDiveColliderRecoveryProfile)
                return true;

            PostureColliderProfile currentProfile = isUsingDiveColliderRecoveryProfile
                ? diveColliderProfile
                : GetPostureColliderProfile(currentPosture);
            if (!RequiresPostureClearance(currentProfile, targetProfile)) return true;

            return HasPostureClearance(targetPosture, targetProfile, source, logBlocked);
        }

        private bool HasPostureClearance(
            InputReader.PlayerPosture targetPosture,
            PostureColliderProfile targetProfile,
            string source,
            bool logBlocked,
            Vector2 bodyPositionOffset = default)
        {
            lastPostureClearanceFailureReason = "none";

            Vector3 lossyScale = transform.lossyScale;
            Vector2 querySize = new Vector2(
                targetProfile.size.x * Mathf.Abs(lossyScale.x),
                targetProfile.size.y * Mathf.Abs(lossyScale.y)
            );

            float clearance = Mathf.Max(0f, postureClearanceSkin);
            float floorContactTolerance = Mathf.Max(0.001f, clearance);
            // Exclude only the supporting floor band, while requiring the same margin above
            // and on both sides. This query never moves the Player or its Rigidbody2D.
            querySize.x += clearance * 2f;
            querySize.y = Mathf.Max(
                0.01f,
                querySize.y - floorContactTolerance + clearance
            );

            Vector2 queryCenter =
                (Vector2)transform.TransformPoint(targetProfile.offset) + bodyPositionOffset;
            queryCenter += Vector2.up * ((floorContactTolerance + clearance) * 0.5f);

            ContactFilter2D filter = new ContactFilter2D();
            filter.SetLayerMask(SolidMask());
            filter.useLayerMask = true;
            filter.useTriggers = false;

            int hitCount = Physics2D.OverlapCapsule(
                queryCenter,
                querySize,
                targetProfile.direction,
                transform.eulerAngles.z,
                filter,
                postureClearanceHits
            );

            if (hitCount >= postureClearanceHits.Length)
            {
                lastPostureClearanceFailureReason = $"clearance-buffer-full count={hitCount}";
                if (debugLogs && logBlocked)
                    Debug.LogWarning(
                        $"[POSTURE] BLOCKED source={source} target={targetPosture} " +
                        $"reason={lastPostureClearanceFailureReason}");
                return false;
            }

            for (int i = 0; i < hitCount; i++)
            {
                Collider2D hit = postureClearanceHits[i];
                if (hit == null || hit == capsule || hit.isTrigger) continue;
                if (hit.attachedRigidbody == rb) continue;
                if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;

                lastPostureClearanceFailureReason =
                    $"solid-overlap collider={DescribeCollider2D(hit)}";
                if (debugLogs && logBlocked)
                    Debug.LogWarning(
                        $"[POSTURE] BLOCKED source={source} target={targetPosture} " +
                        $"reason={lastPostureClearanceFailureReason}");
                return false;
            }

            return true;
        }

        private void ApplyPostureColliderProfile(InputReader.PlayerPosture posture)
        {
            PostureColliderProfile profile = GetPostureColliderProfile(posture);
            ApplyColliderProfile(profile);
            isUsingDiveColliderRecoveryProfile = false;
        }

        private void ApplyColliderProfile(PostureColliderProfile profile)
        {
            capsule.direction = profile.direction;
            capsule.size = profile.size;
            capsule.offset = profile.offset;
        }

        private bool CanChangeColliderProfile(
            PostureColliderProfile currentProfile,
            PostureColliderProfile targetProfile,
            InputReader.PlayerPosture clearancePosture,
            string source,
            bool logBlocked = true)
        {
            if (!IsPostureColliderProfileValid(targetProfile))
            {
                lastPostureClearanceFailureReason = "invalid-special-profile";
                if (debugLogs && logBlocked)
                    Debug.LogWarning($"[POSTURE] BLOCKED source={source} reason=invalid-special-profile");
                return false;
            }

            if (!RequiresPostureClearance(currentProfile, targetProfile))
            {
                lastPostureClearanceFailureReason = "none";
                return true;
            }

            return HasPostureClearance(
                clearancePosture,
                targetProfile,
                source,
                logBlocked);
        }

        private bool TryApplyPostureFromProfile(
            PostureColliderProfile currentProfile,
            InputReader.PlayerPosture targetPosture,
            string source,
            bool logBlocked = true)
        {
            PostureColliderProfile targetProfile = GetPostureColliderProfile(targetPosture);
            if (!CanChangeColliderProfile(
                    currentProfile,
                    targetProfile,
                    targetPosture,
                    source,
                    logBlocked))
                return false;

            ApplyColliderProfile(targetProfile);
            input.CommitPosture(targetPosture, source);
            return true;
        }

        private bool TryApplyPostureFromProfilePreservingBottom(
            PostureColliderProfile currentProfile,
            InputReader.PlayerPosture targetPosture,
            string source,
            bool logBlocked = true)
        {
            PostureColliderProfile targetProfile = GetPostureColliderProfile(targetPosture);
            if (!IsPostureColliderProfileValid(currentProfile) ||
                !IsPostureColliderProfileValid(targetProfile))
            {
                lastPostureClearanceFailureReason = "invalid-bottom-preserving-profile";
                return false;
            }

            float currentBottomY = transform.TransformPoint(
                new Vector2(
                    currentProfile.offset.x,
                    currentProfile.offset.y - currentProfile.size.y * 0.5f)
            ).y;
            float targetBottomY = transform.TransformPoint(
                new Vector2(
                    targetProfile.offset.x,
                    targetProfile.offset.y - targetProfile.size.y * 0.5f)
            ).y;
            float lift = currentBottomY - targetBottomY;
            if (lift <= 0.0001f)
            {
                lastPostureClearanceFailureReason = "bottom-preserving-lift-unavailable";
                return false;
            }

            Vector2 bodyPositionOffset = Vector2.up * lift;
            if (RequiresPostureClearance(currentProfile, targetProfile) &&
                !HasPostureClearance(
                    targetPosture,
                    targetProfile,
                    source,
                    logBlocked,
                    bodyPositionOffset))
                return false;

            Vector2 targetBodyPosition = rb.position + bodyPositionOffset;
            rb.position = targetBodyPosition;
            transform.position = targetBodyPosition;
            ApplyColliderProfile(targetProfile);
            input.CommitPosture(targetPosture, source);
            return true;
        }

        private PostureColliderProfile GetPostureColliderProfile(
            InputReader.PlayerPosture posture)
        {
            switch (posture)
            {
                case InputReader.PlayerPosture.Crouching:
                    return crouchingColliderProfile;
                case InputReader.PlayerPosture.LayDown:
                    return layDownColliderProfile;
                default:
                    return standingColliderProfile;
            }
        }

        private static bool RequiresPostureClearance(
            PostureColliderProfile currentProfile,
            PostureColliderProfile targetProfile)
        {
            if (currentProfile.direction != targetProfile.direction) return true;

            const float epsilon = 0.0001f;
            Vector2 currentMin = currentProfile.offset - currentProfile.size * 0.5f;
            Vector2 currentMax = currentProfile.offset + currentProfile.size * 0.5f;
            Vector2 targetMin = targetProfile.offset - targetProfile.size * 0.5f;
            Vector2 targetMax = targetProfile.offset + targetProfile.size * 0.5f;

            return
                targetMin.x < currentMin.x - epsilon ||
                targetMin.y < currentMin.y - epsilon ||
                targetMax.x > currentMax.x + epsilon ||
                targetMax.y > currentMax.y + epsilon;
        }

        private static bool IsPostureColliderProfileValid(PostureColliderProfile profile)
        {
            if (profile.size.x <= 0f || profile.size.y <= 0f) return false;

            const float epsilon = 0.0001f;
            return profile.direction == CapsuleDirection2D.Vertical
                ? profile.size.y + epsilon >= profile.size.x
                : profile.size.x + epsilon >= profile.size.y;
        }

        private void Update()
        {
            UpdateSlipperyEffect(Time.deltaTime);
            UpdateIgnoredStandableTimer(Time.deltaTime);

            if (stats == null || input == null) return;

            // cooldowns
            ledgeGrabCooldownTimer -= Time.deltaTime;
            ledgeCandidateTimer -= Time.deltaTime;

            if (parryMoveLockTimer > 0f)
                parryMoveLockTimer -= Time.deltaTime;

            if (interactLockTimer > 0f)
                interactLockTimer -= Time.deltaTime;

            if (swingCooldownTimer > 0f)
            {
                swingCooldownTimer -= Time.deltaTime;
                if (swingCooldownTimer > 0f)
                    input.ClearSwingGrabBuffer();
            }

            if (ledgeDebugLogTimer > 0f)
                ledgeDebugLogTimer -= Time.deltaTime;

            if (wallDebugLogTimer > 0f)
                wallDebugLogTimer -= Time.deltaTime;

            if (wallHitDebugLogTimer > 0f)
                wallHitDebugLogTimer -= Time.deltaTime;

            if (wallSlideBlockDebugLogTimer > 0f)
                wallSlideBlockDebugLogTimer -= Time.deltaTime;

            if (wallJumpRegrabTimer > 0f)
                wallJumpRegrabTimer -= Time.deltaTime;

            if (cornerStuckDebugLogTimer > 0f)
                cornerStuckDebugLogTimer -= Time.deltaTime;

            if (externalControlLocked)
            {
                // Keep action cooldowns progressing while an external interaction owns
                // movement and physics, but do not process gameplay state or input.
                dashCooldownTimer -= Time.deltaTime;
                attackCooldownTimer -= Time.deltaTime;
                return;
            }

            if (isFlagSpringPoleAttached)
            {
                UpdateFlagSpringPoleTraversal();
                return;
            }

            if (isOverheadRailHanging)
            {
                UpdateOverheadRailHanging();
                return;
            }

            // Input limpio (para wall/ledge/hang)
            float rawInputX = Mathf.Clamp(input.MoveInput.x, -1f, 1f);
            float inputX = rawInputX;
            float inputY = Mathf.Clamp(input.MoveInput.y, -1f, 1f);
            if (Mathf.Abs(inputX) < wallInputDeadzone) inputX = 0f;
            if (Mathf.Abs(inputY) < wallInputDeadzone) inputY = 0f;

            // Timers de ataque (cancel window)
            if (isAttacking)
            {
                if (!isSustainedAirDownAttack)
                    attackStateTimer -= Time.deltaTime;
                cancelEnableTimer -= Time.deltaTime;
                canCancelAttack = cancelEnableTimer <= 0f;

                if (!isSustainedAirDownAttack && attackStateTimer <= 0f)
                {
                    isAttacking = false;
                    canCancelAttack = false;
                }
            }

            // Si ya no estás manteniendo DOWN, se re-habilita wall check (tras drop del hang)
            if (suppressWallWhileDownHeld && inputY > -hangDownThreshold)
                suppressWallWhileDownHeld = false;

            // Si está colgado, solo lógica de hang
            if (isHandPivotRecovering)
            {
                UpdateHandPivotRecovery();
                return;
            }

            if (isHanging)
            {
                input.ClearSwingGrabBuffer();
                UpdateHanging(inputX, inputY);
                return;
            }

            if (isSwinging)
            {
                UpdateSwinging(inputX, inputY);
                return;
            }

            if (IsSliding || IsDiving || IsPostDiveSliding)
            {
                CheckGround();
                if (IsPostDiveSliding)
                {
                    InputReader.CrouchCommand ignoredCrouchCommand = input.ConsumeCrouchPress();
                    if (ignoredCrouchCommand.IsValid)
                        input.ClaimCrouchGesture("PlayerController.Update:PostDiveSlide");
                    DiscardInputsDuringSprintSlideDive();
                    return;
                }

                UpdateSprintSlideDive();
                return;
            }

            // Flip por movimiento (si no está lockeado por parry)
            float facingThreshold = isGrounded ? facingInputDeadzone : airborneFacingInputThreshold;
            if (!IsMoveLockedByParry && !isWallSliding && Mathf.Abs(rawInputX) >= facingThreshold)
                ApplyFacing((int)Mathf.Sign(rawInputX));

            CheckGround();
            if (isSustainedAirDownAttack && isGrounded)
                CancelAttack("down attack landed");
            CheckWall(inputY);

            TryCancelSustainedDownAttackFromNewAction();

            InputReader.CrouchCommand crouchCommand = input.ConsumeCrouchPress();
            if (HandleCrouchCommand(crouchCommand))
                return;

            // Timers base
            coyoteTimer = isGrounded ? stats.coyoteTime : (coyoteTimer - Time.deltaTime);
            jumpBufferTimer -= Time.deltaTime;
            dashCooldownTimer -= Time.deltaTime;
            attackCooldownTimer -= Time.deltaTime;

            UpdatePendingNormalJump();

            // Consume inputs
            bool jumpPressed = input.ConsumeJumpPressed();
            bool dashPressed = stats.hasDash && input.ConsumeDashPressed();
            bool attackPressed = input.ConsumeAttackPressed();
            bool interactPressed = input.ConsumeInteractPressed();
            bool dolphinDivePressed = input.ConsumeDolphinDivePressed();

            if (dolphinDivePressed && TryStartDirectSprintDive())
                return;

            // ✅ Jump desde Crouch/LayDown: NO salta; intenta levantarse
            if (jumpPressed && (IsCrouching || IsLayDown))
            {
                TryStandingPostureAndCancelGesture(
                    "PlayerController.Update:JumpWhileLowered");
                jumpPressed = false;
                jumpBufferTimer = 0f;
            }

            // Buffer jump
            if (jumpPressed)
                jumpBufferTimer = stats.jumpBufferTime;

            // Cancel con DASH
            if (dashPressed)
            {
                if (isAttacking && canCancelAttack)
                    CancelAttack("dash");

                DropCarriedObject();
                TryStartDash();
            }

            // Cancel con JUMP
            if (jumpPressed && isAttacking && canCancelAttack)
                CancelAttack("jump");

            // ✅ ATAQUE:
            // Si estás en Crouch/LayDown, primero intentamos stealth kill con el MISMO botón.
            if (IsCarrying && ((!wasCrouching && IsCrouching) || (!wasLayDown && IsLayDown)))
                DropCarriedObject();

            if (attackPressed)
            {
                DropCarriedObject();

                if (stealthKill != null && stealthKill.TryStealthKill())
                {
                    attackCooldownTimer = Mathf.Max(attackCooldownTimer, attackCooldown * 0.25f);
                    return;
                }

                if (!isDashing && !isAttacking && attackCooldownTimer <= 0f)
                    StartAttack();
            }

            if (interactPressed)
            {
                TryInteract();

                // An interaction such as hiding or sitting can disable this controller.
                // Do not process movement actions later in the same Update in that case.
                if (!isActiveAndEnabled || externalControlLocked)
                    return;
            }

            // Jump normal (buffer + coyote)
            if (!hasPendingNormalJump && jumpBufferTimer > 0f && coyoteTimer > 0f)
            {
                StartPendingNormalJump();
                jumpBufferTimer = 0f;
                coyoteTimer = 0f;
            }

            // Wall Jump (buffer)
            if (jumpBufferTimer > 0f && (isWallSliding || wallCoyoteTimer > 0f))
            {
                WallJump();
                jumpBufferTimer = 0f;
            }

            // Aerial ability jump. Ground/coyote and wall jump always get first claim
            // on the same buffered press, so one physical press cannot trigger both.
            if (jumpBufferTimer > 0f && TryUseDoubleJump())
                jumpBufferTimer = 0f;

            if (upwardAirCurrentMomentumProtected &&
                (isGrounded || rb.velocity.y <= 0.01f))
            {
                upwardAirCurrentMomentumProtected = false;
            }

            if (downHitBounceAscentProtected &&
                rb.velocity.y <= 0.01f)
            {
                downHitBounceAscentProtected = false;
            }

            // A PassThrough air current keeps the upward momentum it added until
            // gravity reaches the apex; ordinary jump ascent still uses jump cut.
            if (!upwardAirCurrentMomentumProtected &&
                !downHitBounceAscentProtected &&
                !input.JumpHeld && rb.velocity.y > 0.01f)
            {
                rb.velocity = new Vector2(rb.velocity.x, rb.velocity.y * stats.jumpCutMultiplier);
            }

            // Recargar air dashes
            if (isGrounded)
                airDashesRemaining = stats.airDashesMax;

            // Ledge grab automático por raycasts desactivado.
            // El agarre se controla con LedgeGrabTrigger + SwingGrab.
        }

        private void FixedUpdate()
        {
            if (stats == null || input == null) return;
            if (externalControlLocked) return;

            if (isFlagSpringPoleAttached)
            {
                UpdateSprintTimers(Time.fixedDeltaTime);
                sprintActive = false;
                sprintWasGrounded = isGrounded;
                return;
            }

            if (isOverheadRailHanging)
            {
                UpdateSprintTimers(Time.fixedDeltaTime);
                sprintActive = false;
                sprintWasGrounded = isGrounded;
                FixedUpdateOverheadRailHanging();
                return;
            }

            if (isHanging)
            {
                UpdateSprintTimers(Time.fixedDeltaTime);
                sprintActive = false;
                sprintWasGrounded = isGrounded;
                return;
            }

            if (isHandPivotRecovering)
            {
                UpdateSprintTimers(Time.fixedDeltaTime);
                sprintActive = false;
                sprintWasGrounded = isGrounded;
                FixedUpdateHandPivotRecovery();
                return;
            }

            if (isSwinging)
            {
                UpdateSprintTimers(Time.fixedDeltaTime);
                sprintActive = false;
                sprintWasGrounded = isGrounded;
                FixedUpdateSwinging();
                return;
            }

            if (IsSliding || IsDiving || IsPostDiveSliding)
            {
                CheckGround();
                UpdateSprintTimers(Time.fixedDeltaTime);
                sprintActive = false;
                sprintWasGrounded = isGrounded;
                FixedUpdateSprintSlideDive();
                return;
            }

            if (isDashing)
            {
                UpdateSprintTimers(Time.fixedDeltaTime);
                sprintActive = false;
                sprintWasGrounded = isGrounded;
                UpdateDash();
                return;
            }

            suppressWallSlideThisFixed = false;
            CheckGround();
            UpdateSustainedAirDownAttack();
            TryResolveDiveColliderRecoveryProfile();
            UpdateSprintTimers(Time.fixedDeltaTime);
            UpdateSprintState();
            UpdateRecentlyGroundedTimer(Time.fixedDeltaTime);
            HandleCornerAntiStuck();
            HandleWallSlide();
            UpdateWallSlideEdgeVisual(Time.fixedDeltaTime);
            LogWallDebugState();

            // ✅ Parry move lock: no Move()
            if (IsMoveLockedByParry)
            {
                float controlledVelocityX = HorizontalVelocityWithoutConveyor();
                float newVX = Mathf.MoveTowards(
                    controlledVelocityX,
                    0f,
                    parryMoveStopDecel * Time.fixedDeltaTime);
                SetHorizontalVelocityWithConveyor(newVX);
                ApplyBetterJumpGravity();
                ApplyPendingDownHitBounce();
                return;
            }

            Move();
            ApplyBetterJumpGravity();
            if (isWallSliding)
                ApplyWallSlideVelocity();
            ApplyMovingPlatformDelta();
            ApplyPendingDownHitBounce();
        }

        // --------------------
        // ATTACK
        // --------------------
        private void StartAttack()
        {
            if (IsSwinging || isOverheadRailHanging || IsSliding || IsDiving) return;

            DropCarriedObject();

            ComputeAndLockAttackDirection();
            attackSequenceId = attackSequenceId == int.MaxValue ? 1 : attackSequenceId + 1;
            attackDamageTargets.Clear();

            animDriver?.TriggerAttack(lockedAttackDirType);

            isAttacking = true;
            isSustainedAirDownAttack = lockedAttackDirType == 2 && !isGrounded;
            sustainedDownAttackStartActionPressVersion = isSustainedAirDownAttack && input != null
                ? input.GameplayActionPressVersion
                : 0;
            canCancelAttack = false;
            attackStateTimer = attackLockSeconds;
            cancelEnableTimer = attackCancelEnableSeconds;

            bool didHit = Attack(lockedAttackDir);

            attackCooldownTimer = attackCooldown;

            if (debugLogs)
            {
                Debug.Log(
                    $"[PLAYER ATTACK] direction={GetLockedAttackDirection()} " +
                    $"attackId={attackSequenceId} didHit={didHit} enemy={lastAttackHitEnemy}");
            }

            ResolveAttackHit(didHit);
        }

        private void UpdateSustainedAirDownAttack()
        {
            if (!isSustainedAirDownAttack)
                return;

            if (!isAttacking || isGrounded)
            {
                CancelAttack(isGrounded ? "down attack landed" : "down attack interrupted");
                return;
            }

            ResolveAttackHit(Attack(Vector2.down));
        }

        private void ResolveAttackHit(bool didHit)
        {
            if (!didHit)
                return;

            if (lastAttackHitEnemy && hitStopSeconds > 0f)
                HitStopManager.Request(hitStopSeconds);

            if (lockedAttackDirType == 2 && lastAttackAllowsDownBounce)
                QueueDownHitBounce();

            if (isSustainedAirDownAttack)
                CancelAttack("down attack hit");
        }

        private void QueueDownHitBounce()
        {
            if (pendingDownHitBounce || lastQueuedDownHitBounceAttackId == attackSequenceId)
                return;

            pendingDownHitBounceOverridesHorizontal =
                lastAttackUsesDirectionalDownHitBounce;
            if (pendingDownHitBounceOverridesHorizontal)
            {
                int direction = lastDownHitPlayerSeparationDirection != 0
                    ? lastDownHitPlayerSeparationDirection
                    : facingDir;
                float horizontalBounce = lastAttackUsesTankDownHitBounce
                    ? tankDownHitBounceHorizontal
                    : downHitBounceHorizontal;
                pendingDownHitBounceHorizontal = direction * horizontalBounce;
                pendingDownHitBounceVertical = lastAttackUsesTankDownHitBounce
                    ? tankDownHitBounceVertical
                    : downHitBounceVertical;
            }
            else
            {
                pendingDownHitBounceHorizontal = 0f;
                pendingDownHitBounceVertical = pogoBounceForce;
            }

            pendingDownHitBounce = true;
            lastQueuedDownHitBounceAttackId = attackSequenceId;
        }

        private void ApplyPendingDownHitBounce()
        {
            if (!pendingDownHitBounce)
                return;

            pendingDownHitBounce = false;
            ClearGroundStateForDownHitBounce();
            float velocityX = pendingDownHitBounceOverridesHorizontal
                ? pendingDownHitBounceHorizontal
                : rb.velocity.x;
            rb.velocity = new Vector2(
                velocityX,
                Mathf.Max(rb.velocity.y, pendingDownHitBounceVertical));
            downHitBounceAscentProtected = rb.velocity.y > 0.01f;
        }

        private void ClearGroundStateForDownHitBounce()
        {
            isGrounded = false;
            groundNormal = Vector2.up;
            currentGroundCollider = null;
            currentGroundPlatform = null;
            currentGroundStandableSurface = null;
            idleAnchorStandableSurface = null;
            idleStandableOffsetX = 0f;
            slopeMovementSuppressedUntilAirborne = true;
            UpdateGroundConveyor(null, null);
        }

        private void CancelAttack(string reason)
        {
            isAttacking = false;
            isSustainedAirDownAttack = false;
            sustainedDownAttackStartActionPressVersion = 0;
            canCancelAttack = false;
            attackStateTimer = 0f;
            cancelEnableTimer = 0f;

            if (debugLogs)
                Debug.Log($"[PLAYER ATTACK] CANCELED by {reason}");
        }

        private bool TryCancelSustainedDownAttackFromNewAction()
        {
            if (!isSustainedAirDownAttack || input == null ||
                input.GameplayActionPressVersion == sustainedDownAttackStartActionPressVersion)
            {
                return false;
            }

            CancelAttack("new gameplay action");
            return true;
        }

        private void ComputeAndLockAttackDirection()
        {
            float ay = Mathf.Clamp(input.MoveInput.y, -1f, 1f);

            if (allowVerticalAim && ay <= -attackDownThreshold)
            {
                lockedAttackDirType = 2;
                lockedAttackDir = Vector2.down;
                return;
            }

            if (allowVerticalAim && ay >= attackUpThreshold)
            {
                lockedAttackDirType = 1;
                lockedAttackDir = Vector2.up;
                return;
            }

            lockedAttackDirType = 0;
            lockedAttackDir = new Vector2(facingDir, 0f);
        }

        private bool Attack(Vector2 dir)
        {
            if (attackPoint == null) return false;

            lastAttackAllowsDownBounce = false;
            lastAttackUsesDirectionalDownHitBounce = false;
            lastAttackUsesTankDownHitBounce = false;
            lastDownHitPlayerSeparationDirection = 0;

            if (!allowVerticalAim)
                dir = new Vector2(facingDir, 0f);

            if (dir.sqrMagnitude < 0.0001f)
                dir = new Vector2(facingDir, 0f);

            float offset = attackOffset;
            if (offset <= 0f)
            {
                offset = attackPointLocalXAbs > 0f
                    ? attackPointLocalXAbs
                    : Mathf.Max(0.01f, Mathf.Abs(attackPoint.localPosition.x));
            }

            Game.Combat.PlayerAttackDirection attackDirection = GetLockedAttackDirection();
            float queryRadius = attackRadius;
            if (attackDirection == Game.Combat.PlayerAttackDirection.Up)
            {
                offset = upAttackOffset;
                queryRadius = upAttackRadius;
            }

            Vector2 hitPos = (Vector2)transform.position + dir.normalized * offset;
            attackPoint.position = hitPos;

            int count = Physics2D.OverlapCircleNonAlloc(hitPos, queryRadius, attackHits, enemyLayer);
            bool didHit = false;
            lastAttackHitEnemy = false;

            for (int i = 0; i < count; i++)
            {
                var hitCol = attackHits[i];
                if (hitCol == null) continue;

                var playerAttackReceiver =
                    hitCol.GetComponent<Game.Combat.IPlayerAttackReceiver>() ??
                    hitCol.GetComponentInParent<Game.Combat.IPlayerAttackReceiver>();

                if (playerAttackReceiver != null)
                {
                    UnityEngine.Object receiverTarget = playerAttackReceiver as UnityEngine.Object;
                    if (receiverTarget != null && !attackDamageTargets.Add(receiverTarget))
                        continue;

                    bool isCurrentSupport = IsReceiverCurrentStandableSupport(playerAttackReceiver);
                    var context = new Game.Combat.PlayerAttackContext(
                        attackSequenceId,
                        stats.attackDamage,
                        attackDirection,
                        dir.normalized,
                        transform.position,
                        facingDir,
                        isCurrentSupport,
                        transform);
                    var receiverComponent = receiverTarget as Component;
                    var receiverHealth = receiverComponent != null
                        ? receiverComponent.GetComponentInParent<Game.Combat.Health>()
                        : null;
                    int healthBefore = receiverHealth != null ? receiverHealth.CurrentHP : -1;
                    Game.Combat.PlayerAttackResolution resolution =
                        playerAttackReceiver.ReceivePlayerAttack(context);
                    int healthAfter = receiverHealth != null ? receiverHealth.CurrentHP : -1;

                    if (resolution == Game.Combat.PlayerAttackResolution.None)
                        continue;

                    didHit = true;
                    lastAttackHitEnemy = true;
                    if (attackDirection == Game.Combat.PlayerAttackDirection.Down &&
                        (resolution == Game.Combat.PlayerAttackResolution.Damage ||
                         resolution == Game.Combat.PlayerAttackResolution.BellyUp))
                    {
                        lastAttackAllowsDownBounce = true;
                        bool resolvedBellyUp =
                            resolution == Game.Combat.PlayerAttackResolution.BellyUp;
                        if (resolvedBellyUp || !lastAttackUsesDirectionalDownHitBounce)
                        {
                            lastAttackUsesDirectionalDownHitBounce = true;
                            lastDownHitPlayerSeparationDirection =
                                ResolveDownHitPlayerSeparationDirection(
                                    receiverComponent,
                                    hitCol);
                        }
                        if (resolvedBellyUp)
                            lastAttackUsesTankDownHitBounce = true;
                    }
                    LogPlayerAttackHit(
                        receiverTarget,
                        attackDirection,
                        resolution,
                        healthBefore,
                        healthAfter);

                    if (attackDirection == Game.Combat.PlayerAttackDirection.Down &&
                        isCurrentSupport &&
                        (resolution == Game.Combat.PlayerAttackResolution.Damage ||
                         resolution == Game.Combat.PlayerAttackResolution.BellyUp))
                    {
                        ReleaseCurrentStandableSupport(
                            playerAttackReceiver,
                            resolution == Game.Combat.PlayerAttackResolution.BellyUp);
                    }

                    continue;
                }

                var dmgable =
                    hitCol.GetComponent<Game.Combat.IDamageable>() ??
                    hitCol.GetComponentInParent<Game.Combat.IDamageable>();

                if (dmgable != null)
                {
                    UnityEngine.Object damageTarget = dmgable as UnityEngine.Object;
                    if (damageTarget != null && !attackDamageTargets.Add(damageTarget))
                        continue;

                    bool hitEnemy = IsEnemyHit(hitCol, dmgable);
                    Component damageComponent = damageTarget as Component;
                    Game.Combat.Health damageHealth = damageComponent != null
                        ? damageComponent.GetComponentInParent<Game.Combat.Health>()
                        : null;
                    int healthBefore = damageHealth != null ? damageHealth.CurrentHP : -1;
                    bool wasCurrentSupport =
                        IsHitCurrentStandableSupport(hitCol, damageComponent);
                    dmgable.TakeDamage(stats.attackDamage, transform.position);
                    int healthAfter = damageHealth != null ? damageHealth.CurrentHP : -1;
                    bool damageApplied = damageHealth == null || healthAfter < healthBefore;
                    if (!damageApplied)
                        continue;

                    didHit = true;
                    if (hitEnemy)
                        lastAttackHitEnemy = true;

                    if (hitEnemy &&
                        attackDirection == Game.Combat.PlayerAttackDirection.Down)
                    {
                        lastAttackAllowsDownBounce = true;
                        if (wasCurrentSupport)
                            ReleaseCurrentStandableSupport(false);
                    }

                    continue;
                }

                var health =
                    hitCol.GetComponent<Game.Combat.Health>() ??
                    hitCol.GetComponentInParent<Game.Combat.Health>();

                if (health != null)
                {
                    if (!attackDamageTargets.Add(health))
                        continue;

                    int healthBefore = health.CurrentHP;
                    bool wasCurrentSupport = IsHitCurrentStandableSupport(hitCol, health);
                    health.TakeDamage(stats.attackDamage, transform.position);
                    if (health.CurrentHP >= healthBefore)
                        continue;

                    didHit = true;
                    lastAttackHitEnemy = true;
                    if (attackDirection == Game.Combat.PlayerAttackDirection.Down)
                    {
                        lastAttackAllowsDownBounce = true;
                        if (wasCurrentSupport)
                            ReleaseCurrentStandableSupport(false);
                    }
                }
            }

            return didHit;
        }

        private int ResolveDownHitPlayerSeparationDirection(
            Component receiverComponent,
            Collider2D hitCollider)
        {
            float playerCenterX = capsule != null && capsule.enabled
                ? capsule.bounds.center.x
                : transform.position.x;
            Collider2D receiverCollider = receiverComponent != null
                ? receiverComponent.GetComponent<Collider2D>()
                : null;
            float enemyCenterX = receiverCollider != null
                ? receiverCollider.bounds.center.x
                : hitCollider != null
                    ? hitCollider.bounds.center.x
                    : receiverComponent != null
                        ? receiverComponent.transform.position.x
                        : playerCenterX;
            float enemyOffset = enemyCenterX - playerCenterX;
            if (Mathf.Abs(enemyOffset) > 0.1f)
                return enemyOffset > 0f ? -1 : 1;

            return facingDir < 0 ? -1 : 1;
        }

        private Game.Combat.PlayerAttackDirection GetLockedAttackDirection()
        {
            if (lockedAttackDirType == 2)
                return Game.Combat.PlayerAttackDirection.Down;
            if (lockedAttackDirType == 1)
                return Game.Combat.PlayerAttackDirection.Up;
            return Game.Combat.PlayerAttackDirection.Horizontal;
        }

        private bool IsReceiverCurrentStandableSupport(
            Game.Combat.IPlayerAttackReceiver receiver)
        {
            if (!isGrounded || currentGroundStandableSurface == null || receiver == null)
                return false;

            var supportReceiver =
                currentGroundStandableSurface.GetComponent<Game.Combat.IPlayerAttackReceiver>() ??
                currentGroundStandableSurface.GetComponentInParent<Game.Combat.IPlayerAttackReceiver>();
            return ReferenceEquals(supportReceiver, receiver);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogPlayerAttackHit(
            UnityEngine.Object target,
            Game.Combat.PlayerAttackDirection direction,
            Game.Combat.PlayerAttackResolution resolution,
            int healthBefore,
            int healthAfter)
        {
            if (!debugLogs)
                return;

            Debug.Log(
                $"[PLAYER ATTACK HIT] target={(target != null ? target.name : "None")} " +
                $"direction={direction} attackId={attackSequenceId} resolvedAs={resolution} " +
                $"healthBefore={healthBefore} healthAfter={healthAfter}",
                this);
        }

        private static bool IsEnemyHit(Collider2D hitCol, Game.Combat.IDamageable dmgable)
        {
            if (hitCol != null && hitCol.GetComponentInParent<Game.Enemies.EnemyBase>() != null)
                return true;

            UnityEngine.Object damageObject = dmgable as UnityEngine.Object;
            if (damageObject is Component component)
            {
                if (component.GetComponentInParent<Game.Enemies.EnemyBase>() != null)
                    return true;
                if (component.GetComponentInParent<Game.Combat.Health>() != null)
                    return true;
            }

            return false;
        }

        private void TryInteract()
        {
            if (IsSwinging || isOverheadRailHanging || IsSliding || IsDiving) return;
            if (interactLockTimer > 0f) return;

            if (carriedObject != null)
            {
                DropCarriedObject();
                return;
            }

            IInteractable target = GetClosestInteractable();
            if (target == null) return;

            if (target is BarrelCarryable carryable)
            {
                if (carryable.TryPickUp(this))
                    interactLockTimer = interactLockSecondsAfterPickup;

                return;
            }

            target.Interact(gameObject);
        }

        public void DropCarriedObject()
        {
            if (carriedObject == null) return;

            BarrelCarryable toDrop = carriedObject;
            toDrop.Drop();
            interactLockTimer = interactLockSecondsAfterDrop;
        }

        public bool TrySetCarriedObject(BarrelCarryable carryable)
        {
            if (carryable == null) return false;
            if (IsSwinging || isOverheadRailHanging || isFlagSpringPoleAttached ||
                IsSliding || IsDiving)
                return false;
            if (carriedObject != null && carriedObject != carryable) return false;

            carriedObject = carryable;
            return true;
        }

        public void ClearCarriedObject(BarrelCarryable carryable)
        {
            if (carriedObject == carryable)
                carriedObject = null;
        }

        private void LateUpdate()
        {
            wasCrouching = IsCrouching;
            wasLayDown = IsLayDown;
        }

        public bool TryEnterFlagSpringPole(FlagSpringPole pole)
        {
            if (!CanEnterFlagSpringPole(pole))
                return false;
            if (input != null &&
                !TryStandingPostureAndCancelGesture(
                    "PlayerController.TryEnterFlagSpringPole"))
            {
                return false;
            }
            if (!pole.Attach(this))
                return false;

            DropCarriedObject();
            ClearSprintMomentum();
            CancelSprintSlideDiveForReset();
            if (isDashing)
                EndDash();
            if (isOverheadRailHanging)
                ForceEndOverheadRailForReset();
            if (IsSwinging)
                ForceEndSwingForReset();
            if (isHanging)
            {
                if (ledgeClimbRoutine != null)
                {
                    StopCoroutine(ledgeClimbRoutine);
                    ledgeClimbRoutine = null;
                }

                if (capsule != null)
                    capsule.enabled = true;
                ExitHang("flag-spring-pole");
            }

            flagSpringPoleCachedBodyType = rb.bodyType;
            flagSpringPoleCachedConstraints = rb.constraints;
            flagSpringPoleCachedGravityScale = rb.gravityScale;
            currentFlagSpringPole = pole;
            isFlagSpringPoleAttached = true;
            flagSpringPoleHandOffset = SwingHandPosition - rb.position;
            pendingDownHitBounce = false;
            downHitBounceAscentProtected = false;

            InvalidateSwingGrabTemporalState();
            ClearGroundSupportForOverheadTraversal();
            isWallSliding = false;
            isWallLosingContact = false;
            wallJumpLockTimer = 0f;
            wallJumpRegrabTimer = 0f;
            wallStickTimer = 0f;
            wallCoyoteTimer = 0f;
            wallStickSide = 0;
            wallCoyoteSide = 0;
            suppressWallWhileDownHeld = false;
            jumpBufferTimer = 0f;
            coyoteTimer = 0f;
            hasPendingNormalJump = false;
            pendingNormalJumpTimer = 0f;
            normalJumpAnticipationAge = 0f;

            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.gravityScale = 0f;
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.position = pole.GrabPosition - flagSpringPoleHandOffset;
            return true;
        }

        public bool CanEnterFlagSpringPole(FlagSpringPole pole)
        {
            if (pole == null || !pole.isActiveAndEnabled || !pole.CanGrab(this))
                return false;
            if (!isActiveAndEnabled || externalControlLocked)
                return false;
            if (rb == null || !rb.simulated || capsule == null || !capsule.enabled)
                return false;
            if (isFlagSpringPoleAttached || isAttacking || IsMoveLockedByParry)
                return false;
            return true;
        }

        private void UpdateFlagSpringPoleTraversal()
        {
            if (currentFlagSpringPole == null ||
                !currentFlagSpringPole.isActiveAndEnabled)
            {
                ForceEndFlagSpringPoleForReset();
                return;
            }

            InputReader.CrouchCommand crouchCommand = input.ConsumeCrouchPress();
            input.CancelCrouchGesture("PlayerController.UpdateFlagSpringPoleTraversal");
            input.ClearSwingGrabBuffer();
            input.ConsumeAttackPressed();
            input.ConsumeDashPressed();
            input.ConsumeParryPressed();
            input.ConsumeInteractPressed();

            if (input.ConsumeJumpPressed())
            {
                ExitFlagSpringPole(true, true);
                return;
            }

            bool dropRequested = crouchCommand.IsValid ||
                                 input.MoveInput.y <= -hangDownThreshold;
            if (dropRequested)
                ExitFlagSpringPole(false, true);
        }

        internal void FollowFlagSpringPole(FlagSpringPole pole, Vector2 grabPosition)
        {
            if (!isFlagSpringPoleAttached || currentFlagSpringPole != pole || rb == null)
                return;

            rb.MovePosition(grabPosition - flagSpringPoleHandOffset);
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        internal void ForceExitFlagSpringPole(FlagSpringPole pole)
        {
            if (!isFlagSpringPoleAttached || currentFlagSpringPole != pole)
                return;

            ExitFlagSpringPole(false, false);
        }

        private void ExitFlagSpringPole(bool jumpOff, bool notifyPole)
        {
            if (!isFlagSpringPoleAttached)
                return;

            FlagSpringPole pole = currentFlagSpringPole;
            float horizontalInput = input != null
                ? Mathf.Clamp(input.MoveInput.x, -1f, 1f)
                : 0f;
            Vector2 exitVelocity = jumpOff && pole != null
                ? pole.CalculateLaunchVelocity(GetEffectiveJumpForce(), horizontalInput)
                : Vector2.zero;

            isFlagSpringPoleAttached = false;
            currentFlagSpringPole = null;
            rb.bodyType = flagSpringPoleCachedBodyType;
            rb.constraints = flagSpringPoleCachedConstraints;
            rb.gravityScale = flagSpringPoleCachedGravityScale;
            rb.angularVelocity = 0f;
            rb.velocity = exitVelocity;
            ClearGroundSupportForOverheadTraversal();
            slopeMovementSuppressedUntilAirborne = jumpOff;
            if (jumpOff)
            {
                upwardAirCurrentMomentumProtected = false;
                animDriver?.TriggerJumpStart();
                if (Mathf.Abs(exitVelocity.x) > 0.01f)
                    ApplyFacing(exitVelocity.x < 0f ? -1 : 1);
            }

            if (!notifyPole || pole == null)
                return;
            if (jumpOff)
                pole.NotifyPlayerLaunched(this, exitVelocity);
            else
                pole.NotifyPlayerDropped(this);
        }

        private void ForceEndFlagSpringPoleForReset()
        {
            if (isFlagSpringPoleAttached)
                ExitFlagSpringPole(false, true);
        }

        public bool TryEnterOverheadRail(OverheadHangRail rail)
        {
            if (!CanEnterOverheadRail(rail)) return false;
            if (input != null &&
                !TryStandingPostureAndCancelGesture("PlayerController.TryEnterOverheadRail"))
                return false;

            DropCarriedObject();
            ClearSprintMomentum();
            return EnterOverheadRail(rail);
        }

        public bool CanEnterOverheadRail(OverheadHangRail rail)
        {
            if (rail == null || !rail.isActiveAndEnabled) return false;
            if (!isActiveAndEnabled || externalControlLocked) return false;
            if (rb == null || !rb.simulated || capsule == null || !capsule.enabled) return false;
            if (swingHandAnchor == null || overheadRailJoint == null) return false;
            if (isOverheadRailHanging || isFlagSpringPoleAttached ||
                IsSwinging || isHanging || isDashing)
                return false;
            if (IsSliding || IsDiving || IsPostDiveSliding) return false;
            if (isAttacking || IsMoveLockedByParry) return false;
            return rail.CanGrabHandAt(SwingHandPosition);
        }

        public bool CanEnterOverheadRailForFeedback(OverheadHangRail rail)
        {
            if (!CanEnterOverheadRail(rail)) return false;
            return CanPrepareStandingPostureForSwing();
        }

        private bool EnterOverheadRail(OverheadHangRail rail)
        {
            if (rail == null || !rail.HasValidSegment || swingHandAnchor == null ||
                overheadRailJoint == null)
                return false;

            Vector2 tangent = rail.GetTangent();
            Vector2 entryVelocity = rb.velocity;
            Vector2 handPosition = swingHandAnchor.position;
            float normalizedPosition = rail.ProjectPointToRail(handPosition);
            Vector2 grabPosition = rail.GetPositionAtNormalized(normalizedPosition);
            Vector2 localHandAnchor = transform.InverseTransformPoint(handPosition);

            InvalidateSwingGrabTemporalState();
            overheadRailCachedBodyType = rb.bodyType;
            overheadRailCachedConstraints = rb.constraints;
            overheadRailCachedGravityScale = rb.gravityScale;

            currentOverheadRail = rail;
            isOverheadRailHanging = true;
            isOverheadRailStealthHanging = false;
            overheadRailAlongSpeed = Vector2.Dot(entryVelocity, tangent);
            isDashing = false;
            isWallSliding = false;
            wallStickTimer = 0f;
            wallCoyoteTimer = 0f;
            jumpBufferTimer = 0f;
            coyoteTimer = 0f;
            hasPendingNormalJump = false;
            pendingNormalJumpTimer = 0f;
            normalJumpAnticipationAge = 0f;
            parryMoveLockTimer = 0f;
            ClearGroundSupportForOverheadTraversal();

            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.constraints =
                (overheadRailCachedConstraints &
                 ~(RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezePositionY)) |
                RigidbodyConstraints2D.FreezeRotation;
            rb.gravityScale = 0f;
            rb.angularVelocity = 0f;
            rb.position += grabPosition - handPosition;
            rb.velocity = tangent * overheadRailAlongSpeed;

            overheadRailJoint.enabled = false;
            overheadRailJoint.connectedBody = null;
            overheadRailJoint.anchor = localHandAnchor;
            overheadRailJoint.connectedAnchor = rail.PointA;
            overheadRailJoint.autoConfigureAngle = false;
            // With no connected body, Unity measures this joint's translation from the
            // attached hand anchor toward connectedAnchor. Pointing the joint axis B -> A
            // therefore maps PointA to 0 and PointB to +railLength while preserving the
            // same physical line as the gameplay tangent A -> B.
            overheadRailJoint.angle = Vector2.SignedAngle(Vector2.right, -tangent);
            overheadRailJoint.useMotor = false;

            float railLength = rail.Length;
            JointTranslationLimits2D limits = overheadRailJoint.limits;
            limits.min = 0f;
            limits.max = railLength;
            overheadRailJoint.limits = limits;
            overheadRailJoint.useLimits = true;
            overheadRailJoint.enabled = true;

            if (debugLogs)
            {
                Debug.Log(
                    $"[OVERHEAD RAIL] ENTER rail={rail.name} position={normalizedPosition:0.000} " +
                    $"entrySpeed={overheadRailAlongSpeed:0.00}");
            }

            return true;
        }

        private void UpdateOverheadRailHanging()
        {
            UpdateOverheadRailStealthState();
            input.CancelCrouchGesture("PlayerController.UpdateOverheadRailHanging");
            input.ClearSwingGrabBuffer();
            input.ConsumeAttackPressed();
            input.ConsumeDashPressed();
            input.ConsumeParryPressed();

            if (input.ConsumeJumpPressed())
            {
                ExitOverheadRail(true);
                return;
            }

            if (input.ConsumeInteractPressed())
                ExitOverheadRail(false);
        }

        private void UpdateOverheadRailStealthState()
        {
            float verticalInput = Mathf.Clamp(input.MoveInput.y, -1f, 1f);
            float enterThreshold = Mathf.Clamp01(overheadRailStealthEnterThreshold);
            float exitThreshold = Mathf.Min(
                enterThreshold,
                Mathf.Clamp01(overheadRailStealthExitThreshold));

            if (!isOverheadRailStealthHanging)
            {
                if (verticalInput >= enterThreshold)
                    isOverheadRailStealthHanging = true;
                return;
            }

            if (verticalInput < exitThreshold)
                isOverheadRailStealthHanging = false;
        }

        private void FixedUpdateOverheadRailHanging()
        {
            if (currentOverheadRail == null || !currentOverheadRail.isActiveAndEnabled ||
                overheadRailJoint == null || !overheadRailJoint.enabled ||
                swingHandAnchor == null || !currentOverheadRail.HasValidSegment)
            {
                ExitOverheadRail(false);
                return;
            }

            Vector2 tangent = currentOverheadRail.GetTangent();
            float traversalInput = Mathf.Clamp(
                currentOverheadRail.GetPlayerTraversalInput(input.MoveInput),
                -1f,
                1f);
            if (Mathf.Abs(traversalInput) < wallInputDeadzone)
                traversalInput = 0f;

            float voluntaryMoveMultiplier = isOverheadRailStealthHanging
                ? Mathf.Clamp(overheadRailStealthMoveMultiplier, 0.1f, 1f)
                : 1f;
            float targetSpeed = currentOverheadRail.TransportSpeed +
                traversalInput * currentOverheadRail.PlayerMoveSpeed *
                voluntaryMoveMultiplier;
            float rate = Mathf.Abs(traversalInput) > 0.01f
                ? currentOverheadRail.PlayerAcceleration
                : currentOverheadRail.PlayerDeceleration;
            float actualAlongSpeed = Vector2.Dot(rb.velocity, tangent);
            float nextAlongSpeed = Mathf.MoveTowards(
                actualAlongSpeed,
                targetSpeed,
                rate * Time.fixedDeltaTime);

            float normalizedPosition = currentOverheadRail.ProjectPointToRail(
                swingHandAnchor.position);
            float normalizedEndTolerance = Mathf.Min(
                0.05f,
                0.025f / Mathf.Max(0.05f, currentOverheadRail.Length));
            bool pushingPastA =
                normalizedPosition <= normalizedEndTolerance && nextAlongSpeed < 0f;
            bool pushingPastB =
                normalizedPosition >= 1f - normalizedEndTolerance && nextAlongSpeed > 0f;

            if (pushingPastA || pushingPastB)
            {
                if (currentOverheadRail.EndBehavior == OverheadRailEndBehavior.Drop)
                {
                    rb.velocity = tangent * nextAlongSpeed;
                    overheadRailAlongSpeed = nextAlongSpeed;
                    ExitOverheadRail(false);
                    return;
                }

                nextAlongSpeed = 0f;
            }

            overheadRailAlongSpeed = nextAlongSpeed;
            rb.velocity = tangent * overheadRailAlongSpeed;
            rb.angularVelocity = 0f;
        }

        private void ExitOverheadRail(bool jumpOff)
        {
            if (!isOverheadRailHanging) return;

            OverheadHangRail rail = currentOverheadRail;
            Vector2 tangent = rail != null && rail.HasValidSegment
                ? rail.GetTangent()
                : Vector2.right;
            float exitAlongSpeed = rb != null
                ? Vector2.Dot(rb.velocity, tangent)
                : overheadRailAlongSpeed;
            float jumpSpeed = jumpOff && rail != null
                ? rail.JumpOffVerticalSpeed
                : 0f;

            DisableOverheadRailJoint();
            isOverheadRailHanging = false;
            isOverheadRailStealthHanging = false;
            currentOverheadRail = null;
            overheadRailAlongSpeed = 0f;

            rb.bodyType = overheadRailCachedBodyType;
            rb.constraints = overheadRailCachedConstraints;
            rb.gravityScale = overheadRailCachedGravityScale;
            rb.angularVelocity = 0f;
            rb.velocity = tangent * exitAlongSpeed + Vector2.up * jumpSpeed;
            ClearGroundSupportForOverheadTraversal();
            slopeMovementSuppressedUntilAirborne = jumpOff;

            if (debugLogs)
            {
                Debug.Log(
                    $"[OVERHEAD RAIL] EXIT jump={jumpOff} " +
                    $"alongSpeed={exitAlongSpeed:0.00} velocity={rb.velocity}");
            }
        }

        private void ForceEndOverheadRailForReset()
        {
            DisableOverheadRailJoint();
            if (isOverheadRailHanging && rb != null)
            {
                rb.bodyType = overheadRailCachedBodyType;
                rb.constraints = overheadRailCachedConstraints;
                rb.gravityScale = overheadRailCachedGravityScale;
                rb.angularVelocity = 0f;
            }

            isOverheadRailHanging = false;
            isOverheadRailStealthHanging = false;
            currentOverheadRail = null;
            overheadRailAlongSpeed = 0f;
            ClearGroundSupportForOverheadTraversal();
        }

        private void DisableOverheadRailJoint()
        {
            if (overheadRailJoint == null) return;

            overheadRailJoint.enabled = false;
            overheadRailJoint.connectedBody = null;
            overheadRailJoint.useMotor = false;
        }

        private void ClearGroundSupportForOverheadTraversal()
        {
            isGrounded = false;
            groundNormal = Vector2.up;
            currentGroundPlatform = null;
            currentGroundStandableSurface = null;
            currentGroundConveyor = null;
            currentGroundWoodWorkpiece = null;
            appliedConveyorVelocityX = 0f;
            suppressConveyorUntilAirborne = false;
            slopeMovementSuppressedUntilAirborne = false;
            ResetSlopeMovementCarry();
        }

        private void HandlePlayerDamaged(int damage)
        {
            if (isAttacking)
                CancelAttack("damaged");

            if (isFlagSpringPoleAttached)
                ExitFlagSpringPole(false, true);

            if (isOverheadRailHanging)
                ExitOverheadRail(false);
        }

        /// <summary>
        /// Releases traversal states that would otherwise leave physics suspended while
        /// PlayerHealth temporarily disables input processing for hit-stun.
        /// </summary>
        internal void PrepareForHitStun()
        {
            if (isAttacking)
                CancelAttack("hit-stun");

            if (isDashing)
                EndDash();

            if (isFlagSpringPoleAttached)
                ExitFlagSpringPole(false, true);

            if (isOverheadRailHanging)
                ExitOverheadRail(false);

            if (IsSwinging)
                ForceEndSwingForReset();

            if (isHanging)
            {
                if (ledgeClimbRoutine != null)
                {
                    StopCoroutine(ledgeClimbRoutine);
                    ledgeClimbRoutine = null;
                }

                if (capsule != null)
                    capsule.enabled = true;

                ExitHang("hit-stun");
            }

            if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
                rb.gravityScale = defaultGravityScale;
        }

        public bool TryEnterSwing(SwingPole pole)
        {
            if (!CanEnterSwing(pole)) return false;
            return CompleteSwingEntry(pole, "PlayerController.TryEnterSwing");
        }

        public bool TryEnterSwingFromCoyote(SwingPole pole)
        {
            if (!CanEnterSwingFromCoyote(pole)) return false;
            return CompleteSwingEntry(pole, "PlayerController.TryEnterSwingFromCoyote");
        }

        private bool CompleteSwingEntry(SwingPole pole, string source)
        {
            if (input != null &&
                !TryStandingPostureAndCancelGesture(source))
                return false;

            DropCarriedObject();
            ClearSprintMomentum();
            return EnterSwing(pole);
        }

        public bool CanEnterSwing(SwingPole pole)
        {
            if (pole == null) return false;
            if (IsSwinging || isOverheadRailHanging || isFlagSpringPoleAttached ||
                isHanging || IsSliding || IsDiving)
                return false;
            if (swingCooldownTimer > 0f) return false;
            return pole.CanCatch(this, rb);
        }

        public bool CanEnterSwingFromCoyote(SwingPole pole)
        {
            if (pole == null) return false;
            if (IsSwinging || isOverheadRailHanging || isFlagSpringPoleAttached ||
                isHanging || IsSliding || IsDiving)
                return false;
            if (swingCooldownTimer > 0f) return false;
            return pole.CanCatchDuringCoyote(this, rb);
        }

        public bool CanEnterSwingForFeedback(SwingPole pole)
        {
            if (!CanEnterSwing(pole)) return false;
            return CanPrepareStandingPostureForSwing();
        }

        public bool CanEnterSwingFromCoyoteForFeedback(SwingPole pole)
        {
            if (!CanEnterSwingFromCoyote(pole)) return false;
            return CanPrepareStandingPostureForSwing();
        }

        private bool CanPrepareStandingPostureForSwing()
        {
            if (input == null) return true;
            if (capsule == null) return false;

            return CanAdoptPosture(
                InputReader.PlayerPosture.Standing,
                "PlayerController.CanEnterSwingForFeedback",
                false);
        }

        public bool TryEnterLedgeGrabPoint(LedgeGrabPoint point)
        {
            if (!CanEnterLedgeGrabPoint(point)) return false;

            ApplyFacing(point.FacingDirection);
            EnterHang(point.HangPosition, point.CornerPosition, point.HasStandPosition, point.StandPosition, point);
            return true;
        }

        public bool CanAutoRescueLedgeGrabPoint(LedgeGrabPoint point, float maxAbsVerticalSpeed, float maxAbsHorizontalSpeed)
        {
            if (!CanEnterLedgeGrabPoint(point)) return false;
            if (isGrounded) return false;
            if (Mathf.Abs(rb.velocity.y) >= maxAbsVerticalSpeed) return false;
            if (Mathf.Abs(rb.velocity.x) >= maxAbsHorizontalSpeed) return false;
            return true;
        }

        public bool CanEnterLedgeGrabPoint(LedgeGrabPoint point)
        {
            if (point == null || !point.isActiveAndEnabled) return false;
            if (!enableLedgeGrab) return false;
            if (IsSwinging || isOverheadRailHanging || isFlagSpringPoleAttached ||
                isHanging || isDashing || IsSliding || IsDiving || IsCarrying)
                return false;
            if (ledgeGrabCooldownTimer > 0f) return false;
            if (IsCrouching || IsLayDown) return false;
            if (suppressWallWhileDownHeld) return false;
            if (point.FacingDirection == 0) return false;
            return true;
        }

        private bool EnterSwing(SwingPole pole)
        {
            SwingAttachmentMode requestedMode = pole.AttachmentMode;
            if (requestedMode == SwingAttachmentMode.HandPivot &&
                (swingHandAnchor == null || handPivotJoint == null))
            {
                Debug.LogError(
                    $"[SWING] Cannot enter HandPivot on {pole.name}: " +
                    "Player requires SwingHandAnchor and HingeJoint2D.",
                    this);
                return false;
            }

            InvalidateSwingGrabTemporalState();

            Vector2 entryVelocity = rb.velocity;

            currentSwingPole = pole;
            activeSwingMode = requestedMode;
            isSwinging = true;
            isHandPivotRecovering = false;
            isDashing = false;
            isWallSliding = false;
            wallStickTimer = 0f;
            wallCoyoteTimer = 0f;
            jumpBufferTimer = 0f;
            coyoteTimer = 0f;
            parryMoveLockTimer = 0f;

            if (isAttacking)
                CancelAttack("swing");

            DisableSwingJoints();

            if (activeSwingMode == SwingAttachmentMode.HandPivot)
                EnterHandPivotSwing(pole, entryVelocity);
            else
                EnterRopeSwing(pole, entryVelocity);

            if (debugLogs)
                Debug.Log($"[SWING] ENTER mode={activeSwingMode} pole={pole.name} anchor={pole.AnchorPosition}");

            return true;
        }

        private void EnterRopeSwing(SwingPole pole, Vector2 entryVelocity)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.gravityScale = defaultGravityScale;
            rb.angularVelocity = 0f;

            Vector2 anchor = pole.AnchorPosition;
            if (pole.SnapOnEnter)
                rb.position = pole.GetInitialPlayerPosition();

            Vector2 radial = rb.position - anchor;
            float radialSqrMagnitude = radial.sqrMagnitude;
            if (radialSqrMagnitude > 0.000001f)
            {
                Vector2 radialDirection = radial / Mathf.Sqrt(radialSqrMagnitude);
                Vector2 tangent = new Vector2(-radialDirection.y, radialDirection.x);
                rb.velocity = tangent * Vector2.Dot(entryVelocity, tangent);
            }
            else
                rb.velocity = Vector2.zero;

            swingJoint.connectedBody = pole.ConnectedBody;
            swingJoint.connectedAnchor = pole.GetConnectedAnchor();
            float initialDistance = Mathf.Max(0.05f, pole.GetSwingDistance(rb.position));
            pole.GetRopeDistanceLimits(
                initialDistance,
                out activeRopeMinDistance,
                out activeRopeMaxDistance);
            swingJoint.distance = Mathf.Clamp(
                initialDistance,
                activeRopeMinDistance,
                activeRopeMaxDistance);
            swingJoint.enabled = true;
        }

        private void EnterHandPivotSwing(SwingPole pole, Vector2 entryVelocity)
        {
            handPivotCachedBodyType = rb.bodyType;
            handPivotCachedConstraints = rb.constraints;
            handPivotUprightRotation = rb.rotation;
            handPivotExitFacing = facingDir;

            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.constraints = handPivotCachedConstraints & ~RigidbodyConstraints2D.FreezeRotation;
            rb.gravityScale = defaultGravityScale;
            rb.angularVelocity = 0f;

            Vector2 localHandAnchor = transform.InverseTransformPoint(swingHandAnchor.position);
            Vector2 anchor = pole.AnchorPosition;
            rb.position += anchor - (Vector2)swingHandAnchor.position;

            handPivotJoint.connectedBody = pole.ConnectedBody;
            handPivotJoint.anchor = localHandAnchor;
            handPivotJoint.connectedAnchor = pole.GetConnectedAnchor();
            handPivotJoint.autoConfigureConnectedAnchor = false;
            handPivotJoint.useMotor = false;

            JointAngleLimits2D limits = handPivotJoint.limits;
            limits.min = handPivotMinAngle;
            limits.max = handPivotMaxAngle;
            handPivotJoint.limits = limits;
            handPivotJoint.useLimits = true;

            Vector2 connectedVelocity = GetSwingAnchorVelocity(pole, anchor);
            Vector2 radial = rb.worldCenterOfMass - anchor;
            float radius = radial.magnitude;
            if (radius > 0.0001f)
            {
                Vector2 tangent = new Vector2(-radial.y, radial.x) / radius;
                float tangentialSpeed = Vector2.Dot(entryVelocity - connectedVelocity, tangent);
                rb.velocity = connectedVelocity + tangent * tangentialSpeed;
                rb.angularVelocity = tangentialSpeed / radius * Mathf.Rad2Deg;
            }
            else
            {
                rb.velocity = connectedVelocity;
                rb.angularVelocity = 0f;
            }

            handPivotJoint.enabled = true;
        }

        private void UpdateSwinging(float inputXClean, float inputYClean)
        {
            input?.CancelCrouchGesture("PlayerController.UpdateSwinging");
            input?.ClearSwingGrabBuffer();

            input.ConsumeAttackPressed();
            input.ConsumeDashPressed();
            input.ConsumeParryPressed();

            if (input.ConsumeJumpPressed())
            {
                ExitSwing(true, inputXClean);
                return;
            }

            if (input.ConsumeInteractPressed())
                ExitSwing(false, inputXClean);
        }

        private void FixedUpdateSwinging()
        {
            if (activeSwingMode == SwingAttachmentMode.HandPivot)
            {
                FixedUpdateHandPivotSwinging();
                return;
            }

            if (currentSwingPole == null || swingJoint == null || !swingJoint.enabled)
            {
                ExitSwing(false);
                return;
            }

            ClampSwingArc();

            float inputX = Mathf.Clamp(input.MoveInput.x, -1f, 1f);
            float inputY = Mathf.Clamp(input.MoveInput.y, -1f, 1f);
            if (Mathf.Abs(inputX) < wallInputDeadzone) inputX = 0f;
            if (Mathf.Abs(inputY) < wallInputDeadzone) inputY = 0f;

            UpdateRopeClimb(inputY);

            if (inputX != 0f)
            {
                ApplyFacing((int)Mathf.Sign(inputX));
                ApplySwingTangentialAssist(inputX);
            }
            else
                ApplySwingIdleDamping();

            ClampSwingArc();
        }

        private void UpdateRopeClimb(float inputY)
        {
            if (inputY == 0f || currentSwingPole == null ||
                !currentSwingPole.AllowRopeClimb ||
                swingJoint == null || !swingJoint.enabled)
            {
                return;
            }

            float climbSpeed = currentSwingPole.RopeClimbSpeed;
            if (climbSpeed <= 0f)
                return;

            float targetDistance = inputY > 0f
                ? activeRopeMinDistance
                : activeRopeMaxDistance;
            float nextDistance = Mathf.MoveTowards(
                swingJoint.distance,
                targetDistance,
                climbSpeed * Mathf.Abs(inputY) * Time.fixedDeltaTime);

            if (!Mathf.Approximately(nextDistance, swingJoint.distance))
                swingJoint.distance = nextDistance;
        }

        private void FixedUpdateHandPivotSwinging()
        {
            if (currentSwingPole == null || handPivotJoint == null || !handPivotJoint.enabled)
            {
                ExitSwing(false);
                return;
            }

            float inputX = Mathf.Clamp(input.MoveInput.x, -1f, 1f);
            if (Mathf.Abs(inputX) < wallInputDeadzone) inputX = 0f;

            if (inputX != 0f)
                ApplyHandPivotTangentialAssist(inputX);
            else
                ApplyHandPivotIdleDamping();
        }

        private void ApplyHandPivotTangentialAssist(float inputX)
        {
            Vector2 anchor = currentSwingPole.AnchorPosition;
            Vector2 radial = rb.worldCenterOfMass - anchor;
            float radius = radial.magnitude;
            if (radius <= 0.0001f) return;

            Vector2 tangent = new Vector2(-radial.y, radial.x) / radius;
            float tangentialInput = Vector2.Dot(Vector2.right * inputX, tangent);
            if (Mathf.Abs(tangentialInput) <= 0.0001f) return;

            float jointAngle = handPivotJoint.jointAngle;
            float angleLimit = jointAngle >= 0f
                ? handPivotMaxAngle
                : Mathf.Abs(handPivotMinAngle);
            float angleRatio = Mathf.Abs(jointAngle) / Mathf.Max(1f, angleLimit);
            float fadeStartRatio = Mathf.Clamp(handPivotAssistFadeStartRatio, 0f, 0.95f);
            float stopRatio = Mathf.Clamp(
                handPivotAssistStopRatio,
                fadeStartRatio + 0.01f,
                1f);
            float fadeProgress = Mathf.InverseLerp(fadeStartRatio, stopRatio, angleRatio);
            float angleMultiplier = 1f - Mathf.SmoothStep(0f, 1f, fadeProgress);
            if (angleMultiplier <= 0f) return;

            Vector2 relativeVelocity = rb.velocity - GetSwingAnchorVelocity(currentSwingPole, anchor);
            float tangentialSpeed = Vector2.Dot(relativeVelocity, tangent);
            bool moving = Mathf.Abs(tangentialSpeed) > Mathf.Max(0f, handPivotRestSpeedThreshold);
            bool accompanyingMotion = !moving || tangentialSpeed * tangentialInput > 0f;
            float directionMultiplier = accompanyingMotion
                ? 1f
                : Mathf.Max(1f, handPivotCounterInputMultiplier);
            float forceMagnitude = tangentialInput * handPivotMotorForce *
                Mathf.Clamp01(handPivotTangentialAssistMultiplier) *
                directionMultiplier * angleMultiplier;

            rb.AddForce(tangent * forceMagnitude, ForceMode2D.Force);
        }

        private void ApplyHandPivotIdleDamping()
        {
            Vector2 anchor = currentSwingPole.AnchorPosition;
            Vector2 radial = rb.worldCenterOfMass - anchor;
            float radius = radial.magnitude;
            if (radius <= 0.0001f) return;

            Vector2 tangent = new Vector2(-radial.y, radial.x) / radius;
            Vector2 relativeVelocity = rb.velocity - GetSwingAnchorVelocity(currentSwingPole, anchor);
            float tangentialSpeed = Vector2.Dot(relativeVelocity, tangent);
            float damping = Mathf.Max(0f, handPivotIdleTangentialDamping);
            rb.AddForce(-tangent * tangentialSpeed * damping * rb.mass, ForceMode2D.Force);
        }

        private void ApplySwingTangentialAssist(float inputX)
        {
            Vector2 radial = rb.position - currentSwingPole.AnchorPosition;
            float radialSqrMagnitude = radial.sqrMagnitude;
            if (radialSqrMagnitude <= 0.000001f) return;

            Vector2 radialDirection = radial / Mathf.Sqrt(radialSqrMagnitude);
            Vector2 tangent = new Vector2(-radialDirection.y, radialDirection.x);
            float tangentialInput = Vector2.Dot(Vector2.right * inputX, tangent);
            if (Mathf.Abs(tangentialInput) <= 0.0001f) return;

            float angle = Vector2.SignedAngle(Vector2.down, radialDirection);
            float angleLimit = angle >= 0f ? swingMaxAngle : Mathf.Abs(swingMinAngle);
            float angleRatio = Mathf.Abs(angle) / Mathf.Max(1f, angleLimit);
            float fadeStartRatio = Mathf.Clamp(swingAssistFadeStartRatio, 0f, 0.95f);
            float stopRatio = Mathf.Clamp(swingAssistStopRatio, fadeStartRatio + 0.01f, 1f);
            float fadeProgress = Mathf.InverseLerp(fadeStartRatio, stopRatio, angleRatio);
            float angleMultiplier = 1f - Mathf.SmoothStep(0f, 1f, fadeProgress);
            if (angleMultiplier <= 0f) return;

            float tangentialSpeed = Vector2.Dot(rb.velocity, tangent);
            bool moving = Mathf.Abs(tangentialSpeed) > Mathf.Max(0f, swingRestSpeedThreshold);
            bool accompanyingMotion = !moving || tangentialSpeed * tangentialInput > 0f;
            float directionMultiplier = accompanyingMotion
                ? 1f
                : Mathf.Max(1f, swingCounterInputMultiplier);
            float forceMagnitude = tangentialInput * swingMotorForce *
                Mathf.Clamp01(swingTangentialAssistMultiplier) * directionMultiplier * angleMultiplier;

            rb.AddForce(tangent * forceMagnitude, ForceMode2D.Force);
        }

        private void ApplySwingIdleDamping()
        {
            Vector2 anchor = currentSwingPole.AnchorPosition;
            Vector2 radial = rb.position - anchor;
            float radialSqrMagnitude = radial.sqrMagnitude;
            if (radialSqrMagnitude <= 0.000001f) return;

            float radius = Mathf.Sqrt(radialSqrMagnitude);
            Vector2 radialDirection = radial / radius;
            Vector2 tangent = new Vector2(-radialDirection.y, radialDirection.x);
            float tangentialSpeed = Vector2.Dot(rb.velocity, tangent);
            float angle = Vector2.SignedAngle(Vector2.down, radialDirection);

            bool nearCenter = Mathf.Abs(angle) <= Mathf.Max(0f, swingIdleSettleAngle);
            bool nearlyStopped = Mathf.Abs(tangentialSpeed) <= Mathf.Max(0f, swingIdleSettleSpeed);
            if (nearCenter && nearlyStopped)
            {
                rb.position = anchor + Vector2.down * radius;
                rb.velocity = Vector2.zero;
                return;
            }

            float damping = Mathf.Max(0f, swingIdleTangentialDamping);
            rb.AddForce(-tangent * tangentialSpeed * damping * rb.mass, ForceMode2D.Force);
        }

        private void ClampSwingArc()
        {
            if (currentSwingPole == null) return;

            Vector2 anchor = currentSwingPole.AnchorPosition;
            Vector2 delta = rb.position - anchor;
            float distance = Mathf.Max(0.05f, delta.magnitude);

            if (distance <= 0.051f)
                delta = Vector2.down * distance;

            float angle = Vector2.SignedAngle(Vector2.down, delta.normalized);
            float clampedAngle = Mathf.Clamp(angle, swingMinAngle, swingMaxAngle);
            Vector2 clampedDir = Quaternion.Euler(0f, 0f, clampedAngle) * Vector2.down;
            Vector2 clampedPosition = anchor + clampedDir * distance;

            if (clampedPosition.y > anchor.y - swingMinBelowAnchor)
            {
                float requiredY = anchor.y - swingMinBelowAnchor;
                float x = clampedPosition.x - anchor.x;
                float maxX = Mathf.Sqrt(Mathf.Max(0f, distance * distance - swingMinBelowAnchor * swingMinBelowAnchor));
                x = Mathf.Clamp(x, -maxX, maxX);
                clampedPosition = new Vector2(anchor.x + x, requiredY);

                Vector2 belowDelta = clampedPosition - anchor;
                if (belowDelta.sqrMagnitude > 0.0001f)
                    clampedPosition = anchor + belowDelta.normalized * distance;
            }

            bool corrected = (clampedPosition - rb.position).sqrMagnitude > 0.0001f;
            if (!corrected) return;

            rb.position = clampedPosition;

            Vector2 radiusDir = (rb.position - anchor).normalized;
            Vector2 tangent = new Vector2(-radiusDir.y, radiusDir.x);
            rb.velocity = Vector2.Dot(rb.velocity, tangent) * tangent;
        }

        private void ExitSwing(bool jumpRelease, float inputXClean = 0f)
        {
            if (!isSwinging) return;

            if (activeSwingMode == SwingAttachmentMode.HandPivot)
            {
                ExitHandPivotSwing(jumpRelease, inputXClean);
                return;
            }

            ExitRopeSwing(jumpRelease, inputXClean);
        }

        private void ExitRopeSwing(bool jumpRelease, float inputXClean)
        {
            if (!isSwinging && (swingJoint == null || !swingJoint.enabled)) return;

            Vector2 ropeAnchor = currentSwingPole != null
                ? currentSwingPole.AnchorPosition
                : rb.position;
            isSwinging = false;
            currentSwingPole = null;

            if (swingJoint != null)
            {
                swingJoint.enabled = false;
                swingJoint.connectedBody = null;
            }

            activeRopeMinDistance = 0.05f;
            activeRopeMaxDistance = 0.05f;

            rb.gravityScale = defaultGravityScale;
            swingCooldownTimer = swingExitCooldown;

            Vector2 keptVelocity = rb.velocity * swingVelocityKeepMultiplier;
            if (jumpRelease)
            {
                int jumpDir = inputXClean != 0f ? (int)Mathf.Sign(inputXClean) : facingDir;
                ApplyFacing(jumpDir);
                Vector2 releaseVelocity = keptVelocity +
                    new Vector2(jumpDir * swingJumpForceX, swingJumpForceY);
                rb.velocity = ApplyRopeTopExitAssist(releaseVelocity, ropeAnchor, jumpDir);
            }
            else
            {
                rb.velocity = keptVelocity;
            }

            if (debugLogs)
                Debug.Log($"[SWING] EXIT mode=Rope jump={jumpRelease} velocity={rb.velocity}");
        }

        private Vector2 ApplyRopeTopExitAssist(
            Vector2 releaseVelocity,
            Vector2 ropeAnchor,
            int exitDirection)
        {
            Vector2 radius = rb.position - ropeAnchor;
            if (radius.sqrMagnitude <= 0.0001f)
                return releaseVelocity;

            float signedAngle = Vector2.SignedAngle(Vector2.down, radius.normalized);
            float angleLimit = signedAngle < 0f
                ? Mathf.Abs(swingMinAngle)
                : Mathf.Abs(swingMaxAngle);
            float arcRatio = angleLimit > 0.01f
                ? Mathf.Clamp01(Mathf.Abs(signedAngle) / angleLimit)
                : 0f;
            float assistStart = Mathf.Clamp01(swingTopExitAssistStartRatio);
            float assist = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(assistStart, 1f, arcRatio));

            int safeDirection = exitDirection < 0 ? -1 : 1;
            float forwardSpeed = releaseVelocity.x * safeDirection;
            float missingSpeed = Mathf.Max(
                0f,
                swingTopExitTargetHorizontalSpeed - forwardSpeed);
            float addedSpeed = Mathf.Min(
                missingSpeed,
                Mathf.Max(0f, swingTopExitMaxAdditionalSpeed)) * assist;
            releaseVelocity.x += safeDirection * addedSpeed;
            return releaseVelocity;
        }

        private void ExitHandPivotSwing(bool jumpRelease, float inputXClean)
        {
            Vector2 keptVelocity = rb.velocity * handPivotVelocityKeepMultiplier;
            int exitDirection = inputXClean != 0f ? (int)Mathf.Sign(inputXClean) : facingDir;

            isSwinging = false;
            isHandPivotRecovering = true;
            currentSwingPole = null;
            handPivotExitFacing = exitDirection;

            if (handPivotJoint != null)
            {
                handPivotJoint.enabled = false;
                handPivotJoint.connectedBody = null;
            }

            if (swingJoint != null)
            {
                swingJoint.enabled = false;
                swingJoint.connectedBody = null;
            }

            rb.bodyType = handPivotCachedBodyType;
            rb.constraints = handPivotCachedConstraints & ~RigidbodyConstraints2D.FreezeRotation;
            rb.gravityScale = defaultGravityScale;
            rb.angularVelocity = 0f;
            swingCooldownTimer = swingExitCooldown;

            rb.velocity = jumpRelease
                ? keptVelocity + new Vector2(exitDirection * handPivotJumpForceX, handPivotJumpForceY)
                : keptVelocity;

            if (debugLogs)
                Debug.Log($"[SWING] EXIT mode=HandPivot jump={jumpRelease} velocity={rb.velocity}");
        }

        private void UpdateHandPivotRecovery()
        {
            input?.CancelCrouchGesture("PlayerController.UpdateHandPivotRecovery");
            input?.ConsumeAttackPressed();
            input?.ConsumeDashPressed();
            input?.ConsumeParryPressed();
            input?.ConsumeJumpPressed();
            input?.ConsumeInteractPressed();
            input?.ClearSwingGrabBuffer();
        }

        private void FixedUpdateHandPivotRecovery()
        {
            rb.angularVelocity = 0f;

            float currentRotation = rb.rotation;
            float nextRotation = Mathf.MoveTowardsAngle(
                currentRotation,
                handPivotUprightRotation,
                handPivotUprightDegreesPerSecond * Time.fixedDeltaTime);

            if (CanRotateHandPivotColliderTo(nextRotation))
                rb.MoveRotation(nextRotation);

            if (Mathf.Abs(Mathf.DeltaAngle(currentRotation, handPivotUprightRotation)) <=
                    handPivotUprightTolerance &&
                CanRotateHandPivotColliderTo(handPivotUprightRotation))
            {
                CompleteHandPivotRecovery();
            }
        }

        private bool CanRotateHandPivotColliderTo(float worldRotation)
        {
            if (capsule == null || !capsule.enabled) return true;

            Vector3 lossyScale = transform.lossyScale;
            Vector2 querySize = new Vector2(
                capsule.size.x * Mathf.Abs(lossyScale.x),
                capsule.size.y * Mathf.Abs(lossyScale.y));
            float clearance = Mathf.Max(0.001f, postureClearanceSkin);
            querySize.x = Mathf.Max(0.01f, querySize.x - clearance * 2f);
            querySize.y = Mathf.Max(0.01f, querySize.y - clearance * 2f);

            Vector2 scaledOffset = new Vector2(
                capsule.offset.x * lossyScale.x,
                capsule.offset.y * lossyScale.y);
            float radians = worldRotation * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            Vector2 rotatedOffset = new Vector2(
                scaledOffset.x * cos - scaledOffset.y * sin,
                scaledOffset.x * sin + scaledOffset.y * cos);
            Vector2 queryCenter = rb.position + rotatedOffset;

            ContactFilter2D filter = new ContactFilter2D();
            filter.SetLayerMask(SolidMask());
            filter.useLayerMask = true;
            filter.useTriggers = false;

            int hitCount = Physics2D.OverlapCapsule(
                queryCenter,
                querySize,
                capsule.direction,
                worldRotation,
                filter,
                postureClearanceHits);

            if (hitCount >= postureClearanceHits.Length)
                return false;

            for (int i = 0; i < hitCount; i++)
            {
                Collider2D hit = postureClearanceHits[i];
                if (hit == null || hit == capsule || hit.isTrigger) continue;
                if (hit.attachedRigidbody == rb) continue;
                if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
                return false;
            }

            return true;
        }

        private void CompleteHandPivotRecovery()
        {
            rb.rotation = handPivotUprightRotation;
            rb.angularVelocity = 0f;
            rb.bodyType = handPivotCachedBodyType;
            rb.constraints = handPivotCachedConstraints;
            rb.gravityScale = defaultGravityScale;

            isHandPivotRecovering = false;
            activeSwingMode = SwingAttachmentMode.Rope;
            ApplyFacing(handPivotExitFacing);
        }

        private void ForceEndSwingForReset()
        {
            bool restoreHandPivotState =
                activeSwingMode == SwingAttachmentMode.HandPivot || isHandPivotRecovering;

            DisableSwingJoints();

            if (restoreHandPivotState)
            {
                rb.bodyType = handPivotCachedBodyType;
                rb.constraints = handPivotCachedConstraints;
                rb.gravityScale = defaultGravityScale;
                rb.rotation = handPivotUprightRotation;
            }
            else
            {
                rb.gravityScale = defaultGravityScale;
            }

            rb.angularVelocity = 0f;
            isSwinging = false;
            isHandPivotRecovering = false;
            currentSwingPole = null;
            activeSwingMode = SwingAttachmentMode.Rope;
        }

        private void DisableSwingJoints()
        {
            if (swingJoint != null)
            {
                swingJoint.enabled = false;
                swingJoint.connectedBody = null;
            }

            if (handPivotJoint != null)
            {
                handPivotJoint.enabled = false;
                handPivotJoint.connectedBody = null;
            }

            activeRopeMinDistance = 0.05f;
            activeRopeMaxDistance = 0.05f;
        }

        private void InvalidateSwingGrabTemporalState()
        {
            unchecked
            {
                swingGrabTemporalVersion++;
            }

            input?.ClearSwingGrabBuffer();
        }

        private static Vector2 GetSwingAnchorVelocity(SwingPole pole, Vector2 anchor)
        {
            return pole != null && pole.ConnectedBody != null
                ? pole.ConnectedBody.GetPointVelocity(anchor)
                : Vector2.zero;
        }

        private IInteractable GetClosestInteractable()
        {
            IInteractable best = null;
            float bestSqrDistance = float.PositiveInfinity;
            Vector3 origin = transform.position;

            for (int i = interactablesInRange.Count - 1; i >= 0; i--)
            {
                IInteractable candidate = interactablesInRange[i];
                UnityEngine.Object candidateObject = candidate as UnityEngine.Object;

                if (candidateObject == null || !candidate.CanInteract(gameObject))
                {
                    interactablesInRange.RemoveAt(i);
                    continue;
                }

                float sqrDistance = (candidate.InteractableTransform.position - origin).sqrMagnitude;
                if (sqrDistance < bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    best = candidate;
                }
            }

            return best;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            IInteractable interactable = GetInteractable(other);
            if (interactable == null || interactablesInRange.Contains(interactable)) return;

            interactablesInRange.Add(interactable);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            IInteractable interactable = GetInteractable(other);
            if (interactable == null) return;

            interactablesInRange.Remove(interactable);

            if (interactable is DialogueInteractable)
                DialogueManager.HideCurrentDialogue();
        }

        private static IInteractable GetInteractable(Collider2D other)
        {
            return other.GetComponent<IInteractable>() ?? other.GetComponentInParent<IInteractable>();
        }

        // --------------------
        // MOVE / PHYSICS
        // --------------------
        private bool HandleCrouchCommand(InputReader.CrouchCommand command)
        {
            if (CanStartSprintSlide(command))
            {
                if (TryStartSprintSlide())
                    return true;
            }

            ApplyNormalCrouchCommand(command);
            return false;
        }

        private void ApplyNormalCrouchCommand(InputReader.CrouchCommand command)
        {
            if (!command.IsValid)
                return;

            if (isGrounded)
            {
                TryApplyPostureCommand(
                    command,
                    "PlayerController.Update:CrouchCommand");
                return;
            }

            TryStandingPostureAndCancelGesture(
                "PlayerController.Update:CrouchPressWhileAirborne");
        }

        private bool CanStartDirectSprintDive()
        {
            return
                isActiveAndEnabled &&
                !externalControlLocked &&
                input != null &&
                input.CurrentPosture == InputReader.PlayerPosture.Standing &&
                isGrounded &&
                sprintActive &&
                input.SprintHeld &&
                !isDashing &&
                !isAttacking &&
                !isHanging &&
                !isLedgeClimbing &&
                !IsSwinging &&
                !isOverheadRailHanging &&
                !IsMoveLockedByParry &&
                sprintSlideDiveState == SprintSlideDiveState.None;
        }

        private bool CanStartSprintSlide(InputReader.CrouchCommand command)
        {
            return
                command.IsValid &&
                command.PressType == InputReader.CrouchPressType.Short &&
                command.OriginalPosture == InputReader.PlayerPosture.Standing &&
                command.TargetPosture == InputReader.PlayerPosture.Crouching &&
                isGrounded &&
                sprintActive &&
                input.SprintHeld &&
                !isDashing &&
                !isAttacking &&
                !isHanging &&
                !IsSwinging &&
                sprintSlideDiveState == SprintSlideDiveState.None;
        }

        private bool TryStartSprintSlide()
        {
            const string source = "PlayerController.TryStartSprintSlide";
            if (!CanChangeColliderProfile(
                    standingColliderProfile,
                    slideColliderProfile,
                    InputReader.PlayerPosture.Crouching,
                    source))
                return false;

            float entryVelocityX = InitializeSprintSlideDiveManeuver();
            sprintSlideDiveState = SprintSlideDiveState.Slide;

            input.ClaimCrouchGesture(source);
            ApplyColliderProfile(slideColliderProfile);
            rb.gravityScale = defaultGravityScale;
            SetHorizontalVelocityWithConveyor(
                sprintSlideDiveDirection * slideCurrentSpeed);

            if (debugLogs)
            {
                Debug.Log(
                    $"[SLIDE/DIVE] ENTER Slide direction={sprintSlideDiveDirection} " +
                    $"sprintEntryVx={entryVelocityX:0.00} slideEntrySpeed={slideCurrentSpeed:0.00} " +
                    $"startX={slideManeuverStartX:0.000}");
            }

            return true;
        }

        private float InitializeSprintSlideDiveManeuver()
        {
            float entryVelocityX = HorizontalVelocityWithoutConveyor();
            sprintSlideDiveDirection = Mathf.Abs(entryVelocityX) > 0.05f
                ? (entryVelocityX < 0f ? -1 : 1)
                : (facingDir < 0 ? -1 : 1);
            ApplyFacing(sprintSlideDiveDirection);

            slideElapsed = 0f;
            slideCurrentSpeed = Mathf.Max(
                Mathf.Abs(entryVelocityX),
                Mathf.Max(0f, slideInitialHorizontalSpeed));
            slideManeuverStartX = rb.position.x;
            slideManeuverSprintEntryVelocityX = entryVelocityX;
            slideManeuverInitialSpeed = slideCurrentSpeed;
            forwardDiveStartX = slideManeuverStartX;
            forwardDiveTakeoffX = slideManeuverStartX;
            hasForwardDiveTakeoffSample = false;
            postDiveSlideLandingX = slideManeuverStartX;
            combinedSlideToTakeoffDistance = 0f;
            combinedDiveAirDistance = 0f;
            diveElapsed = 0f;
            diveHasLeftGround = false;

            if (hasPendingNormalJump)
                animDriver?.CancelJumpStart();
            hasPendingNormalJump = false;
            pendingNormalJumpTimer = 0f;
            normalJumpAnticipationAge = 0f;
            jumpBufferTimer = 0f;
            isWallSliding = false;
            ResetSlopeMovementCarry();
            ClearSprintMomentum();
            DropCarriedObject();

            return entryVelocityX;
        }

        private void UpdateSprintSlideDive()
        {
            DiscardInputsDuringSprintSlideDive();

            if (IsSliding)
            {
                slideElapsed += Time.deltaTime;

                if (slideElapsed >= Mathf.Max(0.05f, slideDuration))
                    TryFinishSlideInCrouch();

                return;
            }

            if (!IsDiving) return;

            diveElapsed += Time.deltaTime;
            UpdateDiveLandingState();
        }

        private void FixedUpdateSprintSlideDive()
        {
            if (IsPostDiveSliding)
            {
                FixedUpdatePostDiveSlide();
                return;
            }

            if (IsSliding)
            {
                slideCurrentSpeed = Mathf.MoveTowards(
                    slideCurrentSpeed,
                    0f,
                    Mathf.Max(0f, slideDeceleration) * Time.fixedDeltaTime);

                if (ShouldUseSlopeMovement())
                {
                    Vector2 tangent = new Vector2(groundNormal.y, -groundNormal.x).normalized;
                    rb.velocity =
                        tangent * (sprintSlideDiveDirection * slideCurrentSpeed) -
                        groundNormal * slopeStickSpeed;
                    appliedConveyorVelocityX = 0f;
                }
                else
                {
                    SetHorizontalVelocityWithConveyor(
                        sprintSlideDiveDirection * slideCurrentSpeed);
                }

                if (!isGrounded)
                    ApplyBetterJumpGravity();
                ApplyMovingPlatformDelta();
                return;
            }

            if (!IsDiving) return;

            UpdateDiveLandingState();
            if (IsDiving)
                ApplyBetterJumpGravity();
        }

        private bool TryStartDirectSprintDive()
        {
            if (!CanStartDirectSprintDive())
                return false;

            const string source = "PlayerController.TryStartDirectSprintDive";
            if (!CanChangeColliderProfile(
                    standingColliderProfile,
                    diveColliderProfile,
                    InputReader.PlayerPosture.Standing,
                    source))
            {
                return false;
            }

            InitializeSprintSlideDiveManeuver();
            return EnterForwardDive("Sprint -> Dive");
        }

        private bool EnterForwardDive(string transition)
        {
            ApplyColliderProfile(diveColliderProfile);
            forwardDiveStartX = rb.position.x;
            hasForwardDiveTakeoffSample = false;
            sprintSlideDiveState = SprintSlideDiveState.Dive;
            diveElapsed = 0f;
            diveHasLeftGround = false;
            ResetDiveRecoveryTracking();
            slopeMovementSuppressedUntilAirborne = true;
            ResetSlopeMovementCarry();
            DetachConveyorForTakeoff();

            float forwardSpeed = Mathf.Max(Mathf.Abs(rb.velocity.x), slideCurrentSpeed);
            rb.gravityScale = defaultGravityScale;
            rb.velocity = new Vector2(
                sprintSlideDiveDirection * forwardSpeed,
                Mathf.Max(0f, rb.velocity.y));
            rb.AddForce(
                new Vector2(
                    sprintSlideDiveDirection * Mathf.Max(0f, diveHorizontalImpulse),
                    Mathf.Max(0f, diveVerticalImpulse)),
                ForceMode2D.Impulse);
            lastDiveAirborneVelocityX = rb.velocity.x;
            preservedDiveAirborneVelocityX = rb.velocity.x;
            hasDiveAirborneVelocitySample = true;

            if (debugLogs)
            {
                float slideToDiveStartDistance = DirectedHorizontalDistance(
                    slideManeuverStartX,
                    forwardDiveStartX,
                    sprintSlideDiveDirection);
                Debug.Log(
                    $"[SLIDE/DIVE] {transition} direction={sprintSlideDiveDirection} velocity={rb.velocity} " +
                    $"slideToDiveStartDx={slideToDiveStartDistance:0.000}");
            }

            return true;
        }

        private bool TryFinishSlideInCrouch()
        {
            const string source = "PlayerController.TryFinishSlideInCrouch";
            InputReader.PlayerPosture exitPosture = InputReader.PlayerPosture.Crouching;
            if (!TryApplyPostureFromProfile(
                    slideColliderProfile,
                    exitPosture,
                    source))
            {
                exitPosture = InputReader.PlayerPosture.LayDown;
                if (!TryApplyPostureFromProfile(
                        slideColliderProfile,
                        exitPosture,
                        source + ":LayDownFallback"))
                    return false;
            }

            int slideDirection = sprintSlideDiveDirection;
            float normalSlideDistance = DirectedHorizontalDistance(
                slideManeuverStartX,
                rb.position.x,
                slideDirection);
            sprintSlideDiveState = SprintSlideDiveState.None;
            slideElapsed = 0f;
            slideCurrentSpeed = 0f;
            sprintSlideDiveDirection = 0;

            if (debugLogs)
            {
                Debug.Log(
                    $"[SLIDE/DIVE DISTANCE] NormalSlide " +
                    $"sprintEntryVx={slideManeuverSprintEntryVelocityX:0.00} " +
                    $"slideEntrySpeed={slideManeuverInitialSpeed:0.00} " +
                    $"slideDx={normalSlideDistance:0.000} exitVx={rb.velocity.x:0.00} " +
                    $"posture={exitPosture}");
            }

            return true;
        }

        private void UpdateDiveLandingState()
        {
            if (!IsDiving) return;

            bool hasFrontContact = TryGetDiveFrontContact(out RaycastHit2D frontHit);
            bool hasPhysicalSupport = TryGetDiveSupportContact(out _);
            bool landed = isGrounded || hasPhysicalSupport;

            if (hasFrontContact && !diveFrontContactWasActive && debugLogs)
            {
                Debug.Log(
                    $"[SLIDE/DIVE] FRONT CONTACT state={sprintSlideDiveState} " +
                    $"grounded={isGrounded} support={hasPhysicalSupport} velocity={rb.velocity} " +
                    $"collider={DescribeCollider2D(frontHit.collider)}");
            }
            diveFrontContactWasActive = hasFrontContact;

            if (hasFrontContact)
            {
                // Never restore velocity sampled before a real frontal collision.
                hasDiveAirborneVelocitySample = false;
                preservedDiveAirborneVelocityX = 0f;
            }

            if (!landed)
            {
                if (!diveHasLeftGround)
                {
                    forwardDiveTakeoffX = rb.position.x;
                    hasForwardDiveTakeoffSample = true;
                    if (debugLogs)
                    {
                        float slideToTakeoffDistance = DirectedHorizontalDistance(
                            slideManeuverStartX,
                            forwardDiveTakeoffX,
                            sprintSlideDiveDirection);
                        Debug.Log(
                            $"[SLIDE/DIVE DISTANCE] DiveTakeoff " +
                            $"slideToTakeoffDx={slideToTakeoffDistance:0.000} " +
                            $"takeoffX={forwardDiveTakeoffX:0.000} vx={rb.velocity.x:0.00}");
                    }
                }
                diveHasLeftGround = true;
                diveProneRejectedSince = -1f;
                if (!hasFrontContact && rb.velocity.y < -0.01f)
                {
                    lastDiveAirborneVelocityX = rb.velocity.x;
                    float forwardVelocity = rb.velocity.x * sprintSlideDiveDirection;
                    if (forwardVelocity < -0.01f)
                    {
                        hasDiveAirborneVelocitySample = false;
                        preservedDiveAirborneVelocityX = 0f;
                    }
                    else if (forwardVelocity > 0.01f)
                    {
                        if (!hasDiveAirborneVelocitySample ||
                            Mathf.Abs(rb.velocity.x) > Mathf.Abs(preservedDiveAirborneVelocityX))
                            preservedDiveAirborneVelocityX = rb.velocity.x;
                        hasDiveAirborneVelocitySample = true;
                    }
                }
                return;
            }

            const float groundedFallbackTime = 0.12f;
            if (!diveHasLeftGround && diveElapsed < groundedFallbackTime) return;

            if (TryFinishDiveInProne(hasFrontContact)) return;

            if (diveProneRejectedSince < 0f)
            {
                diveProneRejectedSince = Time.time;
                if (debugLogs)
                {
                    Debug.LogWarning(
                        $"[SLIDE/DIVE] Dive -> Prone rejected state={sprintSlideDiveState} " +
                        $"grounded={isGrounded} support={hasPhysicalSupport} velocity={rb.velocity} " +
                        $"frontContact={hasFrontContact} reason={lastPostureClearanceFailureReason}");
                }
            }

            if (Time.time - diveProneRejectedSince < Mathf.Max(0f, diveBlockedRecoveryDelay))
                return;

            TryRecoverDiveAfterBlockedLanding(hasFrontContact);
        }

        private bool TryFinishDiveInProne(bool hasFrontContact)
        {
            const string source = "PlayerController.TryFinishDiveInProne";
            int landingDirection = sprintSlideDiveDirection;
            float resolvedVelocityX = rb.velocity.x;
            float preLandingVelocityX = lastDiveAirborneVelocityX;
            float preservedVelocityX = preservedDiveAirborneVelocityX;
            bool canRestoreAirborneVelocity =
                !hasFrontContact &&
                hasDiveAirborneVelocitySample &&
                landingDirection != 0 &&
                Mathf.Sign(preservedVelocityX) == landingDirection &&
                Mathf.Abs(preservedVelocityX) > Mathf.Abs(resolvedVelocityX);
            float landingVelocityX = canRestoreAirborneVelocity
                ? preservedVelocityX
                : resolvedVelocityX;
            if (!TryApplyPostureFromProfile(
                    diveColliderProfile,
                    InputReader.PlayerPosture.LayDown,
                    source,
                    false) &&
                !TryApplyPostureFromProfilePreservingBottom(
                    diveColliderProfile,
                    InputReader.PlayerPosture.LayDown,
                    source,
                    false))
                return false;

            bool keepSliding =
                Mathf.Abs(landingVelocityX) > Mathf.Max(0f, postDiveSlideStopSpeed);
            sprintSlideDiveState = keepSliding
                ? SprintSlideDiveState.ProneSlide
                : SprintSlideDiveState.None;
            slideElapsed = 0f;
            slideCurrentSpeed = 0f;
            diveElapsed = 0f;
            diveHasLeftGround = false;
            sprintSlideDiveDirection = keepSliding
                ? (landingVelocityX < 0f ? -1 : 1)
                : 0;
            slopeMovementSuppressedUntilAirborne = false;
            suppressConveyorUntilAirborne = false;
            rb.gravityScale = defaultGravityScale;
            rb.angularVelocity = 0f;
            postDiveSlideLandingX = rb.position.x;
            float takeoffX = hasForwardDiveTakeoffSample
                ? forwardDiveTakeoffX
                : forwardDiveStartX;
            combinedSlideToTakeoffDistance = DirectedHorizontalDistance(
                slideManeuverStartX,
                takeoffX,
                landingDirection);
            combinedDiveAirDistance = DirectedHorizontalDistance(
                takeoffX,
                postDiveSlideLandingX,
                landingDirection);

            if (keepSliding)
            {
                // Collider changes do not get to discard valid Dive momentum. A frontal
                // contact invalidates the airborne sample above, so walls cannot add speed.
                rb.velocity = new Vector2(landingVelocityX, rb.velocity.y);
                appliedConveyorVelocityX = 0f;
                postDiveSlideCurrentSpeed = Mathf.Abs(landingVelocityX);
                postDiveSlideEntryVelocityX = landingVelocityX;
                postDiveSlideGroundedGraceTimer = Mathf.Max(0f, postDiveSlideGroundedGraceTime);
                postDiveSlideDebugLogTimer = 0f;
            }
            else
            {
                postDiveSlideCurrentSpeed = 0f;
                postDiveSlideEntryVelocityX = 0f;
                rb.velocity = new Vector2(0f, rb.velocity.y);
                appliedConveyorVelocityX = 0f;
            }

            if (debugLogs)
            {
                string preLandingSpeed = hasDiveAirborneVelocitySample
                    ? preLandingVelocityX.ToString("0.00")
                    : "n/a";
                string preservedSpeed = hasDiveAirborneVelocitySample
                    ? preservedVelocityX.ToString("0.00")
                    : "n/a";
                string transition = keepSliding
                    ? "ENTER ProneSlide"
                    : "LAND Dive -> Prone";
                float expectedProneDistance = CalculateExpectedPostDiveSlideDistance(
                    landingVelocityX);
                Debug.Log(
                    $"[SLIDE/DIVE] {transition} preLandingVx={preLandingSpeed} " +
                    $"preservedVx={preservedSpeed} resolvedVx={resolvedVelocityX:0.00} " +
                    $"entryVx={rb.velocity.x:0.00} " +
                    $"restored={canRestoreAirborneVelocity} frontContact={hasFrontContact} " +
                    $"active={keepSliding} decel={postDiveSlideDeceleration:0.00} " +
                    $"expectedProneDx={expectedProneDistance:0.000}");
            }

            if (!keepSliding)
                LogCombinedManeuverDistance(landingDirection, "no-prone-slide", resolvedVelocityX);

            ResetDiveRecoveryTracking();

            return true;
        }

        private bool TryRecoverDiveAfterBlockedLanding(bool hasFrontContact)
        {
            const string source = "PlayerController.TryRecoverDiveAfterBlockedLanding";
            InputReader.PlayerPosture recoveredPosture = InputReader.PlayerPosture.Crouching;
            bool recovered =
                TryApplyPostureFromProfilePreservingBottom(
                    diveColliderProfile,
                    recoveredPosture,
                    source,
                    false) ||
                TryApplyPostureFromProfile(
                    diveColliderProfile,
                    recoveredPosture,
                    source,
                    false);

            if (!recovered)
            {
                recoveredPosture = InputReader.PlayerPosture.Standing;
                recovered =
                    TryApplyPostureFromProfilePreservingBottom(
                        diveColliderProfile,
                        recoveredPosture,
                        source,
                        false) ||
                    TryApplyPostureFromProfile(
                        diveColliderProfile,
                        recoveredPosture,
                        source,
                        false);
            }

            if (!recovered)
            {
                recoveredPosture = InputReader.PlayerPosture.LayDown;
                input.CommitPosture(recoveredPosture, source);
                isUsingDiveColliderRecoveryProfile = true;
            }
            else
            {
                isUsingDiveColliderRecoveryProfile = false;
            }

            sprintSlideDiveState = SprintSlideDiveState.None;
            slideElapsed = 0f;
            slideCurrentSpeed = 0f;
            diveElapsed = 0f;
            diveHasLeftGround = false;
            sprintSlideDiveDirection = 0;
            slopeMovementSuppressedUntilAirborne = false;
            suppressConveyorUntilAirborne = false;
            rb.gravityScale = defaultGravityScale;
            rb.angularVelocity = 0f;
            ResetSlopeMovementCarry();
            ResetDiveRecoveryTracking();

            if (debugLogs)
            {
                Debug.LogWarning(
                    $"[SLIDE/DIVE] SAFE RECOVERY Dive -> {recoveredPosture} " +
                    $"velocity={rb.velocity} frontContact={hasFrontContact} " +
                    $"temporaryDiveCollider={isUsingDiveColliderRecoveryProfile}");
            }

            return true;
        }

        private void TryResolveDiveColliderRecoveryProfile()
        {
            if (!isUsingDiveColliderRecoveryProfile || !isGrounded) return;

            const string source = "PlayerController.TryResolveDiveColliderRecoveryProfile";
            if (!TryApplyPostureFromProfile(
                    diveColliderProfile,
                    InputReader.PlayerPosture.LayDown,
                    source,
                    false) &&
                !TryApplyPostureFromProfilePreservingBottom(
                    diveColliderProfile,
                    InputReader.PlayerPosture.LayDown,
                    source,
                    false))
                return;

            isUsingDiveColliderRecoveryProfile = false;
            if (debugLogs)
                Debug.Log("[SLIDE/DIVE] RECOVERY collider Dive -> Prone completed");
        }

        private bool TryGetDiveSupportContact(out RaycastHit2D supportHit)
        {
            supportHit = default;
            if (capsule == null || !capsule.enabled) return false;

            int standardGroundMask = StandardGroundMask();
            ContactFilter2D filter = new ContactFilter2D();
            filter.SetLayerMask(standardGroundMask | standableActorMask.value);
            filter.useLayerMask = true;
            filter.useTriggers = false;

            int hitCount = capsule.Cast(
                Vector2.down,
                filter,
                groundHits,
                Mathf.Max(0.005f, diveContactProbeDistance));
            float bestNormalY = float.NegativeInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D candidate = groundHits[i];
                if (candidate.collider == null || candidate.collider == capsule) continue;
                if (candidate.collider.isTrigger) continue;
                if (!IsAllowedGroundCollider(candidate.collider, standardGroundMask)) continue;
                if (candidate.normal.y < groundNormalMinY) continue;
                if (candidate.normal.y <= bestNormalY) continue;

                bestNormalY = candidate.normal.y;
                supportHit = candidate;
            }

            return supportHit.collider != null;
        }

        private bool TryGetDiveFrontContact(out RaycastHit2D frontHit)
        {
            frontHit = default;
            if (sprintSlideDiveDirection == 0) return false;

            return TryGetWallHitOnSide(
                sprintSlideDiveDirection,
                Mathf.Max(0.005f, diveContactProbeDistance),
                out frontHit);
        }

        private void ResetDiveRecoveryTracking()
        {
            diveProneRejectedSince = -1f;
            diveFrontContactWasActive = false;
            lastDiveAirborneVelocityX = 0f;
            preservedDiveAirborneVelocityX = 0f;
            hasDiveAirborneVelocitySample = false;
            lastPostureClearanceFailureReason = "none";
        }

        private void FixedUpdatePostDiveSlide()
        {
            bool hasPhysicalSupport = TryGetDiveSupportContact(out RaycastHit2D supportHit);
            bool hasSlideSupport = isGrounded || hasPhysicalSupport;
            if (hasSlideSupport)
            {
                postDiveSlideGroundedGraceTimer = Mathf.Max(0f, postDiveSlideGroundedGraceTime);
            }
            else
            {
                postDiveSlideGroundedGraceTimer = Mathf.Max(
                    0f,
                    postDiveSlideGroundedGraceTimer - Time.fixedDeltaTime);
                ApplyBetterJumpGravity();
                LogPostDiveSlideProgress(false, "ground-grace");

                if (postDiveSlideGroundedGraceTimer <= 0f)
                    FinishPostDiveSlide(false, "ground-contact-lost");
                return;
            }

            float stopSpeed = Mathf.Max(0f, postDiveSlideStopSpeed);
            if (postDiveSlideCurrentSpeed <= stopSpeed)
            {
                FinishPostDiveSlide(true, "speed-at-or-below-threshold");
                ApplyMovingPlatformDelta();
                return;
            }

            if (TryGetDiveFrontContact(out RaycastHit2D frontHit))
            {
                FinishPostDiveSlide(
                    true,
                    $"front-contact:{DescribeCollider2D(frontHit.collider)}");
                return;
            }

            float physicalForwardSpeed =
                HorizontalVelocityWithoutConveyor() * sprintSlideDiveDirection;
            if (physicalForwardSpeed < -stopSpeed)
            {
                FinishPostDiveSlide(false, "velocity-reversed");
                return;
            }

            // Natural acceleration in the current direction is allowed. Solver reductions
            // from supporting contacts do not become extra, unconfigured deceleration.
            postDiveSlideCurrentSpeed = Mathf.Max(
                postDiveSlideCurrentSpeed,
                Mathf.Max(0f, physicalForwardSpeed));
            postDiveSlideCurrentSpeed = Mathf.MoveTowards(
                postDiveSlideCurrentSpeed,
                0f,
                Mathf.Max(0.01f, postDiveSlideDeceleration) * Time.fixedDeltaTime);

            if (postDiveSlideCurrentSpeed <= stopSpeed)
            {
                FinishPostDiveSlide(true, "decelerated-to-threshold");
                ApplyMovingPlatformDelta();
                return;
            }

            float nextVelocityX = sprintSlideDiveDirection * postDiveSlideCurrentSpeed;

            Vector2 slideGroundNormal = isGrounded
                ? groundNormal
                : supportHit.normal.normalized;
            bool useSlopeMovement =
                slideGroundNormal.y >= groundNormalMinY &&
                slideGroundNormal.y < SlopeNormalMaxY &&
                !slopeMovementSuppressedUntilAirborne;
            if (useSlopeMovement)
            {
                Vector2 tangent = new Vector2(slideGroundNormal.y, -slideGroundNormal.x).normalized;
                float surfaceSpeed = nextVelocityX / tangent.x;
                Vector2 slopeVelocity =
                    tangent * surfaceSpeed - slideGroundNormal * slopeStickSpeed;
                slopeVelocity.x = nextVelocityX;
                rb.velocity = slopeVelocity;
                appliedConveyorVelocityX = 0f;
            }
            else
            {
                SetHorizontalVelocityWithConveyor(nextVelocityX);
            }

            LogPostDiveSlideProgress(hasSlideSupport, "sliding");
            ApplyMovingPlatformDelta();
        }

        private void LogPostDiveSlideProgress(bool hasSupport, string phase)
        {
            if (!debugLogs) return;

            postDiveSlideDebugLogTimer -= Time.fixedDeltaTime;
            if (postDiveSlideDebugLogTimer > 0f) return;

            postDiveSlideDebugLogTimer = 0.25f;
            Debug.Log(
                $"[SLIDE/DIVE] ProneSlide phase={phase} vx={rb.velocity.x:0.00} " +
                $"controlledSpeed={postDiveSlideCurrentSpeed:0.00} " +
                $"grounded={isGrounded} support={hasSupport} " +
                $"groundGrace={postDiveSlideGroundedGraceTimer:0.000}");
        }

        private void FinishPostDiveSlide(bool stopHorizontal, string reason)
        {
            int slideDirection = sprintSlideDiveDirection;
            float velocityBeforeExitX = rb.velocity.x;
            sprintSlideDiveState = SprintSlideDiveState.None;
            sprintSlideDiveDirection = 0;
            postDiveSlideCurrentSpeed = 0f;
            postDiveSlideGroundedGraceTimer = 0f;
            postDiveSlideDebugLogTimer = 0f;
            ResetSlopeMovementCarry();

            if (stopHorizontal)
                SetHorizontalVelocityWithConveyor(0f);

            if (debugLogs)
            {
                Debug.Log(
                    $"[SLIDE/DIVE] EXIT ProneSlide reason={reason} " +
                    $"preExitVx={velocityBeforeExitX:0.00} exitVx={rb.velocity.x:0.00} " +
                    $"grounded={isGrounded}");
            }

            LogCombinedManeuverDistance(slideDirection, reason, velocityBeforeExitX);
            postDiveSlideEntryVelocityX = 0f;
        }

        private void LogCombinedManeuverDistance(
            int direction,
            string reason,
            float velocityBeforeExitX)
        {
            if (!debugLogs) return;

            float proneSlideDistance = DirectedHorizontalDistance(
                postDiveSlideLandingX,
                rb.position.x,
                direction);
            float totalDistance = DirectedHorizontalDistance(
                slideManeuverStartX,
                rb.position.x,
                direction);
            Debug.Log(
                $"[SLIDE/DIVE DISTANCE] Combined reason={reason} " +
                $"sprintEntryVx={slideManeuverSprintEntryVelocityX:0.00} " +
                $"slideEntrySpeed={slideManeuverInitialSpeed:0.00} " +
                $"slideToTakeoffDx={combinedSlideToTakeoffDistance:0.000} " +
                $"diveAirDx={combinedDiveAirDistance:0.000} " +
                $"proneSlideDx={proneSlideDistance:0.000} " +
                $"expectedProneDx={CalculateExpectedPostDiveSlideDistance(postDiveSlideEntryVelocityX):0.000} " +
                $"totalDx={totalDistance:0.000} " +
                $"proneEntryVx={postDiveSlideEntryVelocityX:0.00} " +
                $"pronePreExitVx={velocityBeforeExitX:0.00} exitVx={rb.velocity.x:0.00}");
        }

        private float CalculateExpectedPostDiveSlideDistance(float entryVelocityX)
        {
            float entrySpeed = Mathf.Abs(entryVelocityX);
            float stopSpeed = Mathf.Min(entrySpeed, Mathf.Max(0f, postDiveSlideStopSpeed));
            float deceleration = Mathf.Max(0.01f, postDiveSlideDeceleration);
            return Mathf.Max(
                0f,
                (entrySpeed * entrySpeed - stopSpeed * stopSpeed) / (2f * deceleration));
        }

        private static float DirectedHorizontalDistance(float startX, float endX, int direction)
        {
            int safeDirection = direction < 0 ? -1 : 1;
            return (endX - startX) * safeDirection;
        }

        private void DiscardInputsDuringSprintSlideDive()
        {
            input.ConsumeJumpPressed();
            input.ConsumeAttackPressed();
            input.ConsumeDashPressed();
            input.ConsumeParryPressed();
            input.ConsumeInteractPressed();
            input.ClearSwingGrabBuffer();
        }

        private void CancelSprintSlideDiveForReset()
        {
            sprintSlideDiveState = SprintSlideDiveState.None;
            slideElapsed = 0f;
            slideCurrentSpeed = 0f;
            diveElapsed = 0f;
            diveHasLeftGround = false;
            sprintSlideDiveDirection = 0;
            postDiveSlideCurrentSpeed = 0f;
            postDiveSlideEntryVelocityX = 0f;
            postDiveSlideGroundedGraceTimer = 0f;
            postDiveSlideDebugLogTimer = 0f;
            ResetDiveRecoveryTracking();
        }

        private void UpdateSprintTimers(float deltaTime)
        {
            if (isGrounded)
            {
                sprintMomentumTimer = Mathf.Max(0f, sprintMomentumTimer - deltaTime);
                sprintLandingMomentumTimer = Mathf.Max(0f, sprintLandingMomentumTimer - deltaTime);
            }

            if (sprintActive)
            {
                sprintRegenDelayTimer = Mathf.Max(0f, sprintRegenDelayTimer - deltaTime);
                return;
            }

            sprintRegenDelayTimer = Mathf.Max(0f, sprintRegenDelayTimer - deltaTime);
            if (sprintRegenDelayTimer > 0f) return;

            float maxDuration = Mathf.Max(0f, sprintMaxDuration);
            float rechargeTime = Mathf.Max(0.01f, sprintFullRechargeTime);
            sprintRemaining = Mathf.MoveTowards(
                sprintRemaining,
                maxDuration,
                maxDuration / rechargeTime * deltaTime
            );
        }

        private void UpdateSprintState()
        {
            bool justLanded = !sprintWasGrounded && isGrounded;
            bool wasActive = sprintActive;

            bool hasHorizontalRequest = Mathf.Abs(input.MoveInput.x) > 0.01f;
            bool canSprint =
                isGrounded &&
                !IsCrouching &&
                !IsLayDown &&
                !IsStealthWalking &&
                !isWallSliding &&
                !IsMoveLockedByParry &&
                hasHorizontalRequest &&
                input.SprintHeld &&
                !sprintExhaustedUntilRelease;

            float minimumActivation = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            bool hasReserve = sprintRemaining > minimumActivation;
            sprintActive = canSprint && hasReserve;

            if (!input.SprintHeld)
                sprintExhaustedUntilRelease = false;

            if (sprintActive)
            {
                sprintRemaining = Mathf.Max(0f, sprintRemaining - Time.fixedDeltaTime);
                sprintRegenDelayTimer = Mathf.Max(0f, sprintRegenDelay);

                if (sprintRemaining <= 0.0001f)
                {
                    sprintRemaining = 0f;
                    sprintActive = false;
                    sprintExhaustedUntilRelease = true;
                }
            }

            if (wasActive && !sprintActive)
                CaptureSprintMomentum();

            if (justLanded && HasSprintMomentum())
                sprintLandingMomentumTimer = Mathf.Max(
                    sprintLandingMomentumTimer,
                    Mathf.Max(0f, sprintLandingMomentumDuration)
                );

            sprintWasGrounded = isGrounded;
        }

        private void CaptureSprintMomentum()
        {
            float speed = Mathf.Abs(HorizontalVelocityWithoutConveyor());
            if (speed <= Mathf.Max(GetEffectiveRunSpeed(), 0f) + 0.01f)
                return;

            sprintMomentumSpeed = speed;
            sprintMomentumDirection = rb.velocity.x >= 0f ? 1 : -1;
            sprintMomentumTimer = Mathf.Max(
                sprintMomentumTimer,
                Mathf.Max(0f, sprintMomentumDuration)
            );
        }

        private bool HasSprintMomentum()
        {
            return sprintMomentumDirection != 0 &&
                (sprintMomentumTimer > 0f || sprintLandingMomentumTimer > 0f);
        }

        private float ApplySprintMomentumTarget(float inputX, float targetSpeed)
        {
            if (!HasSprintMomentum() || IsCrouching || IsLayDown || IsStealthWalking)
                return targetSpeed;

            bool opposingInput =
                Mathf.Abs(inputX) > 0.01f &&
                Mathf.Sign(inputX) != sprintMomentumDirection;
            if (opposingInput)
                return targetSpeed;

            float duration = Mathf.Max(
                0.01f,
                Mathf.Max(sprintMomentumDuration, sprintLandingMomentumDuration)
            );
            float remaining = Mathf.Max(sprintMomentumTimer, sprintLandingMomentumTimer);
            float progress = Mathf.Clamp01(remaining / duration);
            float normalMagnitude = Mathf.Abs(targetSpeed);
            float momentumMagnitude = Mathf.Lerp(normalMagnitude, sprintMomentumSpeed, progress);
            if (Mathf.Abs(inputX) <= 0.01f)
                normalMagnitude = 0f;

            momentumMagnitude = Mathf.Max(normalMagnitude, momentumMagnitude);
            return sprintMomentumDirection * momentumMagnitude;
        }

        private void UpdateSprintMomentumFromVelocity(float horizontalVelocity)
        {
            if (!HasSprintMomentum()) return;

            if (Mathf.Abs(horizontalVelocity) <= 0.05f ||
                Mathf.Sign(horizontalVelocity) != sprintMomentumDirection)
            {
                ClearSprintResidualMomentum();
                return;
            }

            sprintMomentumSpeed = Mathf.Abs(horizontalVelocity);
        }

        private void ResetSprintState(bool refill)
        {
            sprintActive = false;
            sprintExhaustedUntilRelease = false;
            sprintRegenDelayTimer = 0f;
            sprintWasGrounded = false;
            sprintMomentumTimer = 0f;
            sprintLandingMomentumTimer = 0f;
            sprintMomentumSpeed = 0f;
            sprintMomentumDirection = 0;
            sprintRemaining = refill ? Mathf.Max(0f, sprintMaxDuration) : 0f;
        }

        private void ClearSprintMomentum()
        {
            sprintActive = false;
            ClearSprintResidualMomentum();
        }

        private void ClearSprintResidualMomentum()
        {
            sprintMomentumTimer = 0f;
            sprintLandingMomentumTimer = 0f;
            sprintMomentumSpeed = 0f;
            sprintMomentumDirection = 0;
        }

        private bool IsStealthWalkRequested()
        {
            if (input == null)
                return false;
            if (input.StealthWalkHeld)
                return true;
            if (!input.MoveInputIsGamepad)
                return false;

            float moveMagnitude = input.MoveMagnitude01;
            return moveMagnitude > 0f &&
                   moveMagnitude <= Mathf.Clamp01(analogStealthWalkMaxMagnitude);
        }

        private void Move()
        {
            // Preserve the exact takeoff velocity until the ground probe confirms airborne.
            if (suppressConveyorUntilAirborne && isGrounded)
                return;

            float inputX = Mathf.Clamp(input.MoveInput.x, -1f, 1f);
            if (isWallSliding)
                inputX = 0f;

            if (isGrounded)
                ClearHorizontalAirCurrentMomentum();

            if (wallJumpLockTimer > 0f)
            {
                wallJumpLockTimer -= Time.fixedDeltaTime;
                if (inputX != 0f && lastWallJumpSide != 0 && Mathf.Sign(inputX) == lastWallJumpSide)
                    inputX = 0f;
            }

            float stanceMult = 1f;
            if (IsLayDown) stanceMult = layDownSpeedMultiplier;
            else if (IsCrouching) stanceMult = crouchSpeedMultiplier;
            else if (IsStealthWalking) stanceMult = stealthWalkSpeedMultiplier;
            else if (sprintActive) stanceMult = sprintSpeedMultiplier;
            if (IsCarrying) stanceMult *= carryingMoveSpeedMultiplier;

            float targetSpeed = inputX * GetEffectiveRunSpeed() * stanceMult;
            targetSpeed = ApplySprintMomentumTarget(inputX, targetSpeed);
            if (hasPendingNormalJump)
                targetSpeed *= jumpAnticipationMoveSpeedMultiplier;

            inputX = ApplyHorizontalAirCurrentControl(inputX);
            targetSpeed = ApplyHorizontalAirCurrentControl(targetSpeed);

            float accel = isGrounded ? stats.groundAcceleration : stats.airAcceleration;
            float decel = isGrounded ? stats.groundDeceleration : stats.airDeceleration;
            float turnResponsiveness = stats.turnAccelerationMultiplier;

            if (isGrounded && IsSlippery)
            {
                accel *= slipperyGroundAccelerationMultiplier;
                decel *= slipperyGroundDecelerationMultiplier;
                turnResponsiveness *= slipperyTurnResponsivenessMultiplier;
            }

            float rate = Mathf.Abs(inputX) > 0.01f ? accel : decel;

            bool useSlopeMovement = ShouldUseSlopeMovement();

            if (useSlopeMovement)
            {
                Vector2 tangent = new Vector2(groundNormal.y, -groundNormal.x).normalized;
                bool enteringSlope = !wasUsingSlopeMovementLastMove;
                if (enteringSlope)
                    slopeHorizontalSpeed = rb.velocity.x;

                float targetHorizontalSpeed = targetSpeed;

                float horizontalRate = rate;

                bool changingSlopeDirection =
                    Mathf.Abs(targetHorizontalSpeed) > 0.01f &&
                    Mathf.Sign(targetHorizontalSpeed) != Mathf.Sign(slopeHorizontalSpeed) &&
                    Mathf.Abs(slopeHorizontalSpeed) > 0.1f;

                if (changingSlopeDirection)
                    horizontalRate *= turnResponsiveness;

                slopeHorizontalSpeed = Mathf.MoveTowards(
                    slopeHorizontalSpeed,
                    targetHorizontalSpeed,
                    horizontalRate * Time.fixedDeltaTime
                );

                if (Mathf.Abs(inputX) < 0.01f && Mathf.Abs(slopeHorizontalSpeed) < 0.02f)
                    slopeHorizontalSpeed = 0f;

                float newHorizontalSpeed = slopeHorizontalSpeed;
                float newSurfaceSpeed = newHorizontalSpeed / tangent.x;

                Vector2 baseGravity = Physics2D.gravity * stats.gravityScale;
                float gravityAlongSlope = Vector2.Dot(baseGravity, tangent);
                newSurfaceSpeed -= gravityAlongSlope * Time.fixedDeltaTime;

                rb.velocity =
                    tangent * newSurfaceSpeed -
                    groundNormal * slopeStickSpeed;
                UpdateSprintMomentumFromVelocity(rb.velocity.x);
                wasUsingSlopeMovementLastMove = true;
                return;
            }

            if (wasUsingSlopeMovementLastMove)
            {
                rb.velocity = new Vector2(
                    slopeHorizontalSpeed,
                    rb.velocity.y
                );
                appliedConveyorVelocityX = 0f;
            }

            wasUsingSlopeMovementLastMove = false;
            slopeHorizontalSpeed = 0f;

            float currentVelocityX = HorizontalVelocityWithoutConveyor();
            targetSpeed = PreserveWithFlowAirCurrentMomentum(
                currentVelocityX,
                targetSpeed);
            bool preserveReleasedAirCurrentVelocity =
                ApplyReleasedHorizontalAirCurrentMomentum(
                    currentVelocityX,
                    targetSpeed,
                    out bool useAirCurrentMomentumDeceleration);
            if (useAirCurrentMomentumDeceleration)
                rate = stats.airDeceleration;

            bool changingDirection =
                Mathf.Abs(targetSpeed) > 0.01f &&
                Mathf.Sign(targetSpeed) != Mathf.Sign(currentVelocityX) &&
                Mathf.Abs(currentVelocityX) > 0.1f;

            if (changingDirection && !useAirCurrentMomentumDeceleration)
                rate *= turnResponsiveness;

            bool preserveAirCurrentMomentum =
                horizontalAirCurrentInfluence &&
                CanUseUpdraftCapeMotion &&
                Mathf.Abs(inputX) < 0.01f &&
                Mathf.Abs(targetSpeed) < 0.01f;
            float newVelX = preserveAirCurrentMomentum ||
                preserveReleasedAirCurrentVelocity
                ? currentVelocityX
                : Mathf.MoveTowards(
                    currentVelocityX,
                    targetSpeed,
                    rate * Time.fixedDeltaTime);

            if (isGrounded && Mathf.Abs(inputX) < 0.01f && Mathf.Abs(newVelX) < 0.02f)
                newVelX = 0f;

            SetHorizontalVelocityWithConveyor(newVelX);
            UpdateSprintMomentumFromVelocity(newVelX);
        }

        private void ApplyFacing(int dir)
        {
            if (dir == 0) return;

            facingDir = dir;

            Vector3 s = visualRoot.localScale;
            s.x = Mathf.Abs(s.x) * dir;
            visualRoot.localScale = s;

            if (attackPoint != null)
            {
                Vector3 lp = attackPoint.localPosition;
                lp.x = attackPointLocalXAbs * dir;
                attackPoint.localPosition = lp;
            }

            if (wallCheck != null)
            {
                Vector3 lp = wallCheck.localPosition;
                lp.x = wallCheckLocalXAbs * dir;
                wallCheck.localPosition = lp;
            }
        }

        private void Jump()
        {
            float jumpForce = GetEffectiveJumpForce() * (IsCarrying ? carryingJumpForceMultiplier : 1f);
            upwardAirCurrentMomentumProtected = false;
            downHitBounceAscentProtected = false;
            DetachConveyorForTakeoff();
            slopeMovementSuppressedUntilAirborne = true;
            rb.velocity = new Vector2(rb.velocity.x, jumpForce);
        }

        private bool TryUseDoubleJump()
        {
            if (!doubleJumpEquipped || !doubleJumpAvailable || isGrounded ||
                coyoteTimer > 0f || hasPendingNormalJump)
            {
                return false;
            }

            ApplyJumpInputFacing();
            animDriver?.TriggerJumpStart();
            Jump();
            doubleJumpAvailable = false;
            return true;
        }

        private float GetEffectiveRunSpeed()
        {
            if (progressionStats == null)
                progressionStats = GetComponent<PlayerStatsRuntime>();

            float fallback = stats != null ? stats.maxRunSpeed : 0f;
            return progressionStats != null
                ? progressionStats.GetEffectiveRunSpeed(fallback)
                : fallback;
        }

        private float GetEffectiveJumpForce()
        {
            if (progressionStats == null)
                progressionStats = GetComponent<PlayerStatsRuntime>();

            float fallback = stats != null ? stats.jumpForce : 0f;
            return progressionStats != null
                ? progressionStats.GetEffectiveJumpForce(fallback)
                : fallback;
        }

        private void StartPendingNormalJump()
        {
            ApplyJumpInputFacing();
            animDriver?.TriggerJumpStart();

            if (jumpStartDelay <= 0f)
            {
                Jump();
                NormalJumpPerformed?.Invoke();
                return;
            }

            hasPendingNormalJump = true;
            pendingNormalJumpTimer = jumpStartDelay;
            normalJumpAnticipationAge = 0f;
        }

        private void ApplyJumpInputFacing()
        {
            if (input == null || IsMoveLockedByParry) return;

            float inputX = Mathf.Clamp(input.MoveInput.x, -1f, 1f);
            if (Mathf.Abs(inputX) >= facingInputDeadzone)
                ApplyFacing((int)Mathf.Sign(inputX));
        }

        private void UpdatePendingNormalJump()
        {
            if (!hasPendingNormalJump) return;

            normalJumpAnticipationAge += Time.deltaTime;
            if (!isGrounded)
            {
                hasPendingNormalJump = false;
                pendingNormalJumpTimer = 0f;
                animDriver?.CancelJumpStart();
                return;
            }

            if (normalJumpAnticipationAge > 0f && input != null && !input.JumpHeld)
            {
                hasPendingNormalJump = false;
                pendingNormalJumpTimer = 0f;
                animDriver?.CancelJumpStart();
                return;
            }

            pendingNormalJumpTimer -= Time.deltaTime;
            if (pendingNormalJumpTimer > 0f) return;

            hasPendingNormalJump = false;
            Jump();
            NormalJumpPerformed?.Invoke();
        }

        private void ApplyBetterJumpGravity()
        {
            if (ShouldUseSlopeMovement())
            {
                rb.gravityScale = stats.gravityScale;
                return;
            }

            float vy = rb.velocity.y;

            if (vy < stats.maxFallSpeed)
                rb.velocity = new Vector2(rb.velocity.x, stats.maxFallSpeed);

            if (wallSlideExitFastFallTimer > 0f)
            {
                wallSlideExitFastFallTimer = Mathf.Max(0f, wallSlideExitFastFallTimer - Time.fixedDeltaTime);
                float fastFallY = Mathf.Max(-Mathf.Abs(wallSlideExitFastFallSpeed), stats.maxFallSpeed);
                if (rb.velocity.y > fastFallY)
                    rb.velocity = new Vector2(rb.velocity.x, fastFallY);
                rb.gravityScale = stats.gravityScale * stats.fallGravityMultiplier;
                return;
            }

            if (vy < -0.01f)
            {
                rb.gravityScale = stats.gravityScale * stats.fallGravityMultiplier;
                return;
            }

            if (Mathf.Abs(vy) < stats.apexThreshold)
            {
                rb.gravityScale = stats.gravityScale * stats.apexGravityMultiplier;
                return;
            }

            rb.gravityScale = stats.gravityScale;
        }

        private float ApplyHorizontalAirCurrentControl(float horizontalValue)
        {
            if (!horizontalAirCurrentInfluence ||
                !CanUseUpdraftCapeMotion ||
                horizontalValue * horizontalAirCurrentDirectionX >= -0.01f)
            {
                return horizontalValue;
            }

            return horizontalValue * horizontalAirCurrentAgainstFlowControl;
        }

        private float PreserveWithFlowAirCurrentMomentum(
            float currentVelocityX,
            float targetVelocityX)
        {
            if (!horizontalAirCurrentInfluence || !CanUseUpdraftCapeMotion)
                return targetVelocityX;

            float currentAlongFlow =
                currentVelocityX * horizontalAirCurrentDirectionX;
            float targetAlongFlow =
                targetVelocityX * horizontalAirCurrentDirectionX;
            return currentAlongFlow > 0f &&
                targetAlongFlow >= -0.01f &&
                currentAlongFlow > targetAlongFlow
                    ? currentVelocityX
                    : targetVelocityX;
        }

        private bool ApplyReleasedHorizontalAirCurrentMomentum(
            float currentVelocityX,
            float targetVelocityX,
            out bool useAirDeceleration)
        {
            useAirDeceleration = false;
            if (!horizontalAirCurrentMomentumActive)
                return false;

            float currentAlongMomentum =
                currentVelocityX * horizontalAirCurrentMomentumDirectionX;
            if (isGrounded || currentAlongMomentum <= 0.01f)
            {
                ClearHorizontalAirCurrentMomentum();
                return false;
            }

            float targetAlongMomentum =
                targetVelocityX * horizontalAirCurrentMomentumDirectionX;
            if (targetAlongMomentum >= currentAlongMomentum - 0.01f)
            {
                ClearHorizontalAirCurrentMomentum();
                return false;
            }

            // Wind momentum is external to locomotion. Give the handoff one fixed
            // step with no locomotion write, then bleed only the surplus with the
            // configured airborne deceleration instead of snapping toward run speed.
            useAirDeceleration = true;
            if (horizontalAirCurrentMomentumGraceFixedSteps <= 0)
                return false;

            horizontalAirCurrentMomentumGraceFixedSteps--;
            return true;
        }

        private void CaptureHorizontalAirCurrentMomentum()
        {
            if (rb == null || isGrounded)
            {
                ClearHorizontalAirCurrentMomentum();
                return;
            }

            float velocityX = HorizontalVelocityWithoutConveyor();
            if (Mathf.Abs(velocityX) <= 0.01f)
            {
                ClearHorizontalAirCurrentMomentum();
                return;
            }

            horizontalAirCurrentMomentumActive = true;
            horizontalAirCurrentMomentumDirectionX = Mathf.Sign(velocityX);
            horizontalAirCurrentMomentumGraceFixedSteps = 1;
        }

        private void ClearHorizontalAirCurrentMomentum()
        {
            horizontalAirCurrentMomentumActive = false;
            horizontalAirCurrentMomentumDirectionX = 0f;
            horizontalAirCurrentMomentumGraceFixedSteps = 0;
        }

        // --------------------
        // DASH
        // --------------------
        private void TryStartDash()
        {
            if (IsSwinging || isOverheadRailHanging || IsSliding || IsDiving) return;
            if (IsCrouching || IsLayDown) return;
            if (isDashing) return;
            if (dashCooldownTimer > 0f) return;
            if (!CanSpendDashStamina()) return;

            if (!isGrounded)
            {
                if (!stats.allowAirDash) return;
                if (airDashesRemaining <= 0) return;
                airDashesRemaining--;
            }

            SpendDashStamina();
            StartDash();
        }

        private void StartDash()
        {
            ClearSprintMomentum();
            dashStartedGrounded =
                isGrounded &&
                !slopeMovementSuppressedUntilAirborne;
            dashHorizontalSign = facingDir < 0 ? -1 : 1;
            dashFollowingSlope = false;
            ResetSlopeMovementCarry();

            isDashing = true;
            dashTimer = stats.dashDuration;
            dashCooldownTimer = stats.dashCooldown;

            rb.gravityScale = 0f;
            SetHorizontalVelocityWithConveyor(
                dashHorizontalSign * stats.dashSpeed);
            rb.velocity = new Vector2(rb.velocity.x, 0f);
        }

        private void UpdateDash()
        {
            dashTimer -= Time.fixedDeltaTime;
            if (dashTimer <= 0f)
            {
                EndDash();
                return;
            }

            if (!dashStartedGrounded || slopeMovementSuppressedUntilAirborne)
                return;

            CheckGround();

            Vector2 slopeNormal;
            bool hasValidSlope = TryGetCurrentDashSlope(out slopeNormal);
            if (!hasValidSlope)
                hasValidSlope = TryProbeDashSlope(out slopeNormal);

            if (hasValidSlope)
            {
                Vector2 tangent = new Vector2(slopeNormal.y, -slopeNormal.x).normalized;
                Vector2 signedTangent = tangent * dashHorizontalSign;

                rb.velocity =
                    signedTangent * stats.dashSpeed -
                    slopeNormal * slopeStickSpeed;
                appliedConveyorVelocityX = 0f;
                dashFollowingSlope = true;
                return;
            }

            if (!dashFollowingSlope)
                return;

            SetHorizontalVelocityWithConveyor(
                dashHorizontalSign * stats.dashSpeed);
            rb.velocity = new Vector2(rb.velocity.x, 0f);
            dashFollowingSlope = false;
        }

        private void EndDash()
        {
            isDashing = false;
            rb.gravityScale = defaultGravityScale;
            dashStartedGrounded = false;
            dashFollowingSlope = false;
            ResetSlopeMovementCarry();
        }

        private void ResetSlopeMovementCarry()
        {
            wasUsingSlopeMovementLastMove = false;
            slopeHorizontalSpeed = 0f;
        }

        private bool IsValidDashSlopeNormal(Vector2 normal)
        {
            return
                normal.y >= groundNormalMinY &&
                normal.y < SlopeNormalMaxY;
        }

        private bool TryGetCurrentDashSlope(out Vector2 normal)
        {
            normal = groundNormal;
            return
                isGrounded &&
                currentGroundPlatform == null &&
                currentGroundStandableSurface == null &&
                currentGroundConveyor == null &&
                currentGroundWoodWorkpiece == null &&
                IsValidDashSlopeNormal(normal);
        }

        private bool TryProbeDashSlope(out Vector2 normal)
        {
            normal = Vector2.up;
            if (groundCheck == null) return false;

            int mask = groundLayer.value != 0 ? groundLayer.value : SolidMask();
            Vector2 size = new Vector2(
                Mathf.Max(0.01f, groundCheckBoxSize.x),
                Mathf.Max(0.01f, groundCheckBoxSize.y)
            );
            Vector2 origin = (Vector2)groundCheck.position + Vector2.up * (size.y * 0.5f);
            Vector2 direction = Vector2.right * dashHorizontalSign;
            float castDistance = stats.dashSpeed * Time.fixedDeltaTime + groundCheckDistance;

            int hitCount = Physics2D.BoxCastNonAlloc(
                origin,
                size,
                0f,
                direction,
                groundHits,
                castDistance,
                mask
            );

            RaycastHit2D closestHit = default;
            float closestDistance = float.PositiveInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D candidate = groundHits[i];
                if (candidate.collider == null) continue;
                if (candidate.collider.isTrigger) continue;
                if (candidate.collider.GetComponentInParent<HorizontalMovingPlatform>() != null) continue;
                if (candidate.collider.GetComponentInParent<ConveyorBelt>() != null) continue;
                if (candidate.collider.GetComponentInParent<WoodWorkpiece>() != null) continue;
                if (!IsValidDashSlopeNormal(candidate.normal)) continue;
                if (candidate.distance >= closestDistance) continue;

                closestDistance = candidate.distance;
                closestHit = candidate;
            }

            if (closestHit.collider == null) return false;

            normal = closestHit.normal.normalized;
            return true;
        }

        // --------------------
        // WALL
        // --------------------
        private void HandleWallSlide()
        {
            float inputX = input != null ? Mathf.Clamp(input.MoveInput.x, -1f, 1f) : 0f;
            if (Mathf.Abs(inputX) < wallInputDeadzone) inputX = 0f;
            float inputY = input != null ? Mathf.Clamp(input.MoveInput.y, -1f, 1f) : 0f;
            if (Mathf.Abs(inputY) < wallInputDeadzone) inputY = 0f;

            if (suppressWallSlideThisFixed)
            {
                isWallSliding = false;
                LogWallSlideBlocked("suppressWallSlideThisFixed", inputX);
                return;
            }

            if (suppressWallWhileDownHeld)
            {
                isWallSliding = false;
                LogWallSlideBlocked("suppressWallWhileDownHeld", inputX);
                return;
            }

            bool wasWallSliding = isWallSliding;
            isWallSliding = false;

            if (isGrounded)
            {
                LogWallSlideBlocked("isGrounded", inputX);
                return;
            }

            if (rb.velocity.y >= 0f)
            {
                LogWallSlideBlocked("vy >= 0", inputX);
                return;
            }

            if (!isTouchingWall) return;

            int slideSide = wallSide;
            if (slideSide == 0)
            {
                LogWallSlideBlocked("wallSide == 0", inputX);
                return;
            }

            Collider2D slideWallCollider = GetWallColliderOnSide(slideSide);
            bool sameWallClimbBlocked = IsSameWallClimbBlocked(slideSide, slideWallCollider);
            if (sameWallClimbBlocked)
            {
                LogWallSlideBlocked("same wall climb blocked", inputX);
                return;
            }

            bool exitingDown = inputY < 0f;
            if (wasWallSliding && exitingDown)
            {
                StartWallSlideExitFastFall();
                LogWallSlideBlocked("voluntary exit down", inputX);
                return;
            }

            isWallSliding = true;
            ApplyFacing(slideSide);
            ApplyWallSlideVelocity();
        }

        private void StartWallSlideExitFastFall()
        {
            wallSlideExitFastFallTimer = wallSlideExitFastFallTime;
            float fastFallY = Mathf.Max(-Mathf.Abs(wallSlideExitFastFallSpeed), stats.maxFallSpeed);
            if (rb.velocity.y > fastFallY)
                rb.velocity = new Vector2(rb.velocity.x, fastFallY);
        }

        private void ApplyWallSlideVelocity()
        {
            float configuredTarget = stats.wallSlideSpeed * wallSlideSpeedMultiplier;
            float targetWallSlideSpeed = -Mathf.Max(wallSlideTargetFallSpeed, Mathf.Abs(configuredTarget));
            if (rb.velocity.y < targetWallSlideSpeed)
            {
                float newY = Mathf.MoveTowards(
                    rb.velocity.y,
                    targetWallSlideSpeed,
                    Mathf.Max(stats.wallSlideAcceleration, 60f) * Time.fixedDeltaTime
                );
                rb.velocity = new Vector2(rb.velocity.x, newY);
            }
        }

        private void UpdateWallSlideEdgeVisual(float deltaTime)
        {
            isWallLosingContact = false;
            float visibleHeight = float.PositiveInfinity;
            bool edgeBelowCenter = false;
            float edgeY = wallSlideVisualEdgeY;

            if (!isWallSliding || wallSide == 0 || capsule == null)
            {
                wallSlideVisualHasEdge = false;
                wallSlideVisualVisibleFraction = 1f;
                return;
            }

            if (wallSlideVisualCollider != null && wallSlideVisualSide == wallSide && !wallSlideVisualHasEdge)
                wallSlideVisualHasEdge = TryFindCurrentWallLowerEdge(out wallSlideVisualEdgeY);
            edgeY = wallSlideVisualEdgeY;

            if (wallSlideVisualHasEdge)
            {
                Bounds bounds = capsule.bounds;
                edgeBelowCenter = edgeY <= bounds.center.y;
                if (edgeBelowCenter)
                {
                    visibleHeight = bounds.max.y - edgeY;
                    wallSlideVisualVisibleFraction = Mathf.Clamp01(
                        visibleHeight / Mathf.Max(bounds.size.y, 0.0001f)
                    );
                    isWallLosingContact = wallSlideVisualVisibleFraction <= wallSlideEdgeVisibleFractionThreshold;
                }
                else
                {
                    wallSlideVisualVisibleFraction = 1f;
                }
            }
            else
            {
                wallSlideVisualVisibleFraction = 1f;
            }
        }

        private bool TryFindCurrentWallLowerEdge(out float edgeY)
        {
            const float inwardSkin = 0.02f;
            const float verticalSearchMargin = 0.25f;
            const float minHitDistance = 0.001f;
            const float lowerEdgeNormalMinY = 0.5f;
            const float edgeWallProbeYOffset = 0.03f;

            edgeY = 0f;
            if (wallSlideVisualCollider == null || wallSlideVisualSide == 0) return false;

            Bounds bounds = capsule.bounds;
            Vector2 wallDirection = wallSlideVisualSide > 0 ? Vector2.right : Vector2.left;
            Vector2 origin = new Vector2(
                wallSlideVisualHit.point.x + wallDirection.x * inwardSkin,
                bounds.min.y - verticalSearchMargin
            );
            float distance = bounds.size.y + verticalSearchMargin * 2f;

            int hitCount = Physics2D.RaycastNonAlloc(
                origin,
                Vector2.up,
                wallEdgeHits,
                distance,
                SolidMask()
            );

            bool found = false;
            float lowestEdgeY = float.PositiveInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D candidate = wallEdgeHits[i];
                if (candidate.collider != wallSlideVisualCollider) continue;
                if (candidate.collider.isTrigger) continue;
                if (candidate.distance <= minHitDistance) continue;
                if (candidate.normal.y > -lowerEdgeNormalMinY) continue;
                Vector2 wallProbeOrigin = new Vector2(
                    wallSlideVisualHit.point.x - wallDirection.x * inwardSkin,
                    candidate.point.y + edgeWallProbeYOffset
                );
                RaycastHit2D wallProbeHit = Physics2D.Raycast(
                    wallProbeOrigin,
                    wallDirection,
                    inwardSkin * 3f,
                    SolidMask()
                );
                if (wallProbeHit.collider != wallSlideVisualCollider) continue;
                if (!IsValidWallNormalForSide(wallProbeHit.normal, wallSlideVisualSide)) continue;
                if (candidate.point.y >= lowestEdgeY) continue;

                lowestEdgeY = candidate.point.y;
                found = true;
            }

            if (!found) return false;

            edgeY = lowestEdgeY;
            return true;
        }

        private void LogWallSlideBlocked(string reason, float inputX)
        {
            if (!logWallDebug) return;
            if (!isTouchingWall || isWallSliding) return;
            if (wallSlideBlockDebugLogTimer > 0f) return;

            wallSlideBlockDebugLogTimer = wallDebugLogInterval;
            Debug.Log(
                $"[WALL SLIDE BLOCKED] reason={reason} inputX={inputX:0.00} wallSide={wallSide} " +
                $"grounded={isGrounded} vy={rb.velocity.y:0.00} " +
                $"automaticEntry=True sameWallBlocked={IsSameWallClimbBlocked(wallSide)} " +
                $"suppressFixed={suppressWallSlideThisFixed}");
        }

        private void WallJump()
        {
            if (suppressWallWhileDownHeld) return;

            bool canWallJump = isWallSliding;
            if (!canWallJump) return;
            if (wallJumpRegrabTimer > 0f) return;

            int activeWallSide = wallSide != 0 ? wallSide : wallCoyoteSide;
            if (activeWallSide == 0)
                activeWallSide = facingDir;

            int jumpDir = -activeWallSide;
            int jumpedWallSide = activeWallSide;
            Collider2D jumpedWallCollider = GetWallColliderOnSide(jumpedWallSide);
            if (IsSameWallClimbBlocked(jumpedWallSide, jumpedWallCollider)) return;

            ClearSprintMomentum();
            upwardAirCurrentMomentumProtected = false;
            downHitBounceAscentProtected = false;

            float jumpMult = IsCarrying ? carryingJumpForceMultiplier : 1f;
            float wallJumpForceX = stats.wallJumpForceX;
            if (TryGetWallHitOnSide(jumpDir, narrowWallProbeDistance, out _))
                wallJumpForceX *= narrowWallJumpForceXMultiplier;
            float jumpX = jumpDir * wallJumpForceX * jumpMult;
            float keptMomentumX = Mathf.Max(0f, rb.velocity.x * jumpDir) * stats.wallJumpMomentumKeep * jumpDir;
            rb.velocity = new Vector2(jumpX + keptMomentumX, stats.wallJumpForceY * jumpMult);

            ApplyFacing(jumpDir);

            lastWallJumpSide = jumpedWallSide;
            lastWallJumpY = rb.position.y;
            lastWallJumpCollider = jumpedWallCollider;
            RegisterSameWallClimbCeiling(jumpedWallSide, lastWallJumpCollider);
            wallJumpLockTimer = wallJumpLockTime;
            wallJumpRegrabTimer = wallJumpRegrabDelay;
            wallStickTimer = 0f;
            wallCoyoteTimer = 0f;
            wallStickSide = 0;
            wallCoyoteSide = 0;
            animDriver?.TriggerWallJumpStart();
            WallJumped?.Invoke(jumpedWallSide);
        }

        // --------------------
        // CHECKS
        // --------------------
        private void CheckGround()
        {
            if (groundCheck == null)
            {
                isGrounded = false;
                groundNormal = Vector2.up;
                currentGroundCollider = null;
                currentGroundPlatform = null;
                currentGroundStandableSurface = null;
                idleAnchorStandableSurface = null;
                UpdateGroundConveyor(null, null);
                slopeMovementSuppressedUntilAirborne = false;
                return;
            }

            int standardGroundMask = StandardGroundMask();
            int mask = standardGroundMask | standableActorMask.value;
            Vector2 size = new Vector2(
                Mathf.Max(0.01f, groundCheckBoxSize.x),
                Mathf.Max(0.01f, groundCheckBoxSize.y)
            );
            Vector2 origin = (Vector2)groundCheck.position + Vector2.up * (size.y * 0.5f);
            float castDistance = groundCheckDistance + size.y * 0.5f;

            int hitCount = Physics2D.BoxCastNonAlloc(
                origin,
                size,
                0f,
                Vector2.down,
                groundHits,
                castDistance,
                mask
            );

            RaycastHit2D bestHit = default;
            float bestNormalY = float.NegativeInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D candidate = groundHits[i];
                if (candidate.collider == null) continue;
                if (candidate.collider.isTrigger) continue;
                if (!IsAllowedGroundCollider(candidate.collider, standardGroundMask)) continue;
                if (candidate.normal.y < groundNormalMinY) continue;
                if (candidate.normal.y <= bestNormalY) continue;

                bestNormalY = candidate.normal.y;
                bestHit = candidate;
            }

            isGrounded = bestHit.collider != null;
            if (isGrounded)
                doubleJumpAvailable = doubleJumpEquipped;

            groundNormal = isGrounded ? bestHit.normal.normalized : Vector2.up;
            currentGroundCollider = isGrounded ? bestHit.collider : null;
            if (!isGrounded)
                slopeMovementSuppressedUntilAirborne = false;

            currentGroundPlatform = isGrounded
                ? bestHit.collider.GetComponentInParent<HorizontalMovingPlatform>()
                : null;
            currentGroundStandableSurface = isGrounded
                ? GetAcceptedStandableSurface(bestHit.collider)
                : null;
            ConveyorBelt detectedConveyor = isGrounded
                ? bestHit.collider.GetComponentInParent<ConveyorBelt>()
                : null;
            WoodWorkpiece detectedWoodWorkpiece = isGrounded
                ? bestHit.collider.GetComponentInParent<WoodWorkpiece>()
                : null;
            if (detectedWoodWorkpiece != null)
                detectedConveyor = null;
            UpdateGroundConveyor(detectedConveyor, detectedWoodWorkpiece);

            if (logLedgeDebug && bestHit.collider != lastGroundDebugCollider)
            {
                lastGroundDebugCollider = bestHit.collider;
                Debug.Log($"[GROUND DEBUG] grounded={isGrounded} hit={DescribeCollider2D(bestHit.collider)} point={bestHit.point} normal={bestHit.normal}");
            }
        }

        private bool ShouldUseSlopeMovement()
        {
            return
                isGrounded &&
                groundNormal.y >= groundNormalMinY &&
                groundNormal.y < SlopeNormalMaxY &&
                !slopeMovementSuppressedUntilAirborne &&
                currentGroundPlatform == null &&
                currentGroundStandableSurface == null &&
                currentGroundConveyor == null &&
                currentGroundWoodWorkpiece == null;
        }

        private void UpdateGroundConveyor(
            ConveyorBelt detectedConveyor,
            WoodWorkpiece detectedWoodWorkpiece)
        {
            if (!isGrounded)
            {
                currentGroundConveyor = null;
                currentGroundWoodWorkpiece = null;
                appliedConveyorVelocityX = 0f;
                suppressConveyorUntilAirborne = false;
                return;
            }

            if (suppressConveyorUntilAirborne && rb.velocity.y <= 0f)
                suppressConveyorUntilAirborne = false;

            if (suppressConveyorUntilAirborne)
            {
                detectedConveyor = null;
                detectedWoodWorkpiece = null;
            }

            if (detectedConveyor != currentGroundConveyor ||
                detectedWoodWorkpiece != currentGroundWoodWorkpiece ||
                (detectedConveyor == null && detectedWoodWorkpiece == null))
                appliedConveyorVelocityX = 0f;

            currentGroundConveyor = detectedConveyor;
            currentGroundWoodWorkpiece = detectedWoodWorkpiece;
        }

        private float CurrentConveyorVelocityX()
        {
            if (!isGrounded || suppressConveyorUntilAirborne)
                return 0f;

            if (currentGroundWoodWorkpiece != null)
            {
                Rigidbody2D supportBody = currentGroundWoodWorkpiece.Body;
                return supportBody != null && supportBody.simulated
                    ? supportBody.velocity.x
                    : 0f;
            }

            return currentGroundConveyor != null
                ? currentGroundConveyor.SurfaceVelocity.x
                : 0f;
        }

        private float HorizontalVelocityWithoutConveyor()
        {
            return rb.velocity.x - appliedConveyorVelocityX;
        }

        private void SetHorizontalVelocityWithConveyor(float controlledVelocityX)
        {
            float surfaceVelocityX = CurrentConveyorVelocityX();
            rb.velocity = new Vector2(controlledVelocityX + surfaceVelocityX, rb.velocity.y);
            appliedConveyorVelocityX = surfaceVelocityX;
        }

        private void DetachConveyorForTakeoff()
        {
            float supportVelocityX = CurrentConveyorVelocityX();
            bool hadSupportVelocity =
                Mathf.Abs(supportVelocityX) > 0.0001f ||
                Mathf.Abs(appliedConveyorVelocityX) > 0.0001f;
            float carriedVelocityX =
                HorizontalVelocityWithoutConveyor() + supportVelocityX;
            rb.velocity = new Vector2(carriedVelocityX, rb.velocity.y);
            appliedConveyorVelocityX = 0f;
            currentGroundConveyor = null;
            currentGroundWoodWorkpiece = null;
            suppressConveyorUntilAirborne = hadSupportVelocity;
        }

        private void ApplyMovingPlatformDelta()
        {
            StabilizeIdleStandableSurface();
            if (!isGrounded) return;

            Vector2 delta;
            if (currentGroundPlatform != null)
                delta = currentGroundPlatform.LastDelta;
            else if (currentGroundStandableSurface != null)
                delta = currentGroundStandableSurface.LastDelta;
            else
                return;

            if (delta.sqrMagnitude <= 0f) return;

            rb.position += delta;
            transform.position = rb.position;
        }

        private void StabilizeIdleStandableSurface()
        {
            if (!CanStabilizeIdleStandableSurface())
            {
                idleAnchorStandableSurface = null;
                return;
            }

            if (idleAnchorStandableSurface != currentGroundStandableSurface)
            {
                idleAnchorStandableSurface = currentGroundStandableSurface;
                idleStandableOffsetX = rb.position.x - currentGroundStandableSurface.transform.position.x;
                return;
            }

            float targetX = currentGroundStandableSurface.transform.position.x + idleStandableOffsetX;
            float correctedX = Mathf.MoveTowards(
                rb.position.x,
                targetX,
                standableDriftCorrectionSpeed * Time.fixedDeltaTime);
            rb.position = new Vector2(correctedX, rb.position.y);
            transform.position = rb.position;

            // Standable actors are carried once by LastDelta below. Keeping the
            // controller's own world-X velocity at zero therefore means zero
            // relative velocity without adding the support velocity a second time.
            rb.velocity = new Vector2(0f, rb.velocity.y);
        }

        private bool CanStabilizeIdleStandableSurface()
        {
            if (!isGrounded || currentGroundStandableSurface == null || input == null)
                return false;
            if (Mathf.Abs(input.MoveInput.x) > standableIdleInputThreshold)
                return false;
            if (isDashing || isWallSliding || IsSliding || IsDiving || IsPostDiveSliding ||
                hasPendingNormalJump || IsSwinging || isHanging || isOverheadRailHanging)
                return false;
            if (HasSprintMomentum() || horizontalAirCurrentMomentumActive)
                return false;

            return true;
        }

        private void ReleaseCurrentStandableSupport(
            Game.Combat.IPlayerAttackReceiver expectedReceiver,
            bool waitForHorizontalSeparation)
        {
            if (currentGroundStandableSurface == null || expectedReceiver == null)
                return;

            var supportReceiver =
                currentGroundStandableSurface.GetComponent<Game.Combat.IPlayerAttackReceiver>() ??
                currentGroundStandableSurface.GetComponentInParent<Game.Combat.IPlayerAttackReceiver>();
            if (!ReferenceEquals(supportReceiver, expectedReceiver))
                return;

            ReleaseCurrentStandableSupport(waitForHorizontalSeparation);
        }

        private bool IsHitCurrentStandableSupport(
            Collider2D hitCollider,
            Component damageTarget)
        {
            if (currentGroundStandableSurface == null)
                return false;

            PlayerStandableSurface hitSurface = hitCollider != null
                ? hitCollider.GetComponentInParent<PlayerStandableSurface>()
                : null;
            if (hitSurface == null && damageTarget != null)
            {
                hitSurface = damageTarget.GetComponent<PlayerStandableSurface>() ??
                             damageTarget.GetComponentInParent<PlayerStandableSurface>();
            }

            return hitSurface == currentGroundStandableSurface;
        }

        private void ReleaseCurrentStandableSupport(bool waitForHorizontalSeparation)
        {
            if (currentGroundStandableSurface == null)
                return;

            ignoredStandableSurface = currentGroundStandableSurface;
            ignoredStandableMinimumTimer = releasedSupportIgnoreDuration;
            ignoredStandableTimer = waitForHorizontalSeparation
                ? Mathf.Max(
                    releasedSupportIgnoreDuration,
                    tankDownHitSupportIgnoreMaxDuration)
                : releasedSupportIgnoreDuration;
            ignoredStandableWaitForHorizontalSeparation = waitForHorizontalSeparation;
            idleAnchorStandableSurface = null;
            idleStandableOffsetX = 0f;
            isGrounded = false;
            groundNormal = Vector2.up;
            currentGroundCollider = null;
            currentGroundPlatform = null;
            currentGroundStandableSurface = null;
            slopeMovementSuppressedUntilAirborne = false;
            UpdateGroundConveyor(null, null);
        }

        private void UpdateIgnoredStandableTimer(float deltaTime)
        {
            if (ignoredStandableSurface == null)
            {
                ClearIgnoredStandableSurface();
                return;
            }

            float elapsed = Mathf.Max(0f, deltaTime);
            ignoredStandableTimer = Mathf.Max(0f, ignoredStandableTimer - elapsed);
            ignoredStandableMinimumTimer = Mathf.Max(
                0f,
                ignoredStandableMinimumTimer - elapsed);
            if (ignoredStandableTimer <= 0f)
            {
                ClearIgnoredStandableSurface();
                return;
            }

            if (ignoredStandableMinimumTimer > 0f)
                return;

            if (!ignoredStandableWaitForHorizontalSeparation ||
                !HasHorizontalOverlapWithIgnoredStandableSurface())
            {
                ClearIgnoredStandableSurface();
            }
        }

        private bool HasHorizontalOverlapWithIgnoredStandableSurface()
        {
            if (capsule == null || !capsule.enabled || ignoredStandableSurface == null)
                return false;

            Collider2D supportCollider = ignoredStandableSurface.SurfaceCollider;
            if (supportCollider == null || !supportCollider.enabled)
                return false;

            Bounds playerBounds = capsule.bounds;
            Bounds supportBounds = supportCollider.bounds;
            float horizontalOverlap =
                Mathf.Min(playerBounds.max.x, supportBounds.max.x) -
                Mathf.Max(playerBounds.min.x, supportBounds.min.x);
            return horizontalOverlap > 0.01f;
        }

        private void ClearIgnoredStandableSurface()
        {
            ignoredStandableSurface = null;
            ignoredStandableTimer = 0f;
            ignoredStandableMinimumTimer = 0f;
            ignoredStandableWaitForHorizontalSeparation = false;
        }

        private bool IsAllowedGroundCollider(Collider2D candidate, int standardGroundMask)
        {
            int candidateLayer = 1 << candidate.gameObject.layer;
            if ((standardGroundMask & candidateLayer) != 0)
                return true;

            if ((standableActorMask.value & candidateLayer) == 0)
                return false;

            PlayerStandableSurface surface = GetAcceptedStandableSurface(candidate);
            return surface != null &&
                   (ignoredStandableTimer <= 0f || surface != ignoredStandableSurface);
        }

        private int StandardGroundMask()
        {
            return groundLayer.value != 0 ? groundLayer.value : SolidMask();
        }

        private static PlayerStandableSurface GetAcceptedStandableSurface(Collider2D candidate)
        {
            if (candidate == null)
                return null;

            PlayerStandableSurface surface =
                candidate.GetComponentInParent<PlayerStandableSurface>();
            return surface != null && surface.Accepts(candidate) ? surface : null;
        }

        private void UpdateRecentlyGroundedTimer(float deltaTime)
        {
            if (isGrounded)
            {
                recentlyGroundedTimer = recentlyGroundedAntiStuckGraceTime;
                return;
            }

            recentlyGroundedTimer = Mathf.Max(0f, recentlyGroundedTimer - deltaTime);
        }

        private void CheckWall(float inputYClean)
        {
            if (capsule == null) return;

            bool downHeld = inputYClean <= -hangDownThreshold;
            if (disableWallWhileDownHeldAfterDrop && suppressWallWhileDownHeld && downHeld)
            {
                isTouchingWall = false;
                wallSide = 0;
                hasNarrowWallContact = false;
                wallStickTimer = 0f;
                wallCoyoteTimer = 0f;
                wallStickSide = 0;
                wallCoyoteSide = 0;
                return;
            }

            if (isGrounded)
            {
                isTouchingWall = false;
                isWallSliding = false;
                wallSide = 0;
                hasNarrowWallContact = false;
                wallStickTimer = 0f;
                wallCoyoteTimer = 0f;
                wallStickSide = 0;
                wallCoyoteSide = 0;
                ClearSameWallClimbBlock();
                return;
            }

            float inputX = Mathf.Clamp(input.MoveInput.x, -1f, 1f);
            if (Mathf.Abs(inputX) < wallInputDeadzone) inputX = 0f;

            bool hasRightWall = TryGetWallHitOnSide(1, out RaycastHit2D rightWallHit);
            bool hasLeftWall = TryGetWallHitOnSide(-1, out RaycastHit2D leftWallHit);
            hasNarrowWallContact = hasRightWall && hasLeftWall;
            int previousWallSide = wallSide;

            isTouchingWall = false;
            wallSide = 0;

            if (hasRightWall && hasLeftWall)
            {
                isTouchingWall = true;
                if (isWallSliding && previousWallSide != 0)
                    wallSide = previousWallSide;
                else if (inputX != 0f)
                    wallSide = (int)Mathf.Sign(inputX);
                else if (previousWallSide != 0)
                    wallSide = previousWallSide;
                else if (wallCoyoteSide != 0)
                    wallSide = wallCoyoteSide;
                else
                    wallSide = facingDir;
            }
            else if (hasRightWall)
            {
                isTouchingWall = true;
                wallSide = 1;
            }
            else if (hasLeftWall)
            {
                isTouchingWall = true;
                wallSide = -1;
            }

            if (isTouchingWall && wallSide != 0)
            {
                RaycastHit2D selectedWallHit = wallSide > 0 ? rightWallHit : leftWallHit;
                UpdateWallSlideVisualReference(selectedWallHit, wallSide);
            }

            if (isTouchingWall)
            {
                wallStickTimer = wallStickTime;
                wallCoyoteTimer = wallCoyoteTime;
                wallStickSide = wallSide;
                wallCoyoteSide = wallSide;

                if (wallSide != 0)
                {
                    Collider2D selectedWallCollider = GetWallColliderOnSide(wallSide);
                    if (IsValidOppositeWallForClimbReset(wallSide, selectedWallCollider))
                        ClearSameWallClimbBlock();
                }
            }
            else
            {
                wallStickTimer = Mathf.Max(0f, wallStickTimer - Time.deltaTime);
                wallCoyoteTimer = Mathf.Max(0f, wallCoyoteTimer - Time.deltaTime);
                wallStickTimer = 0f;
                wallStickSide = 0;

                if (wallCoyoteTimer <= 0f)
                    wallCoyoteSide = 0;
            }
        }

        private void UpdateWallSlideVisualReference(RaycastHit2D wallHit, int side)
        {
            if (wallHit.collider != wallSlideVisualCollider || side != wallSlideVisualSide)
            {
                wallSlideVisualHasEdge = false;
                wallSlideVisualVisibleFraction = 1f;
            }

            wallSlideVisualHit = wallHit;
            wallSlideVisualCollider = wallHit.collider;
            wallSlideVisualSide = side;
        }

        private bool IsInputPushingIntoWall(int side)
        {
            if (input == null || side == 0) return false;

            float inputX = Mathf.Clamp(input.MoveInput.x, -1f, 1f);
            if (Mathf.Abs(inputX) < wallInputDeadzone) return false;

            return Mathf.Sign(inputX) == side;
        }

        private bool TryGetWallHitOnSide(int side, out RaycastHit2D hit)
        {
            return TryGetWallHitOnSide(side, wallCheckDistance, out hit);
        }

        private bool TryGetWallHitOnSide(int side, float distance, out RaycastHit2D hit)
        {
            hit = default;
            if (side == 0 || capsule == null) return false;

            Vector2 dir = side > 0 ? Vector2.right : Vector2.left;
            ContactFilter2D filter = new ContactFilter2D();
            filter.SetLayerMask(SolidMask());
            filter.useLayerMask = true;
            filter.useTriggers = false;

            int hitCount = capsule.Cast(dir, filter, wallHits, Mathf.Max(0f, distance));
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D candidate = wallHits[i];
                if (candidate.collider == null) continue;
                if (candidate.collider == capsule) continue;
                if (candidate.collider.isTrigger) continue;
                if (!IsValidWallNormalForSide(candidate.normal, side)) continue;

                hit = candidate;
                LogAcceptedWallHit(side, candidate);
                return true;
            }

            return false;
        }

        private bool IsValidWallNormalForSide(Vector2 normal, int side)
        {
            return normal.x * side <= -wallNormalMinX && normal.y < groundNormalMinY;
        }

        private void LogAcceptedWallHit(int side, RaycastHit2D hit)
        {
            if (!logWallDebug) return;
            if (wallHitDebugLogTimer > 0f) return;

            wallHitDebugLogTimer = wallDebugLogInterval;
            string layerName = LayerMask.LayerToName(hit.collider.gameObject.layer);
            Debug.Log(
                $"[WALL HIT] side={side} collider={DescribeCollider2D(hit.collider)} " +
                $"layer={hit.collider.gameObject.layer}:{layerName} normal={hit.normal} distance={hit.distance:0.000}");
        }

        private void HandleCornerAntiStuck()
        {
            if (!enableCornerAntiStuck)
            {
                cornerStuckTimer = 0f;
                return;
            }

            if (isGrounded || isHanging || IsSwinging || isDashing)
            {
                cornerStuckTimer = 0f;
                return;
            }

            if (recentlyGroundedTimer > 0f)
            {
                cornerStuckTimer = 0f;
                return;
            }

            float inputX = Mathf.Clamp(input.MoveInput.x, -1f, 1f);
            if (Mathf.Abs(inputX) < wallInputDeadzone)
            {
                cornerStuckTimer = 0f;
                return;
            }

            int inputSide = (int)Mathf.Sign(inputX);
            bool pushingIntoCorner = HasCornerStuckContact(inputSide);
            bool horizontalBlocked = Mathf.Abs(rb.velocity.x) <= cornerStuckHorizontalSpeedThreshold;

            if ((!pushingIntoCorner && !horizontalBlocked) || Mathf.Abs(rb.velocity.y) > cornerStuckVerticalSpeedThreshold)
            {
                cornerStuckTimer = 0f;
                return;
            }

            cornerStuckTimer += Time.fixedDeltaTime;
            LogCornerStuckDebug(inputX);
            if (cornerStuckTimer < cornerStuckTime) return;

            isWallSliding = false;
            isTouchingWall = false;
            wallSide = 0;
            wallStickTimer = 0f;
            wallCoyoteTimer = 0f;
            wallStickSide = 0;
            wallCoyoteSide = 0;
            suppressWallSlideThisFixed = true;

            if (TryApplyCornerCorrection(inputSide))
            {
                cornerStuckTimer = 0f;
                LogCornerStuckDebug(inputX, true);
                return;
            }

            float pushX = pushingIntoCorner ? -inputSide * cornerPushOutSpeed : 0f;
            float fallY = Mathf.Min(rb.velocity.y, cornerForceFallSpeed);
            rb.velocity = new Vector2(pushX, fallY);
            cornerStuckTimer = 0f;
            LogCornerStuckDebug(inputX, true);
        }

        private bool TryApplyCornerCorrection(int inputSide)
        {
            if (capsule == null || cornerCorrectionOffsets == null) return false;

            Vector2 startPosition = rb.position;
            for (int i = 0; i < cornerCorrectionOffsets.Length; i++)
            {
                Vector3 rawOffset = cornerCorrectionOffsets[i];
                Vector2 offset = new Vector2(rawOffset.x * inputSide, rawOffset.y);
                Vector2 candidatePosition = startPosition + offset;

                if (!IsCapsuleBlockedAt(candidatePosition))
                {
                    rb.position = candidatePosition;
                    transform.position = candidatePosition;
                    rb.velocity = new Vector2(0f, Mathf.Min(rb.velocity.y, cornerForceFallSpeed));
                    return true;
                }
            }

            return false;
        }

        private bool IsCapsuleBlockedAt(Vector2 bodyPosition)
        {
            Vector2 capsuleCenter = bodyPosition + capsule.offset;
            int count = Physics2D.OverlapCapsuleNonAlloc(
                capsuleCenter,
                capsule.size,
                capsule.direction,
                0f,
                cornerCorrectionHits,
                SolidMask()
            );

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = cornerCorrectionHits[i];
                if (hit == null || hit == capsule || hit.isTrigger) continue;
                return true;
            }

            return false;
        }

        private bool HasCornerStuckContact(int side)
        {
            if (side == 0 || capsule == null) return false;

            Bounds b = capsule.bounds;
            Vector2 dir = side > 0 ? Vector2.right : Vector2.left;
            float sideX = b.center.x + dir.x * (b.extents.x + 0.01f);
            float[] ySamples =
            {
                b.center.y - b.extents.y * 0.35f,
                b.center.y,
                b.center.y + b.extents.y * 0.25f
            };

            for (int i = 0; i < ySamples.Length; i++)
            {
                Vector2 origin = new Vector2(sideX, ySamples[i]);
                RaycastHit2D hit = Physics2D.Raycast(origin, dir, cornerStuckProbeDistance, SolidMask());
                if (hit.collider == null) continue;
                if (hit.normal.y >= groundNormalMinY) continue;
                return true;
            }

            return false;
        }

        private void LogCornerStuckDebug(float inputX, bool resolved = false)
        {
            if (!logCornerStuckDebug) return;
            if (!resolved && cornerStuckDebugLogTimer > 0f) return;

            cornerStuckDebugLogTimer = 0.15f;
            Debug.Log(
                $"[CORNER STUCK] resolved={resolved} timer={cornerStuckTimer:0.00} inputX={inputX:0.00} " +
                $"vel={rb.velocity} grounded={isGrounded} sliding={isWallSliding} touching={isTouchingWall} side={wallSide}");
        }

        private void LogWallDebugState()
        {
            if (!logWallDebug) return;
            if (wallDebugLogTimer > 0f) return;
            if (isGrounded || rb.velocity.y >= 0f) return;

            wallDebugLogTimer = wallDebugLogInterval;
            Debug.Log(
                $"[WALL DEBUG] inputX={Mathf.Clamp(input.MoveInput.x, -1f, 1f):0.00} " +
                $"touching={isTouchingWall} side={wallSide} vy={rb.velocity.y:0.00} " +
                $"sliding={isWallSliding} checkDist={wallCheckDistance:0.00} " +
                $"stick={wallStickTimer:0.00} coyote={wallCoyoteTimer:0.00}");
        }

        private bool IsSameWallClimbBlocked(int side)
        {
            if (!preventSameWallClimb) return false;
            if (lastWallJumpSide == 0 || side == 0) return false;
            bool verticalBlockActive = rb.position.y >= lastWallJumpY - sameWallJumpYReleaseMargin;
            if (!verticalBlockActive) return false;
            if (side == lastWallJumpSide) return true;
            return hasNarrowWallContact && side == -lastWallJumpSide;
        }

        private void ClearSameWallClimbBlock()
        {
            lastWallJumpSide = 0;
            lastWallJumpY = 0f;
            lastWallJumpCollider = null;
            sameWallClimbCollider = null;
            sameWallClimbSide = 0;
            sameWallClimbCeilingY = 0f;
        }

        private bool IsSameWallClimbBlocked(int side, Collider2D wallCollider)
        {
            const float sameWallJumpYReleaseEpsilon = 0.05f;

            if (!preventSameWallClimb) return false;
            if (sameWallClimbSide == 0 || side == 0) return false;
            if (side != sameWallClimbSide) return false;
            if (wallCollider == null || wallCollider != sameWallClimbCollider) return false;
            return rb.position.y >= sameWallClimbCeilingY - sameWallJumpYReleaseEpsilon;
        }

        private Collider2D GetWallColliderOnSide(int side)
        {
            return TryGetWallHitOnSide(side, out RaycastHit2D wallHit)
                ? wallHit.collider
                : null;
        }

        private void RegisterSameWallClimbCeiling(int jumpedWallSide, Collider2D jumpedCollider)
        {
            if (jumpedWallSide == 0 || jumpedCollider == null) return;
            if (sameWallClimbSide == jumpedWallSide && sameWallClimbCollider == jumpedCollider) return;

            sameWallClimbSide = jumpedWallSide;
            sameWallClimbCollider = jumpedCollider;
            sameWallClimbCeilingY = rb.position.y;
        }

        private bool IsValidOppositeWallForClimbReset(int side, Collider2D wallCollider)
        {
            if (sameWallClimbSide == 0 || wallCollider == null) return false;
            return side == -sameWallClimbSide && wallCollider != sameWallClimbCollider;
        }

        private int SolidMask()
        {
            int mask = groundLayer.value | wallLayer.value;
            return mask != 0 ? mask : Physics2D.DefaultRaycastLayers;
        }

        // --------------------
        // LEDGE GRAB / HANG
        // --------------------
        private void TryLedgeGrab(float inputXClean)
        {
            if (ledgeGrabCooldownTimer > 0f)
            {
                SetLedgeDebugFail("cooldown activo");
                return;
            }
            if (isHanging) return;
            if (isDashing)
            {
                SetLedgeDebugFail("dashing");
                return;
            }
            if (rb.velocity.y > ledgeGrabMaxVerticalSpeedToAllow)
            {
                SetLedgeDebugFail("vertical speed too high");
                return;
            }

            if (requirePushIntoWallToGrab)
            {
                if (inputXClean == 0f)
                {
                    SetLedgeDebugFail("requires push input");
                    return;
                }
                if (Mathf.Sign(inputXClean) != facingDir)
                {
                    SetLedgeDebugFail("input not toward wall");
                    return;
                }
            }

            if (TryFindLedgeCandidate(out Vector2 hangPos, out Vector2 corner, out int ledgeSide))
                CacheLedgeCandidate(hangPos, corner, ledgeSide);

            if (ledgeCandidateTimer <= 0f)
            {
                if (isGrounded)
                    SetLedgeDebugFail("grounded and no ledge candidate");
                else
                    SetLedgeDebugFail(string.IsNullOrEmpty(ledgeDebugFailReason) ? "no ledge candidate" : ledgeDebugFailReason);
                return;
            }

            if (IsSameWallClimbBlocked(ledgeCandidateSide))
            {
                SetLedgeDebugFail("same wall climb blocked");
                return;
            }

            float snapDistance = Vector2.Distance(transform.position, ledgeCandidateHangPos);
            if (snapDistance > EffectiveLedgeSnapDistance())
            {
                SetLedgeDebugFail($"snap too far {snapDistance:0.00}>{EffectiveLedgeSnapDistance():0.00}");
                return;
            }

            if (isGrounded)
            {
                SetLedgeDebugFail("candidate ok but grounded");
                return;
            }

            if (!input.ConsumeSwingGrabPressed())
            {
                SetLedgeDebugFail("candidate ok - waiting SwingGrab");
                return;
            }

            ApplyFacing(ledgeCandidateSide);
            EnterHang(ledgeCandidateHangPos, ledgeCandidateCorner);
        }

        private bool TryFindLedgeCandidate(out Vector2 hangPos, out Vector2 corner, out int ledgeSide)
        {
            hangPos = Vector2.zero;
            corner = Vector2.zero;
            ledgeSide = 0;
            SetLedgeDebugFail("no wallHit");

            Bounds b = capsule.bounds;

            float skin = 0.02f;
            float halfWidth = b.extents.x;
            float topY = b.max.y;
            float baseHandsY = topY - ledgeHandsFromTop;
            float tolerance = Mathf.Max(0f, ledgeVerticalTolerance);
            float[] yOffsets = { 0f, -0.5f, 0.5f, -1f, 1f };
            int firstSide = wallSide != 0 ? wallSide : facingDir;

            if (TryFindLedgeCandidateOnSide(firstSide, b, halfWidth, skin, baseHandsY, tolerance, yOffsets, out hangPos, out corner))
            {
                ledgeSide = firstSide;
                return true;
            }

            int otherSide = -firstSide;
            if (TryFindLedgeCandidateOnSide(otherSide, b, halfWidth, skin, baseHandsY, tolerance, yOffsets, out hangPos, out corner))
            {
                ledgeSide = otherSide;
                return true;
            }

            return false;
        }

        private bool TryFindLedgeCandidateOnSide(int side, Bounds b, float halfWidth, float skin, float baseHandsY, float tolerance, float[] yOffsets, out Vector2 hangPos, out Vector2 corner)
        {
            hangPos = Vector2.zero;
            corner = Vector2.zero;
            if (side == 0) return false;
            if (IsSameWallClimbBlocked(side))
            {
                SetLedgeDebugFail($"side {side} same wall climb blocked");
                return false;
            }

            Vector2 faceDir = side > 0 ? Vector2.right : Vector2.left;
            for (int i = 0; i < yOffsets.Length; i++)
            {
                Vector2 handsOrigin = new Vector2(
                    b.center.x + faceDir.x * (halfWidth + skin),
                    baseHandsY + yOffsets[i] * tolerance
                );

                if (TryBuildLedgeCandidate(handsOrigin, faceDir, side, out hangPos, out corner))
                    return true;
            }

            return false;
        }

        private bool TryBuildLedgeCandidate(Vector2 handsOrigin, Vector2 faceDir, int side, out Vector2 hangPos, out Vector2 corner)
        {
            hangPos = Vector2.zero;
            corner = Vector2.zero;

            int solidMask = SolidMask();
            float forwardCheck = ledgeForwardCheck + Mathf.Max(0f, ledgeSnapDistance - maxHangSnapDistance) * 0.35f;
            RaycastHit2D wallHit = Physics2D.Raycast(handsOrigin, faceDir, forwardCheck, solidMask);
            SetLedgeDebugWallRay(handsOrigin, handsOrigin + faceDir * forwardCheck);
            if (!wallHit)
            {
                SetLedgeDebugFail("no wallHit");
                return false;
            }
            if (Mathf.Abs(wallHit.normal.x) < wallNormalMinX)
            {
                SetLedgeDebugFail("wall normal invalid");
                return false;
            }

            Vector2 headOrigin = handsOrigin + Vector2.up * ledgeHeadClearance;
            RaycastHit2D headBlock = Physics2D.Raycast(headOrigin, faceDir, forwardCheck, solidMask);
            SetLedgeDebugHeadRay(headOrigin, headOrigin + faceDir * forwardCheck);
            if (headBlock)
            {
                SetLedgeDebugFail("headBlocked");
                return false;
            }

            Vector2 topCheckOrigin = headOrigin + faceDir * ledgeTopForward;
            int topMask = ledgeTopLayer.value != 0 ? ledgeTopLayer.value : solidMask;
            float downDistance = ledgeTopDown + ledgeVerticalTolerance;
            RaycastHit2D topGround = Physics2D.Raycast(topCheckOrigin, Vector2.down, downDistance, topMask);
            SetLedgeDebugTopRay(topCheckOrigin, topCheckOrigin + Vector2.down * downDistance);
            LogTopGroundDebug(topGround);
            if (!topGround)
            {
                SetLedgeDebugFail("no topGround");
                return false;
            }
            if (topGround.normal.y < groundNormalMinY)
            {
                SetLedgeDebugFail("topGround normal invalid");
                return false;
            }
            float verticalDelta = topGround.point.y - handsOrigin.y;
            if (Mathf.Abs(verticalDelta) > ledgeHandsFromTop + ledgeVerticalTolerance + ledgeHeadClearance)
            {
                SetLedgeDebugFail("vertical tolerance failed");
                return false;
            }

            corner = new Vector2(wallHit.point.x, topGround.point.y);

            float hangX = corner.x - faceDir.x * hangWallSkin + (ledgeHangOffset.x * side);
            float hangY = corner.y + ledgeHangOffset.y;
            hangPos = new Vector2(hangX, hangY);
            SetLedgeDebugCandidate(corner, hangPos);
            SetLedgeDebugFail($"candidate ok side={side}");
            return true;
        }

        private void CacheLedgeCandidate(Vector2 hangPos, Vector2 corner, int ledgeSide)
        {
            ledgeCandidateHangPos = hangPos;
            ledgeCandidateCorner = corner;
            ledgeCandidateSide = ledgeSide;
            ledgeCandidateTimer = ledgeGrabGraceTime;
        }

        private float EffectiveLedgeSnapDistance()
        {
            return Mathf.Max(maxHangSnapDistance, ledgeSnapDistance);
        }

        private void SetLedgeDebugWallRay(Vector2 start, Vector2 end)
        {
            ledgeDebugWallStart = start;
            ledgeDebugWallEnd = end;
            ledgeDebugHasProbe = true;
            DrawLedgeDebugLine(start, end, Color.cyan);
        }

        private void SetLedgeDebugHeadRay(Vector2 start, Vector2 end)
        {
            ledgeDebugHeadStart = start;
            ledgeDebugHeadEnd = end;
            DrawLedgeDebugLine(start, end, Color.yellow);
        }

        private void SetLedgeDebugTopRay(Vector2 start, Vector2 end)
        {
            ledgeDebugTopStart = start;
            ledgeDebugTopEnd = end;
            DrawLedgeDebugLine(start, end, Color.green);
        }

        private void SetLedgeDebugCandidate(Vector2 corner, Vector2 hangPos)
        {
            ledgeDebugCorner = corner;
            ledgeDebugHangPos = hangPos;
            ledgeDebugHasCorner = true;
            ledgeDebugHasHangPos = true;
        }

        private void SetLedgeDebugFail(string reason)
        {
            ledgeDebugFailReason = reason;
            if (!logLedgeDebug) return;
            if (ledgeDebugLogTimer > 0f && reason == lastLoggedLedgeFailReason) return;

            ledgeDebugLogTimer = ledgeDebugLogInterval;
            lastLoggedLedgeFailReason = reason;
            Debug.Log($"[LEDGE DEBUG] {reason} | pos={transform.position} vel={rb.velocity} facing={facingDir} wallSide={wallSide} lastWallJumpSide={lastWallJumpSide} lastWallJumpY={lastWallJumpY:0.00}");
        }

        private void LogTopGroundDebug(RaycastHit2D topGround)
        {
            if (!logLedgeDebug) return;
            if (topGround.collider == lastTopGroundDebugCollider) return;

            lastTopGroundDebugCollider = topGround.collider;
            Debug.Log($"[LEDGE TOP DEBUG] hit={DescribeCollider2D(topGround.collider)} point={topGround.point} normal={topGround.normal}");
        }

        private static string DescribeCollider2D(Collider2D col)
        {
            if (col == null) return "none";

            string bodyType = col.attachedRigidbody != null ? col.attachedRigidbody.bodyType.ToString() : "NoRigidbody";
            string materialName = col.sharedMaterial != null ? col.sharedMaterial.name : "NoMaterial";

            TilemapCollider2D tilemap = col as TilemapCollider2D;
            if (tilemap != null)
                return $"{col.name} ({col.GetType().Name}, usedByComposite={tilemap.usedByComposite}, rb={bodyType}, material={materialName})";

            CompositeCollider2D composite = col as CompositeCollider2D;
            if (composite != null)
                return $"{col.name} ({col.GetType().Name}, geometry={composite.geometryType}, rb={bodyType}, material={materialName})";

            return $"{col.name} ({col.GetType().Name}, rb={bodyType}, material={materialName})";
        }

        private void DrawLedgeDebugLine(Vector2 start, Vector2 end, Color color)
        {
            if (!drawLedgeDebug) return;
            Debug.DrawLine(start, end, color);
        }

        private void EnterHang(Vector2 lockedPos, Vector2 corner)
        {
            EnterHang(lockedPos, corner, false, Vector2.zero);
        }

        private void EnterHang(Vector2 lockedPos, Vector2 corner, bool hasClimbPosition, Vector2 climbPosition)
        {
            EnterHang(lockedPos, corner, hasClimbPosition, climbPosition, null);
        }

        private void EnterHang(Vector2 lockedPos, Vector2 corner, bool hasClimbPosition, Vector2 climbPosition, LedgeGrabPoint sourcePoint)
        {
            if (isAttacking)
                CancelAttack("ledge hang");
            ClearSprintMomentum();
            isHanging = true;
            isWallSliding = false;
            ledgeCandidateTimer = 0f;
            wallStickTimer = 0f;
            wallCoyoteTimer = 0f;
            activeLedgePoint = sourcePoint;
            SetLedgeDebugFail("enter hang");

            cachedBodyType = rb.bodyType;
            cachedConstraints = rb.constraints;
            cachedGravityScale = rb.gravityScale;

            hangPositionLocked = lockedPos;
            hasClimbPositionLocked = hasClimbPosition;
            climbPositionLocked = climbPosition;
            isLedgeClimbing = false;

            transform.position = hangPositionLocked;
            rb.velocity = Vector2.zero;
            rb.gravityScale = 0f;

            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.constraints = RigidbodyConstraints2D.FreezePosition | RigidbodyConstraints2D.FreezeRotation;

            ledgeGrabCooldownTimer = ledgeGrabCooldown;
            suppressWallWhileDownHeld = false;

            if (debugLogs)
                Debug.Log($"[HANG] ENTER lockPos={hangPositionLocked} corner={corner} climb={(hasClimbPositionLocked ? climbPositionLocked.ToString() : "none")} facing={facingDir}");
        }

        private void ExitHang(string reason = "")
        {
            isHanging = false;
            isLedgeClimbing = false;
            hasClimbPositionLocked = false;
            activeLedgePoint = null;

            rb.bodyType = cachedBodyType;
            rb.constraints = cachedConstraints;
            rb.gravityScale = cachedGravityScale;

            ledgeGrabCooldownTimer = ledgeGrabCooldown;

            if (debugLogs)
                Debug.Log($"[HANG] EXIT {(string.IsNullOrEmpty(reason) ? "" : $"({reason})")} pos={transform.position}");
        }

        private void UpdateHanging(float inputXClean, float inputYClean)
        {
            if (isLedgeClimbing)
            {
                rb.velocity = Vector2.zero;
                return;
            }

            RefreshActiveLedgePositions();
            transform.position = hangPositionLocked;
            rb.velocity = Vector2.zero;

            bool downHeld = inputYClean <= -hangDownThreshold;
            if (downHeld)
            {
                ExitHang("down-drop");
                rb.velocity = new Vector2(0f, 0f);

                if (disableWallWhileDownHeldAfterDrop)
                    suppressWallWhileDownHeld = true;

                return;
            }

            // Jump desde hang: OK (sale del hang)
            if (input.ConsumeJumpPressed())
            {
                RefreshActiveLedgePositions();

                if (hasClimbPositionLocked)
                {
                    StartLedgeClimb(climbPositionLocked);
                }
                else
                {
                    ExitHang("jump");
                    rb.velocity = new Vector2(ledgeClimbJumpX * facingDir, ledgeClimbJumpY);
                }

                wallJumpLockTimer = 0f;
                return;
            }

            // Dash desde hang: alejarse
            if (stats.hasDash && input.ConsumeDashPressed())
            {
                if (!CanSpendDashStamina()) return;

                int dashDir = -facingDir;
                ExitHang("dash-away");
                ApplyFacing(dashDir);
                SpendDashStamina();
                StartDashOverride(new Vector2(dashDir, 0f));
                return;
            }
        }

        private void RefreshActiveLedgePositions()
        {
            if (activeLedgePoint == null) return;

            if (activeLedgePoint.HangTransform != null)
                hangPositionLocked = activeLedgePoint.HangPosition;

            if (activeLedgePoint.StandTransform != null)
            {
                hasClimbPositionLocked = true;
                climbPositionLocked = activeLedgePoint.StandPosition;
            }
        }

        private bool CanSpendDashStamina()
        {
            return dashStaminaCost <= 0 || (stamina != null && stamina.HasStamina(dashStaminaCost));
        }

        private void StartLedgeClimb(Vector2 standPosition)
        {
            if (ledgeClimbRoutine != null)
                StopCoroutine(ledgeClimbRoutine);

            ledgeClimbRoutine = StartCoroutine(LedgeClimbRoutine(standPosition));
        }

        private IEnumerator LedgeClimbRoutine(Vector2 standPosition)
        {
            isLedgeClimbing = true;
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;

            if (capsule != null)
                capsule.enabled = false;

            Vector2 startPosition = rb.position;
            float duration = Mathf.Max(0.01f, ledgeClimbDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                t = t * t * (3f - 2f * t);

                Vector2 nextPosition = Vector2.Lerp(startPosition, standPosition, t);
                transform.position = nextPosition;
                rb.position = nextPosition;
                rb.velocity = Vector2.zero;
                yield return null;
            }

            SnapToLedgeStandPoint(standPosition, false);
            ExitHang("climb-finish");
            SnapToLedgeStandPoint(standPosition, false);

            if (capsule != null)
                capsule.enabled = true;

            isLedgeClimbing = false;
            ledgeClimbRoutine = null;
        }

        private void SnapToLedgeStandPoint(Vector2 standPosition, bool temporarilyDisableCollider = true)
        {
            if (temporarilyDisableCollider && capsule != null && ledgeClimbColliderDisableTime > 0f)
                StartCoroutine(TemporarilyDisablePlayerCollider(ledgeClimbColliderDisableTime));

            transform.position = standPosition;
            rb.position = standPosition;
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
            isWallSliding = false;
            wallStickTimer = 0f;
            wallCoyoteTimer = 0f;
            coyoteTimer = 0f;
            jumpBufferTimer = 0f;

            if (debugLogs)
                Debug.Log($"[HANG] CLIMB SNAP stand={standPosition}");
        }

        private IEnumerator TemporarilyDisablePlayerCollider(float seconds)
        {
            capsule.enabled = false;
            yield return new WaitForSeconds(seconds);
            capsule.enabled = true;
        }

        private void SpendDashStamina()
        {
            if (dashStaminaCost <= 0) return;
            stamina?.SpendStamina(dashStaminaCost);
        }

        private void StartDashOverride(Vector2 dir)
        {
            if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right * facingDir;
            dir = dir.normalized;

            dashStartedGrounded = false;
            dashFollowingSlope = false;
            ResetSlopeMovementCarry();

            isDashing = true;
            dashTimer = stats.dashDuration;
            dashCooldownTimer = stats.dashCooldown;

            rb.gravityScale = 0f;
            rb.velocity = dir * stats.dashSpeed;
        }

        private void OnDrawGizmosSelected()
        {
            if (groundCheck != null)
            {
                Vector2 size = new Vector2(
                    Mathf.Max(0.01f, groundCheckBoxSize.x),
                    Mathf.Max(0.01f, groundCheckBoxSize.y)
                );
                Vector3 origin = groundCheck.position + Vector3.up * (size.y * 0.5f);
                Vector3 center = origin + Vector3.down * (groundCheckDistance + size.y * 0.5f);

                Gizmos.color = isGrounded ? Color.green : Color.yellow;
                Gizmos.DrawWireCube(center, size);
                Gizmos.DrawLine(origin, center);
            }

            CapsuleCollider2D wallProbeCapsule = capsule != null ? capsule : GetComponent<CapsuleCollider2D>();
            if (wallProbeCapsule != null)
                DrawWallProbeGizmos(wallProbeCapsule);

            if (!drawLedgeDebug || !ledgeDebugHasProbe) return;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(ledgeDebugWallStart, ledgeDebugWallEnd);
            Gizmos.DrawWireSphere(ledgeDebugWallStart, 0.035f);

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(ledgeDebugHeadStart, ledgeDebugHeadEnd);
            Gizmos.DrawWireSphere(ledgeDebugHeadStart, 0.035f);

            Gizmos.color = Color.green;
            Gizmos.DrawLine(ledgeDebugTopStart, ledgeDebugTopEnd);
            Gizmos.DrawWireSphere(ledgeDebugTopStart, 0.035f);

            if (ledgeDebugHasCorner)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(ledgeDebugCorner, 0.08f);
            }

            if (ledgeDebugHasHangPos)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(ledgeDebugHangPos, 0.1f);
                Gizmos.DrawLine(transform.position, ledgeDebugHangPos);
            }
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || !showSprintDebugOverlay) return;

            float maxDuration = Mathf.Max(0f, sprintMaxDuration);
            float remaining = Mathf.Clamp(sprintRemaining, 0f, maxDuration);
            float fill = maxDuration > 0.0001f ? remaining / maxDuration : 0f;
            float velocityX = rb != null ? rb.velocity.x : 0f;
            string posture = IsLayDown ? "LayDown" : IsCrouching ? "Crouching" : "Standing";

            List<string> states = new List<string>();
            if (sprintActive) states.Add("ACTIVE");
            else states.Add("INACTIVE");

            if (sprintExhaustedUntilRelease)
                states.Add("BLOCKED UNTIL RELEASE");
            else if (!sprintActive && sprintRegenDelayTimer > 0f)
                states.Add("WAITING REGEN");
            else if (!sprintActive && remaining < maxDuration - 0.0001f)
                states.Add("REGENERATING");
            else if (!sprintActive && remaining >= maxDuration - 0.0001f)
                states.Add("FULL");

            bool hasMomentum = HasSprintMomentum();
            Rect panel = new Rect(12f, 12f, 360f, 214f);
            Rect bar = new Rect(panel.x + 12f, panel.y + 38f, panel.width - 24f, 18f);
            Rect fillRect = new Rect(bar.x, bar.y, bar.width * fill, bar.height);

            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.78f);
            GUI.Box(panel, "SPRINT DEBUG");

            GUI.color = new Color(0.12f, 0.12f, 0.12f, 1f);
            GUI.Box(bar, GUIContent.none);
            GUI.color = sprintActive ? new Color(0.2f, 0.85f, 0.35f, 1f) :
                new Color(0.25f, 0.65f, 0.95f, 1f);
            GUI.DrawTexture(fillRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(panel.x + 12f, panel.y + 60f, panel.width - 24f, 18f),
                $"Sprint: {remaining:0.00} / {maxDuration:0.00} s ({fill * 100f:0}%)");
            GUI.Label(new Rect(panel.x + 12f, panel.y + 80f, panel.width - 24f, 18f),
                $"State: {string.Join(" | ", states)}");
            GUI.Label(new Rect(panel.x + 12f, panel.y + 100f, panel.width - 24f, 18f),
                $"Regen delay: {sprintRegenDelayTimer:0.00} s | Held: {input != null && input.SprintHeld}");
            GUI.Label(new Rect(panel.x + 12f, panel.y + 120f, panel.width - 24f, 18f),
                $"Momentum: {(hasMomentum ? "YES" : "NO")} | air: {sprintMomentumTimer:0.00} s | landing: {sprintLandingMomentumTimer:0.00} s");
            GUI.Label(new Rect(panel.x + 12f, panel.y + 140f, panel.width - 24f, 18f),
                $"Velocity X: {velocityX:+0.00;-0.00;0.00}");
            GUI.Label(new Rect(panel.x + 12f, panel.y + 160f, panel.width - 24f, 18f),
                $"Physics: {(isGrounded ? "Grounded" : "Airborne")} | Posture: {posture}");
            GUI.Label(new Rect(panel.x + 12f, panel.y + 180f, panel.width - 24f, 18f),
                $"Momentum direction: {sprintMomentumDirection:+0;-0;0}");

            GUI.color = previousColor;
        }

        private void DrawWallProbeGizmos(CapsuleCollider2D sourceCapsule)
        {
            DrawWallProbeGizmosOnSide(sourceCapsule.bounds, Vector2.right, wallSide == 1);
            DrawWallProbeGizmosOnSide(sourceCapsule.bounds, Vector2.left, wallSide == -1);
        }

        private void DrawWallProbeGizmosOnSide(Bounds b, Vector2 dir, bool active)
        {
            Gizmos.color = active && isTouchingWall ? Color.green : Color.cyan;
            Vector3 startCenter = b.center;
            Vector3 endCenter = startCenter + (Vector3)(dir * wallCheckDistance);

            Gizmos.DrawWireCube(startCenter, b.size);
            Gizmos.DrawWireCube(endCenter, b.size);

            Vector3 topOffset = Vector3.up * b.extents.y;
            Vector3 bottomOffset = Vector3.down * b.extents.y;
            Vector3 sideOffset = (Vector3)(dir * b.extents.x);
            Gizmos.DrawLine(startCenter + topOffset + sideOffset, endCenter + topOffset + sideOffset);
            Gizmos.DrawLine(startCenter + bottomOffset + sideOffset, endCenter + bottomOffset + sideOffset);
        }

    }
}

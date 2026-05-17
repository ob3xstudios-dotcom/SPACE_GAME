using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Game.Barrels;
using Game.Interaction;
using Game.Input;
using Game.Systems;
using Game.UI;
using Game.World;

namespace Game.Player
{
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CapsuleCollider2D))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Referencias")]
        public InputReader input;
        public CharacterStats stats;

        [Header("Debug")]
        public bool debugLogs = false;

        [Header("Stamina")]
        [SerializeField] private PlayerStamina stamina;
        [SerializeField, Min(0)] private int dashStaminaCost = 1;

        [Header("Facing / Visual")]
        [SerializeField] private Transform visualRoot;

        // --------------------
        // GROUND / WALL
        // --------------------
        [Header("Ground Check")]
        public Transform groundCheck;
        public float groundCheckRadius = 0.2f;
        [SerializeField] private Vector2 groundCheckBoxSize = new Vector2(0.70f, 0.08f);
        [SerializeField, Range(0.01f, 0.25f)] private float groundCheckDistance = 0.08f;
        public LayerMask groundLayer;
        [Tooltip("Solo cuenta como suelo si la normal apunta hacia arriba (evita que pared lateral te ponga grounded).")]
        [SerializeField, Range(0f, 1f)] private float groundNormalMinY = 0.55f;

        [Header("Wall Check")]
        public Transform wallCheck;
        public float wallCheckDistance = 0.25f;
        public LayerMask wallLayer;
        [Tooltip("Solo cuenta como pared si la normal apunta lateralmente.")]
        [SerializeField, Range(0f, 1f)] private float wallNormalMinX = 0.55f;

        [Header("Tuning (Designer) - Wall Feel")]
        [SerializeField, Range(0.1f, 3f)] private float wallSlideSpeedMultiplier = 0.35f;
        [SerializeField, Range(0.4f, 4f)] private float wallSlideTargetFallSpeed = 1.2f;
        [SerializeField, Range(0f, 0.3f)] private float wallStickTime = 0.08f;
        [SerializeField, Range(0f, 0.3f)] private float wallCoyoteTime = 0.14f;
        [SerializeField, Range(0.05f, 0.3f)] private float wallJumpLockTime = 0.10f;

        [Header("Tuning (Designer) - Wall Input")]
        [SerializeField, Range(0.01f, 0.5f)] private float wallInputDeadzone = 0.2f;
        [SerializeField] private bool requirePushIntoWallForSlide = true;

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

        // --------------------
        // STEALTH MOVEMENT
        // --------------------
        [Header("Stealth Movement")]
        [SerializeField, Range(0.1f, 1f)] private float crouchSpeedMultiplier = 0.6f;
        [SerializeField, Range(0.1f, 1f)] private float layDownSpeedMultiplier = 0.35f;

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
        [SerializeField, Min(0f)] private float swingJumpForceX = 8f;
        [SerializeField, Min(0f)] private float swingJumpForceY = 12f;
        [SerializeField, Range(0f, 0.5f)] private float swingExitCooldown = 0.12f;
        [SerializeField, Range(0.1f, 1f)] private float swingDownThreshold = 0.5f;
        [SerializeField, Range(0f, 1f)] private float swingVelocityKeepMultiplier = 0.85f;
        [SerializeField, Range(-89f, 0f)] private float swingMinAngle = -75f;
        [SerializeField, Range(0f, 89f)] private float swingMaxAngle = 75f;
        [SerializeField, Min(0.01f)] private float swingMinBelowAnchor = 0.05f;
        private DistanceJoint2D swingJoint;
        private SwingPole currentSwingPole;
        private float swingCooldownTimer;
        private bool isSwinging;

        // --------------------
        // RUNTIME STATE
        // --------------------
        private float attackCooldownTimer;

        private Rigidbody2D rb;
        private CapsuleCollider2D capsule;
        private PlayerAnimatorDriver animDriver;

        [SerializeField] private PlayerStealthKill stealthKill;

        private bool isGrounded;
        private bool isTouchingWall;
        private bool isWallSliding;

        private float coyoteTimer;
        private float jumpBufferTimer;

        private bool isDashing;
        private float dashTimer;
        private float dashCooldownTimer;
        private int airDashesRemaining;

        private float wallJumpLockTimer;
        private float defaultGravityScale;

        private readonly Collider2D[] attackHits = new Collider2D[16];
        private readonly RaycastHit2D[] groundHits = new RaycastHit2D[8];
        private readonly RaycastHit2D[] wallHits = new RaycastHit2D[8];
        private readonly Collider2D[] cornerCorrectionHits = new Collider2D[8];
        private readonly HashSet<UnityEngine.Object> attackDamageTargets = new HashSet<UnityEngine.Object>();
        private readonly List<IInteractable> interactablesInRange = new List<IInteractable>();
        private bool lastAttackHitEnemy;

        private int facingDir = 1;
        private float attackPointLocalXAbs;
        private float wallCheckLocalXAbs;

        private Vector2 lockedAttackDir = Vector2.right;
        private int lockedAttackDirType = 0;

        private bool isAttacking;
        private bool canCancelAttack;
        private float attackStateTimer;
        private float cancelEnableTimer;

        private int wallSide;
        private int wallStickSide;
        private int wallCoyoteSide;
        private float wallStickTimer;
        private float wallCoyoteTimer;
        private float wallDebugLogTimer;
        private float wallHitDebugLogTimer;
        private float wallSlideBlockDebugLogTimer;
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

        private RigidbodyType2D cachedBodyType;
        private RigidbodyConstraints2D cachedConstraints;
        private float cachedGravityScale;

        private bool suppressWallWhileDownHeld;

        public bool FacingLeft => facingDir < 0;
        public bool IsGrounded => isGrounded;
        public bool IsAttacking => isAttacking;
        public bool IsHanging => isHanging;
        public bool IsWallSliding => isWallSliding;
        public int WallSide => wallSide;
        public bool IsCarrying => carriedObject != null;
        public bool IsSwinging => isSwinging;
        public BarrelCarryable CarriedObject => carriedObject;
        public Vector2 Velocity => rb != null ? rb.velocity : Vector2.zero;

        public event Action<int> WallJumped;

        // Para Animator (si lo usáis)
        public bool IsCrouching => input != null && input.IsCrouching;
        public bool IsLayDown => input != null && input.IsLayDown;

        public void FaceDirection(int dir)
        {
            ApplyFacing(dir < 0 ? -1 : 1);
        }

        public void ResetTransientState()
        {
            attackCooldownTimer = 0f;
            coyoteTimer = 0f;
            jumpBufferTimer = 0f;
            dashTimer = 0f;
            dashCooldownTimer = 0f;
            wallJumpLockTimer = 0f;
            wallStickTimer = 0f;
            wallCoyoteTimer = 0f;
            wallStickSide = 0;
            wallCoyoteSide = 0;
            ledgeGrabCooldownTimer = 0f;
            ledgeCandidateTimer = 0f;
            lastWallJumpSide = 0;
            lastWallJumpY = 0f;
            parryMoveLockTimer = 0f;
            attackStateTimer = 0f;
            cancelEnableTimer = 0f;

            isDashing = false;
            isWallSliding = false;
            isAttacking = false;
            canCancelAttack = false;
            suppressWallWhileDownHeld = false;

            if (carriedObject != null)
                carriedObject.Drop();

            if (isSwinging)
                ExitSwing(false);

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

            if (input != null)
                input.ForceStandUp();
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            capsule = GetComponent<CapsuleCollider2D>();
            animDriver = GetComponent<PlayerAnimatorDriver>();

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

            if (ledgeTopLayer.value == 0)
                ledgeTopLayer = groundLayer | wallLayer;

            ApplyFacing(1);

            if (stealthKill == null)
                stealthKill = GetComponent<PlayerStealthKill>();
        }

        private void Update()
        {
            if (stats == null || input == null) return;

            // cooldowns
            ledgeGrabCooldownTimer -= Time.deltaTime;
            ledgeCandidateTimer -= Time.deltaTime;

            if (parryMoveLockTimer > 0f)
                parryMoveLockTimer -= Time.deltaTime;

            if (interactLockTimer > 0f)
                interactLockTimer -= Time.deltaTime;

            if (swingCooldownTimer > 0f)
                swingCooldownTimer -= Time.deltaTime;

            if (ledgeDebugLogTimer > 0f)
                ledgeDebugLogTimer -= Time.deltaTime;

            if (wallDebugLogTimer > 0f)
                wallDebugLogTimer -= Time.deltaTime;

            if (wallHitDebugLogTimer > 0f)
                wallHitDebugLogTimer -= Time.deltaTime;

            if (wallSlideBlockDebugLogTimer > 0f)
                wallSlideBlockDebugLogTimer -= Time.deltaTime;

            if (cornerStuckDebugLogTimer > 0f)
                cornerStuckDebugLogTimer -= Time.deltaTime;

            // Input limpio (para wall/ledge/hang)
            float inputX = Mathf.Clamp(input.MoveInput.x, -1f, 1f);
            float inputY = Mathf.Clamp(input.MoveInput.y, -1f, 1f);
            if (Mathf.Abs(inputX) < wallInputDeadzone) inputX = 0f;
            if (Mathf.Abs(inputY) < wallInputDeadzone) inputY = 0f;

            // Timers de ataque (cancel window)
            if (isAttacking)
            {
                attackStateTimer -= Time.deltaTime;
                cancelEnableTimer -= Time.deltaTime;
                canCancelAttack = cancelEnableTimer <= 0f;

                if (attackStateTimer <= 0f)
                {
                    isAttacking = false;
                    canCancelAttack = false;
                }
            }

            // Si ya no estás manteniendo DOWN, se re-habilita wall check (tras drop del hang)
            if (suppressWallWhileDownHeld && inputY > -hangDownThreshold)
                suppressWallWhileDownHeld = false;

            // Si está colgado, solo lógica de hang
            if (isHanging)
            {
                UpdateHanging(inputX, inputY);
                return;
            }

            if (isSwinging)
            {
                UpdateSwinging(inputX, inputY);
                return;
            }

            // Flip por movimiento (si no está lockeado por parry)
            if (!IsMoveLockedByParry && inputX != 0f)
                ApplyFacing((int)Mathf.Sign(inputX));

            CheckGround();
            CheckWall(inputY);

            bool crouchPressed = input.ConsumeCrouchPressed();
            if (crouchPressed)
            {
                if (isGrounded)
                    input.ToggleCrouch();
                else
                    input.ForceStandUp();
            }

            // Timers base
            coyoteTimer = isGrounded ? stats.coyoteTime : (coyoteTimer - Time.deltaTime);
            jumpBufferTimer -= Time.deltaTime;
            dashCooldownTimer -= Time.deltaTime;
            attackCooldownTimer -= Time.deltaTime;

            // Consume inputs
            bool jumpPressed = input.ConsumeJumpPressed();
            bool dashPressed = stats.hasDash && input.ConsumeDashPressed();
            bool attackPressed = input.ConsumeAttackPressed();
            bool interactPressed = input.ConsumeInteractPressed();

            // ✅ Jump desde Crouch/LayDown: NO salta; intenta levantarse
            if (jumpPressed && (IsCrouching || IsLayDown))
            {
                input.ForceStandUp();
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
                TryInteract();

            // Jump normal (buffer + coyote)
            if (jumpBufferTimer > 0f && coyoteTimer > 0f)
            {
                Jump();
                jumpBufferTimer = 0f;
                coyoteTimer = 0f;
            }

            // Wall Jump (buffer)
            if (jumpBufferTimer > 0f && (isWallSliding || wallCoyoteTimer > 0f))
            {
                WallJump();
                jumpBufferTimer = 0f;
            }

            // Jump cut
            if (!input.JumpHeld && rb.velocity.y > 0.01f)
                rb.velocity = new Vector2(rb.velocity.x, rb.velocity.y * stats.jumpCutMultiplier);

            // Recargar air dashes
            if (isGrounded)
                airDashesRemaining = stats.airDashesMax;

            // Ledge grab automático por raycasts desactivado.
            // El agarre se controla con LedgeGrabTrigger + SwingGrab.
        }

        private void FixedUpdate()
        {
            if (stats == null || input == null) return;

            if (isHanging) return;

            if (isSwinging)
            {
                FixedUpdateSwinging();
                return;
            }

            if (isDashing)
            {
                UpdateDash();
                return;
            }

            suppressWallSlideThisFixed = false;
            HandleCornerAntiStuck();
            HandleWallSlide();
            LogWallDebugState();

            // ✅ Parry move lock: no Move()
            if (IsMoveLockedByParry)
            {
                float newVX = Mathf.MoveTowards(rb.velocity.x, 0f, parryMoveStopDecel * Time.fixedDeltaTime);
                rb.velocity = new Vector2(newVX, rb.velocity.y);
                ApplyBetterJumpGravity();
                return;
            }

            Move();
            ApplyBetterJumpGravity();
            if (isWallSliding)
                ApplyWallSlideVelocity();
        }

        // --------------------
        // ATTACK
        // --------------------
        private void StartAttack()
        {
            if (isSwinging) return;

            DropCarriedObject();

            ComputeAndLockAttackDirection();

            animDriver?.TriggerAttack(lockedAttackDirType);

            isAttacking = true;
            canCancelAttack = false;
            attackStateTimer = attackLockSeconds;
            cancelEnableTimer = attackCancelEnableSeconds;

            bool didHit = Attack(lockedAttackDir);

            attackCooldownTimer = attackCooldown;

            if (debugLogs)
                Debug.Log($"[PLAYER ATTACK] dirType={lockedAttackDirType} dir={lockedAttackDir} didHit={didHit} enemy={lastAttackHitEnemy}");

            if (didHit)
            {
                if (lastAttackHitEnemy && hitStopSeconds > 0f)
                    HitStopManager.Request(hitStopSeconds);

                if (lockedAttackDirType == 2 && !isGrounded && pogoBounceForce > 0f)
                    rb.velocity = new Vector2(rb.velocity.x, Mathf.Max(rb.velocity.y, pogoBounceForce));
            }
        }

        private void CancelAttack(string reason)
        {
            isAttacking = false;
            canCancelAttack = false;
            attackStateTimer = 0f;
            cancelEnableTimer = 0f;

            if (debugLogs)
                Debug.Log($"[PLAYER ATTACK] CANCELED by {reason}");
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

            attackDamageTargets.Clear();

            if (!allowVerticalAim)
                dir = new Vector2(facingDir, 0f);

            if (dir.sqrMagnitude < 0.0001f)
                dir = new Vector2(facingDir, 0f);

            float offset = attackOffset;
            if (offset <= 0f)
                offset = Mathf.Max(0.01f, Mathf.Abs(attackPoint.localPosition.x));

            Vector2 hitPos = (Vector2)transform.position + dir.normalized * offset;
            attackPoint.position = hitPos;

            int count = Physics2D.OverlapCircleNonAlloc(hitPos, attackRadius, attackHits, enemyLayer);
            bool didHit = false;
            lastAttackHitEnemy = false;

            for (int i = 0; i < count; i++)
            {
                var hitCol = attackHits[i];
                if (hitCol == null) continue;

                var dmgable =
                    hitCol.GetComponent<Game.Combat.IDamageable>() ??
                    hitCol.GetComponentInParent<Game.Combat.IDamageable>();

                if (dmgable != null)
                {
                    UnityEngine.Object damageTarget = dmgable as UnityEngine.Object;
                    if (damageTarget != null && !attackDamageTargets.Add(damageTarget))
                        continue;

                    didHit = true;
                    if (IsEnemyHit(hitCol, dmgable))
                        lastAttackHitEnemy = true;

                    dmgable.TakeDamage(stats.attackDamage, transform.position);
                    continue;
                }

                var health =
                    hitCol.GetComponent<Game.Combat.Health>() ??
                    hitCol.GetComponentInParent<Game.Combat.Health>();

                if (health != null)
                {
                    if (!attackDamageTargets.Add(health))
                        continue;

                    didHit = true;
                    lastAttackHitEnemy = true;
                    health.TakeDamage(stats.attackDamage, transform.position);
                }
            }

            return didHit;
        }

        private static bool IsEnemyHit(Collider2D hitCol, Game.Combat.IDamageable dmgable)
        {
            if (hitCol != null && hitCol.GetComponentInParent<Game.Enemies.EnemyBase>() != null)
                return true;

            UnityEngine.Object damageObject = dmgable as UnityEngine.Object;
            if (damageObject is Component component && component.GetComponentInParent<Game.Enemies.EnemyBase>() != null)
                return true;

            return false;
        }

        private void TryInteract()
        {
            if (isSwinging) return;
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
            if (isSwinging) return false;
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

        public bool TryEnterSwing(SwingPole pole)
        {
            if (!CanEnterSwing(pole)) return false;

            DropCarriedObject();
            EnterSwing(pole);
            return true;
        }

        public bool CanEnterSwing(SwingPole pole)
        {
            if (pole == null) return false;
            if (isSwinging || isHanging) return false;
            if (swingCooldownTimer > 0f) return false;
            return pole.CanCatch(this, rb);
        }

        public bool TryEnterLedgeGrabPoint(LedgeGrabPoint point)
        {
            if (!CanEnterLedgeGrabPoint(point)) return false;

            ApplyFacing(point.FacingDirection);
            EnterHang(point.HangPosition, point.CornerPosition, point.HasStandPosition, point.StandPosition);
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
            if (isSwinging || isHanging || isDashing || IsCarrying) return false;
            if (ledgeGrabCooldownTimer > 0f) return false;
            if (IsCrouching || IsLayDown) return false;
            if (suppressWallWhileDownHeld) return false;
            if (point.FacingDirection == 0) return false;
            return true;
        }

        private void EnterSwing(SwingPole pole)
        {
            currentSwingPole = pole;
            isSwinging = true;
            isDashing = false;
            isWallSliding = false;
            wallStickTimer = 0f;
            wallCoyoteTimer = 0f;
            jumpBufferTimer = 0f;
            coyoteTimer = 0f;
            parryMoveLockTimer = 0f;

            if (isAttacking)
                CancelAttack("swing");

            input?.ForceStandUp();

            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.gravityScale = defaultGravityScale;
            rb.angularVelocity = 0f;

            Vector2 anchor = pole.AnchorPosition;
            if (pole.SnapOnEnter)
            {
                rb.position = pole.GetInitialPlayerPosition();
                rb.velocity = Vector2.zero;
            }

            swingJoint.connectedBody = pole.ConnectedBody;
            swingJoint.connectedAnchor = pole.GetConnectedAnchor();
            swingJoint.distance = Mathf.Max(0.05f, pole.GetSwingDistance(rb.position));
            swingJoint.enabled = true;

            if (debugLogs)
                Debug.Log($"[SWING] ENTER pole={pole.name} anchor={anchor}");
        }

        private void UpdateSwinging(float inputXClean, float inputYClean)
        {
            input?.ForceStandUp();

            input.ConsumeAttackPressed();
            input.ConsumeDashPressed();
            input.ConsumeParryPressed();

            if (input.ConsumeJumpPressed())
            {
                ExitSwing(true, inputXClean);
                return;
            }

            bool downHeld = inputYClean <= -swingDownThreshold;
            if (downHeld || input.ConsumeInteractPressed())
                ExitSwing(false, inputXClean);
        }

        private void FixedUpdateSwinging()
        {
            if (currentSwingPole == null || swingJoint == null || !swingJoint.enabled)
            {
                ExitSwing(false);
                return;
            }

            ClampSwingArc();

            float inputX = Mathf.Clamp(input.MoveInput.x, -1f, 1f);
            if (Mathf.Abs(inputX) < wallInputDeadzone) inputX = 0f;

            if (inputX != 0f)
            {
                ApplyFacing((int)Mathf.Sign(inputX));
                rb.AddForce(Vector2.right * inputX * swingMotorForce, ForceMode2D.Force);
            }

            ClampSwingArc();
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
            if (!isSwinging && (swingJoint == null || !swingJoint.enabled)) return;

            isSwinging = false;
            currentSwingPole = null;

            if (swingJoint != null)
            {
                swingJoint.enabled = false;
                swingJoint.connectedBody = null;
            }

            rb.gravityScale = defaultGravityScale;
            swingCooldownTimer = swingExitCooldown;

            Vector2 keptVelocity = rb.velocity * swingVelocityKeepMultiplier;
            if (jumpRelease)
            {
                int jumpDir = inputXClean != 0f ? (int)Mathf.Sign(inputXClean) : facingDir;
                ApplyFacing(jumpDir);
                rb.velocity = keptVelocity + new Vector2(jumpDir * swingJumpForceX, swingJumpForceY);
            }
            else
            {
                rb.velocity = keptVelocity;
            }

            if (debugLogs)
                Debug.Log($"[SWING] EXIT jump={jumpRelease} velocity={rb.velocity}");
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
        private void Move()
        {
            float inputX = Mathf.Clamp(input.MoveInput.x, -1f, 1f);

            if (wallJumpLockTimer > 0f)
            {
                wallJumpLockTimer -= Time.fixedDeltaTime;
                if (inputX != 0f && lastWallJumpSide != 0 && Mathf.Sign(inputX) == lastWallJumpSide)
                    inputX = 0f;
            }

            float stanceMult = 1f;
            if (IsLayDown) stanceMult = layDownSpeedMultiplier;
            else if (IsCrouching) stanceMult = crouchSpeedMultiplier;
            if (IsCarrying) stanceMult *= carryingMoveSpeedMultiplier;

            float targetSpeed = inputX * stats.maxRunSpeed * stanceMult;

            float accel = isGrounded ? stats.groundAcceleration : stats.airAcceleration;
            float decel = isGrounded ? stats.groundDeceleration : stats.airDeceleration;

            float rate = Mathf.Abs(inputX) > 0.01f ? accel : decel;

            bool changingDirection =
                Mathf.Abs(targetSpeed) > 0.01f &&
                Mathf.Sign(targetSpeed) != Mathf.Sign(rb.velocity.x) &&
                Mathf.Abs(rb.velocity.x) > 0.1f;

            if (changingDirection)
                rate *= stats.turnAccelerationMultiplier;

            float newVelX = Mathf.MoveTowards(rb.velocity.x, targetSpeed, rate * Time.fixedDeltaTime);

            if (isGrounded && Mathf.Abs(inputX) < 0.01f && Mathf.Abs(newVelX) < 0.02f)
                newVelX = 0f;

            rb.velocity = new Vector2(newVelX, rb.velocity.y);
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
            float jumpForce = stats.jumpForce * (IsCarrying ? carryingJumpForceMultiplier : 1f);
            rb.velocity = new Vector2(rb.velocity.x, jumpForce);
        }

        private void ApplyBetterJumpGravity()
        {
            float vy = rb.velocity.y;

            if (vy < stats.maxFallSpeed)
                rb.velocity = new Vector2(rb.velocity.x, stats.maxFallSpeed);

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

        // --------------------
        // DASH
        // --------------------
        private void TryStartDash()
        {
            if (isSwinging) return;
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
            isDashing = true;
            dashTimer = stats.dashDuration;
            dashCooldownTimer = stats.dashCooldown;

            rb.gravityScale = 0f;
            rb.velocity = new Vector2(facingDir * stats.dashSpeed, 0f);
        }

        private void UpdateDash()
        {
            dashTimer -= Time.fixedDeltaTime;
            if (dashTimer <= 0f)
                EndDash();
        }

        private void EndDash()
        {
            isDashing = false;
            rb.gravityScale = defaultGravityScale;
        }

        // --------------------
        // WALL
        // --------------------
        private void HandleWallSlide()
        {
            float inputX = input != null ? Mathf.Clamp(input.MoveInput.x, -1f, 1f) : 0f;
            if (Mathf.Abs(inputX) < wallInputDeadzone) inputX = 0f;

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

            if (inputX == 0f || Mathf.Sign(inputX) != slideSide)
            {
                LogWallSlideBlocked(inputX == 0f ? "input deadzone/zero" : "input not pushing into wall", inputX);
                return;
            }

            isWallSliding = true;
            ApplyWallSlideVelocity();
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

        private void LogWallSlideBlocked(string reason, float inputX)
        {
            if (!logWallDebug) return;
            if (!isTouchingWall || isWallSliding) return;
            if (wallSlideBlockDebugLogTimer > 0f) return;

            wallSlideBlockDebugLogTimer = wallDebugLogInterval;
            Debug.Log(
                $"[WALL SLIDE BLOCKED] reason={reason} inputX={inputX:0.00} wallSide={wallSide} " +
                $"grounded={isGrounded} vy={rb.velocity.y:0.00} " +
                $"requirePush={requirePushIntoWallForSlide} sameWallBlocked={IsSameWallClimbBlocked(wallSide)} " +
                $"suppressFixed={suppressWallSlideThisFixed}");
        }

        private void WallJump()
        {
            if (suppressWallWhileDownHeld) return;

            bool canWallJump = isWallSliding || wallCoyoteTimer > 0f;
            if (!canWallJump) return;

            int activeWallSide = wallSide != 0 ? wallSide : wallCoyoteSide;
            if (activeWallSide == 0)
                activeWallSide = facingDir;

            int jumpDir = -activeWallSide;
            int jumpedWallSide = activeWallSide;
            if (IsSameWallClimbBlocked(jumpedWallSide)) return;

            float jumpMult = IsCarrying ? carryingJumpForceMultiplier : 1f;
            float jumpX = jumpDir * stats.wallJumpForceX * jumpMult;
            float keptMomentumX = Mathf.Max(0f, rb.velocity.x * jumpDir) * stats.wallJumpMomentumKeep * jumpDir;
            rb.velocity = new Vector2(jumpX + keptMomentumX, stats.wallJumpForceY * jumpMult);

            ApplyFacing(jumpDir);

            lastWallJumpSide = jumpedWallSide;
            lastWallJumpY = rb.position.y;
            wallJumpLockTimer = wallJumpLockTime;
            wallStickTimer = 0f;
            wallCoyoteTimer = 0f;
            wallStickSide = 0;
            wallCoyoteSide = 0;
            WallJumped?.Invoke(jumpedWallSide);
        }

        // --------------------
        // CHECKS
        // --------------------
        private void CheckGround()
        {
            if (groundCheck == null) return;

            int mask = groundLayer.value != 0 ? groundLayer.value : SolidMask();
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
                if (candidate.normal.y < groundNormalMinY) continue;
                if (candidate.normal.y <= bestNormalY) continue;

                bestNormalY = candidate.normal.y;
                bestHit = candidate;
            }

            isGrounded = bestHit.collider != null;

            if (logLedgeDebug && bestHit.collider != lastGroundDebugCollider)
            {
                lastGroundDebugCollider = bestHit.collider;
                Debug.Log($"[GROUND DEBUG] grounded={isGrounded} hit={DescribeCollider2D(bestHit.collider)} point={bestHit.point} normal={bestHit.normal}");
            }
        }

        private void CheckWall(float inputYClean)
        {
            if (capsule == null) return;

            bool downHeld = inputYClean <= -hangDownThreshold;
            if (disableWallWhileDownHeldAfterDrop && suppressWallWhileDownHeld && downHeld)
            {
                isTouchingWall = false;
                wallSide = 0;
                wallStickTimer = 0f;
                wallCoyoteTimer = 0f;
                wallStickSide = 0;
                wallCoyoteSide = 0;
                return;
            }

            float inputX = Mathf.Clamp(input.MoveInput.x, -1f, 1f);
            if (Mathf.Abs(inputX) < wallInputDeadzone) inputX = 0f;

            bool hasRightWall = TryGetWallHitOnSide(1, out _);
            bool hasLeftWall = TryGetWallHitOnSide(-1, out _);

            isTouchingWall = false;
            wallSide = 0;

            if (hasRightWall && hasLeftWall)
            {
                if (inputX != 0f)
                {
                    isTouchingWall = true;
                    wallSide = (int)Mathf.Sign(inputX);
                }
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

            if (isGrounded)
            {
                ClearSameWallClimbBlock();
                wallStickTimer = 0f;
                wallCoyoteTimer = 0f;
                wallStickSide = 0;
                wallCoyoteSide = 0;
                return;
            }

            if (isTouchingWall)
            {
                if (lastWallJumpSide != 0 && wallSide != lastWallJumpSide)
                    ClearSameWallClimbBlock();

                wallStickTimer = wallStickTime;
                wallCoyoteTimer = wallCoyoteTime;
                wallStickSide = wallSide;
                wallCoyoteSide = wallSide;
            }
            else
            {
                if (lastWallJumpSide != 0 && rb.position.y < lastWallJumpY - sameWallJumpYReleaseMargin)
                    ClearSameWallClimbBlock();

                wallStickTimer = Mathf.Max(0f, wallStickTimer - Time.deltaTime);
                wallCoyoteTimer = Mathf.Max(0f, wallCoyoteTimer - Time.deltaTime);
                wallStickTimer = 0f;
                wallStickSide = 0;

                if (wallCoyoteTimer <= 0f)
                    wallCoyoteSide = 0;
            }
        }

        private bool TryGetWallHitOnSide(int side, out RaycastHit2D hit)
        {
            hit = default;
            if (side == 0 || capsule == null) return false;

            Vector2 dir = side > 0 ? Vector2.right : Vector2.left;
            ContactFilter2D filter = new ContactFilter2D();
            filter.SetLayerMask(SolidMask());
            filter.useLayerMask = true;
            filter.useTriggers = false;

            int hitCount = capsule.Cast(dir, filter, wallHits, wallCheckDistance);
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
            return normal.x * side <= -wallNormalMinX;
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

            if (isGrounded || isHanging || isSwinging || isDashing)
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
            if (side != lastWallJumpSide) return false;
            return rb.position.y >= lastWallJumpY - sameWallJumpYReleaseMargin;
        }

        private void ClearSameWallClimbBlock()
        {
            lastWallJumpSide = 0;
            lastWallJumpY = 0f;
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
            isHanging = true;
            isWallSliding = false;
            ledgeCandidateTimer = 0f;
            wallStickTimer = 0f;
            wallCoyoteTimer = 0f;
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

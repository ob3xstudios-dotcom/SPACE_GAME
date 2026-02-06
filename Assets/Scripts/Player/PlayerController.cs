using System;
using System.Reflection;
using UnityEngine;
using Game.Input;

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

        [Header("Facing / Visual")]
        [SerializeField] private Transform visualRoot;

        // --------------------
        // GROUND / WALL
        // --------------------
        [Header("Ground Check")]
        public Transform groundCheck;
        public float groundCheckRadius = 0.2f;
        public LayerMask groundLayer;
        [Tooltip("Solo cuenta como suelo si la normal apunta hacia arriba (evita que pared lateral te ponga grounded).")]
        [SerializeField, Range(0f, 1f)] private float groundNormalMinY = 0.55f;

        [Header("Wall Check")]
        public Transform wallCheck;
        public float wallCheckDistance = 0.3f;
        public LayerMask wallLayer;

        [Header("Tuning (Designer) - Wall Feel")]
        [SerializeField, Range(0.1f, 3f)] private float wallSlideSpeedMultiplier = 0.6f;
        [SerializeField, Range(0f, 0.3f)] private float wallStickTime = 0.12f;
        [SerializeField, Range(0f, 0.3f)] private float wallCoyoteTime = 0.10f;
        [SerializeField, Range(0.05f, 0.3f)] private float wallJumpLockTime = 0.14f;

        [Header("Tuning (Designer) - Wall Input")]
        [SerializeField, Range(0.01f, 0.5f)] private float wallInputDeadzone = 0.2f;
        [SerializeField] private bool requirePushIntoWallForSlide = true;

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

        [Tooltip("Offset final aplicado al punto corner (X se multiplica por facingDir).")]
        [SerializeField] private Vector2 ledgeHangOffset = new Vector2(0.10f, -0.22f);

        [Header("Ledge Hang / Exit")]
        [SerializeField] private float ledgeClimbJumpY = 10f;
        [SerializeField] private float ledgeClimbJumpX = 1.5f;

        [Tooltip("Si ON, exige empujar hacia la pared para poder agarrar. En prototipo suele ir mejor OFF.")]
        [SerializeField] private bool requirePushIntoWallToGrab = false;

        [Header("Hang - PM Rules")]
        [Tooltip("Umbral para considerar DOWN (MoveInput.y <= -threshold).")]
        [SerializeField, Range(0.1f, 1f)] private float hangDownThreshold = 0.5f;

        [Tooltip("Si mantienes DOWN tras soltarte del hang, se cancela el wall check hasta soltar DOWN.")]
        [SerializeField] private bool disableWallWhileDownHeldAfterDrop = true;

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

        // --------------------
        // RUNTIME STATE
        // --------------------
        private float attackCooldownTimer;

        private Rigidbody2D rb;
        private CapsuleCollider2D capsule;
        private PlayerAnimatorDriver animDriver;

        private MonoBehaviour stealthKill; // lo buscamos por tipo para no acoplar
        private MethodInfo stealthKillTryMethod;

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
        private float wallStickTimer;
        private float wallCoyoteTimer;

        private Coroutine hitStopCo;

        // HANG
        private bool isHanging;
        private float ledgeGrabCooldownTimer;
        private Vector2 hangPositionLocked;

        private RigidbodyType2D cachedBodyType;
        private RigidbodyConstraints2D cachedConstraints;
        private float cachedGravityScale;

        private bool suppressWallWhileDownHeld;

        // Optional: levantar al pulsar jump
        private MethodInfo forceStandUpMethod;

        public bool FacingLeft => facingDir < 0;
        public bool IsGrounded => isGrounded;
        public bool IsAttacking => isAttacking;
        public bool IsHanging => isHanging;

        // Para Animator (si lo usáis)
        public bool IsCrouching => input != null && GetBoolProp(input, "IsCrouching");
        public bool IsLayDown => input != null && GetBoolProp(input, "IsLayDown");

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            capsule = GetComponent<CapsuleCollider2D>();
            animDriver = GetComponent<PlayerAnimatorDriver>();

            if (input == null) input = GetComponent<InputReader>();
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

            if (ledgeTopLayer.value == 0)
                ledgeTopLayer = groundLayer | wallLayer;

            ApplyFacing(1);

            // Optional hooks via reflection
            forceStandUpMethod = input != null
                ? input.GetType().GetMethod("ForceStandUp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                : null;

            // Buscar PlayerStealthKill sin acoplar a clase concreta
            // (si existe, intentamos llamar TryStealthKill())
            var comps = GetComponents<MonoBehaviour>();
            foreach (var c in comps)
            {
                if (c == null) continue;
                if (c.GetType().Name == "PlayerStealthKill")
                {
                    stealthKill = c;
                    stealthKillTryMethod = c.GetType().GetMethod("TryStealthKill", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    break;
                }
            }
        }

        private void Update()
        {
            if (stats == null || input == null) return;

            // cooldowns
            ledgeGrabCooldownTimer -= Time.deltaTime;

            if (parryMoveLockTimer > 0f)
                parryMoveLockTimer -= Time.deltaTime;

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

            // Flip por movimiento (si no está lockeado por parry)
            if (!IsMoveLockedByParry && inputX != 0f)
                ApplyFacing((int)Mathf.Sign(inputX));

            CheckGround();
            CheckWall(inputY);

            // Timers base
            coyoteTimer = isGrounded ? stats.coyoteTime : (coyoteTimer - Time.deltaTime);
            jumpBufferTimer -= Time.deltaTime;
            dashCooldownTimer -= Time.deltaTime;
            attackCooldownTimer -= Time.deltaTime;

            // Consume inputs
            bool jumpPressed = input.ConsumeJumpPressed();
            bool dashPressed = stats.hasDash && input.ConsumeDashPressed();
            bool attackPressed = input.ConsumeAttackPressed();

            // ✅ Jump desde Crouch/LayDown: NO salta; intenta levantarse
            if (jumpPressed && (IsCrouching || IsLayDown))
            {
                forceStandUpMethod?.Invoke(input, null);
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

                TryStartDash();
            }

            // Cancel con JUMP
            if (jumpPressed && isAttacking && canCancelAttack)
                CancelAttack("jump");

            // ✅ ATAQUE:
            // Si estás en Crouch/LayDown, primero intentamos stealth kill con el MISMO botón.
            if (attackPressed)
            {
                bool didStealthKill = false;

                if ((IsCrouching || IsLayDown) && stealthKill != null && stealthKillTryMethod != null)
                {
                    try
                    {
                        didStealthKill = (bool)stealthKillTryMethod.Invoke(stealthKill, null);
                    }
                    catch { didStealthKill = false; }
                }

                if (!didStealthKill)
                {
                    if (!isDashing && !isAttacking && attackCooldownTimer <= 0f)
                        StartAttack();
                }
                else
                {
                    attackCooldownTimer = Mathf.Max(attackCooldownTimer, attackCooldown * 0.25f);
                }
            }

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

            // Ledge grab (después de checks)
            if (enableLedgeGrab && !suppressWallWhileDownHeld)
            {
                // Recomendación: no permitir hang si estás crouch/laydown
                if (!IsCrouching && !IsLayDown)
                    TryLedgeGrab(inputX);
            }
        }

        private void FixedUpdate()
        {
            if (stats == null || input == null) return;

            if (isHanging) return;

            if (isDashing)
            {
                UpdateDash();
                return;
            }

            HandleWallSlide();

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
        }

        // --------------------
        // ATTACK
        // --------------------
        private void StartAttack()
        {
            ComputeAndLockAttackDirection();

            animDriver?.TriggerAttack(lockedAttackDirType);

            isAttacking = true;
            canCancelAttack = false;
            attackStateTimer = attackLockSeconds;
            cancelEnableTimer = attackCancelEnableSeconds;

            bool didHit = Attack(lockedAttackDir);

            attackCooldownTimer = attackCooldown;

            if (debugLogs)
                Debug.Log($"[PLAYER ATTACK] dirType={lockedAttackDirType} dir={lockedAttackDir} didHit={didHit}");

            if (didHit)
            {
                if (hitStopSeconds > 0f)
                    StartHitStop(hitStopSeconds);

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

        private void StartHitStop(float seconds)
        {
            if (hitStopCo != null) StopCoroutine(hitStopCo);
            hitStopCo = StartCoroutine(HitStopRoutine(seconds));
        }

        private static System.Collections.IEnumerator HitStopRoutine(float seconds)
        {
            float prev = Time.timeScale;
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(seconds);
            Time.timeScale = prev <= 0f ? 1f : prev;
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

            for (int i = 0; i < count; i++)
            {
                var hitCol = attackHits[i];
                if (hitCol == null) continue;

                var dmgable =
                    hitCol.GetComponent<Game.Combat.IDamageable>() ??
                    hitCol.GetComponentInParent<Game.Combat.IDamageable>();

                if (dmgable != null)
                {
                    didHit = true;
                    dmgable.TakeDamage(stats.attackDamage, transform.position);
                    continue;
                }

                var health =
                    hitCol.GetComponent<Game.Combat.Health>() ??
                    hitCol.GetComponentInParent<Game.Combat.Health>();

                if (health != null)
                {
                    didHit = true;
                    health.TakeDamage(stats.attackDamage, transform.position);
                }
            }

            return didHit;
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
                return;
            }

            float stanceMult = 1f;
            if (IsLayDown) stanceMult = layDownSpeedMultiplier;
            else if (IsCrouching) stanceMult = crouchSpeedMultiplier;

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
            rb.velocity = new Vector2(rb.velocity.x, stats.jumpForce);
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
            if (isDashing) return;
            if (dashCooldownTimer > 0f) return;

            if (!isGrounded)
            {
                if (!stats.allowAirDash) return;
                if (airDashesRemaining <= 0) return;
                airDashesRemaining--;
            }

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
            if (suppressWallWhileDownHeld)
            {
                isWallSliding = false;
                return;
            }

            isWallSliding = false;

            if (isGrounded) return;
            if (rb.velocity.y >= 0f) return;
            if (!(isTouchingWall || wallStickTimer > 0f)) return;

            float inputX = input.MoveInput.x;
            if (Mathf.Abs(inputX) < wallInputDeadzone) inputX = 0f;

            if (requirePushIntoWallForSlide)
            {
                int side = (wallSide != 0) ? wallSide : facingDir;
                bool pushingIntoWall = (inputX != 0f) && (Mathf.Sign(inputX) == side);
                if (!pushingIntoWall) return;
            }

            isWallSliding = true;

            float targetWallSlideSpeed = stats.wallSlideSpeed * wallSlideSpeedMultiplier;
            if (rb.velocity.y < targetWallSlideSpeed)
                rb.velocity = new Vector2(rb.velocity.x, targetWallSlideSpeed);
        }

        private void WallJump()
        {
            if (suppressWallWhileDownHeld) return;

            bool canWallJump = isWallSliding || wallCoyoteTimer > 0f;
            if (!canWallJump) return;

            int jumpDir = (wallSide != 0) ? -wallSide : -facingDir;
            rb.velocity = new Vector2(jumpDir * stats.wallJumpForceX, stats.wallJumpForceY);

            ApplyFacing(jumpDir);

            wallJumpLockTimer = wallJumpLockTime;
            wallStickTimer = 0f;
            wallCoyoteTimer = 0f;
        }

        // --------------------
        // CHECKS
        // --------------------
        private void CheckGround()
        {
            if (groundCheck == null) return;

            float dist = groundCheckRadius + 0.06f;
            RaycastHit2D hit = Physics2D.Raycast(groundCheck.position, Vector2.down, dist, groundLayer);
            isGrounded = hit.collider != null && hit.normal.y >= groundNormalMinY;
        }

        private void CheckWall(float inputYClean)
        {
            if (wallCheck == null) return;

            bool downHeld = inputYClean <= -hangDownThreshold;
            if (disableWallWhileDownHeldAfterDrop && suppressWallWhileDownHeld && downHeld)
            {
                isTouchingWall = false;
                wallSide = 0;
                wallStickTimer = 0f;
                wallCoyoteTimer = 0f;
                return;
            }

            Vector2 dir = (facingDir > 0) ? Vector2.right : Vector2.left;
            var hit = Physics2D.Raycast(wallCheck.position, dir, wallCheckDistance, wallLayer);

            isTouchingWall = hit.collider != null;
            wallSide = isTouchingWall ? facingDir : 0;

            if (isGrounded)
            {
                wallStickTimer = 0f;
                wallCoyoteTimer = 0f;
                return;
            }

            if (isTouchingWall)
            {
                wallStickTimer = wallStickTime;
                wallCoyoteTimer = wallCoyoteTime;
            }
            else
            {
                wallStickTimer = Mathf.Max(0f, wallStickTimer - Time.deltaTime);
                wallCoyoteTimer = Mathf.Max(0f, wallCoyoteTimer - Time.deltaTime);
            }
        }

        // --------------------
        // LEDGE GRAB / HANG
        // --------------------
        private void TryLedgeGrab(float inputXClean)
        {
            if (ledgeGrabCooldownTimer > 0f) return;
            if (isHanging) return;
            if (isDashing) return;
            if (isGrounded) return;
            if (rb.velocity.y > ledgeGrabMaxVerticalSpeedToAllow) return;

            if (requirePushIntoWallToGrab)
            {
                if (inputXClean == 0f) return;
                if (Mathf.Sign(inputXClean) != facingDir) return;
            }

            Bounds b = capsule.bounds;
            Vector2 faceDir = (facingDir > 0) ? Vector2.right : Vector2.left;

            float skin = 0.02f;
            float halfWidth = b.extents.x;
            float topY = b.max.y;

            Vector2 handsOrigin = new Vector2(
                b.center.x + faceDir.x * (halfWidth + skin),
                topY - ledgeHandsFromTop
            );

            RaycastHit2D wallHit = Physics2D.Raycast(handsOrigin, faceDir, ledgeForwardCheck, wallLayer);
            if (!wallHit) return;

            Vector2 headOrigin = handsOrigin + Vector2.up * ledgeHeadClearance;
            RaycastHit2D headBlock = Physics2D.Raycast(headOrigin, faceDir, ledgeForwardCheck, wallLayer);
            if (headBlock) return;

            Vector2 topCheckOrigin = headOrigin + faceDir * ledgeTopForward;
            RaycastHit2D topGround = Physics2D.Raycast(topCheckOrigin, Vector2.down, ledgeTopDown, ledgeTopLayer);
            if (!topGround) return;

            Vector2 corner = new Vector2(wallHit.point.x, topGround.point.y);

            float hangX = corner.x - faceDir.x * hangWallSkin + (ledgeHangOffset.x * facingDir);
            float hangY = corner.y + ledgeHangOffset.y;

            Vector2 targetHangPos = new Vector2(hangX, hangY);
            Vector2 current = transform.position;

            if (Vector2.Distance(current, targetHangPos) > maxHangSnapDistance)
                targetHangPos = Vector2.MoveTowards(current, targetHangPos, maxHangSnapDistance);

            EnterHang(targetHangPos, corner);
        }

        private void EnterHang(Vector2 lockedPos, Vector2 corner)
        {
            isHanging = true;
            isWallSliding = false;

            cachedBodyType = rb.bodyType;
            cachedConstraints = rb.constraints;
            cachedGravityScale = rb.gravityScale;

            hangPositionLocked = lockedPos;

            transform.position = hangPositionLocked;
            rb.velocity = Vector2.zero;
            rb.gravityScale = 0f;

            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.constraints = RigidbodyConstraints2D.FreezePosition | RigidbodyConstraints2D.FreezeRotation;

            ledgeGrabCooldownTimer = ledgeGrabCooldown;
            suppressWallWhileDownHeld = false;

            if (debugLogs)
                Debug.Log($"[HANG] ENTER lockPos={hangPositionLocked} corner={corner} facing={facingDir}");
        }

        private void ExitHang(string reason = "")
        {
            isHanging = false;

            rb.bodyType = cachedBodyType;
            rb.constraints = cachedConstraints;
            rb.gravityScale = cachedGravityScale;

            ledgeGrabCooldownTimer = ledgeGrabCooldown;

            if (debugLogs)
                Debug.Log($"[HANG] EXIT {(string.IsNullOrEmpty(reason) ? "" : $"({reason})")} pos={transform.position}");
        }

        private void UpdateHanging(float inputXClean, float inputYClean)
        {
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
                ExitHang("jump");
                rb.velocity = new Vector2(ledgeClimbJumpX * facingDir, ledgeClimbJumpY);
                wallJumpLockTimer = 0f;
                return;
            }

            // Dash desde hang: alejarse
            if (stats.hasDash && input.ConsumeDashPressed())
            {
                int dashDir = -facingDir;
                ExitHang("dash-away");
                ApplyFacing(dashDir);
                StartDashOverride(new Vector2(dashDir, 0f));
                return;
            }
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

        // --------------------
        // UTIL (reflection-safe)
        // --------------------
        private static bool GetBoolProp(object obj, string propName)
        {
            if (obj == null) return false;
            try
            {
                var p = obj.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(bool))
                    return (bool)p.GetValue(obj);
                var f = obj.GetType().GetField(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(bool))
                    return (bool)f.GetValue(obj);
            }
            catch { }
            return false;
        }
    }
}

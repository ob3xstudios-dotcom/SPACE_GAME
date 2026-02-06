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
        public bool debugLogs = true;

        [SerializeField] private Transform visualRoot;

        [Header("Ground Check")]
        public Transform groundCheck;
        public float groundCheckRadius = 0.2f;
        public LayerMask groundLayer;
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

        /// <summary>Bloquea SOLO movimiento horizontal durante seconds (parry).</summary>
        public void LockMovement(float seconds)
        {
            parryMoveLockTimer = Mathf.Max(parryMoveLockTimer, Mathf.Max(0f, seconds));
            rb.velocity = new Vector2(0f, rb.velocity.y);
        }

        private float attackCooldownTimer;

        private Rigidbody2D rb;
        private CapsuleCollider2D capsule;
        private PlayerAnimatorDriver animDriver;
        private PlayerStealthKill stealthKill;

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
        private int lockedAttackDirType = 0; // 0=Side,1=Up,2=Down

        private bool isAttacking;
        private bool canCancelAttack;
        private float attackStateTimer;
        private float cancelEnableTimer;

        private int wallSide;
        private float wallStickTimer;
        private float wallCoyoteTimer;

        private Coroutine hitStopCo;


        public bool FacingLeft => facingDir < 0;
        public bool IsGrounded => isGrounded;
        public bool IsAttacking => isAttacking;

        // ✅ Para que compile con el AnimatorDriver que lee IsHanging
        public bool IsHanging => false;

        // ✅ Para el Animator
        public bool IsCrouching => input != null && input.IsCrouching;
        public bool IsLayDown => input != null && input.IsLayDown;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            capsule = GetComponent<CapsuleCollider2D>();
            animDriver = GetComponent<PlayerAnimatorDriver>();
            stealthKill = GetComponent<PlayerStealthKill>();

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

            ApplyFacing(1);
        }

        private void Update()
        {
            if (stats == null || input == null) return;

            // Parry move-lock
            if (parryMoveLockTimer > 0f)
                parryMoveLockTimer -= Time.deltaTime;

            // Input limpio (para wall feel)
            float inputX = Mathf.Clamp(input.MoveInput.x, -1f, 1f);
            float inputY = Mathf.Clamp(input.MoveInput.y, -1f, 1f);
            if (Mathf.Abs(inputX) < wallInputDeadzone) inputX = 0f;
            if (Mathf.Abs(inputY) < wallInputDeadzone) inputY = 0f;

            // Attack lock timers
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

            // ✅ ATTACK botón único:
            // En Crouch/LayDown intentamos stealth kill primero; si no, ataque normal.
            if (attackPressed)
            {
                bool didStealthKill = false;

                if (stealthKill != null && (IsCrouching || IsLayDown))
                    didStealthKill = stealthKill.TryStealthKill();

                if (!didStealthKill)
                {
                    if (!isDashing && !isAttacking && attackCooldownTimer <= 0f)
                        StartAttack();
                }
                else
                {
                    // mini cooldown tras kill para que no se sienta “spam”
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
        }

        private void FixedUpdate()
        {
            if (stats == null || input == null) return;

            if (isDashing)
            {
                UpdateDash();
                return;
            }

            HandleWallSlide();

            // Parry lock: no Move()
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

        private void StartAttack()
        {
            ComputeAndLockAttackDirection();

            animDriver?.TriggerAttack(lockedAttackDirType);

            isAttacking = true;
            canCancelAttack = false;
            attackStateTimer = attackLockSeconds;
            cancelEnableTimer = attackCancelEnableSeconds;

            bool didHit = Attack(lockedAttackDir);

            if (debugLogs)
                Debug.Log($"[PLAYER ATTACK] dirType={lockedAttackDirType} dir={lockedAttackDir} didHit={didHit}");

            attackCooldownTimer = attackCooldown;

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

        private void HandleWallSlide()
        {
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
            bool canWallJump = isWallSliding || wallCoyoteTimer > 0f;
            if (!canWallJump) return;

            int jumpDir = (wallSide != 0) ? -wallSide : -facingDir;

            rb.velocity = new Vector2(jumpDir * stats.wallJumpForceX, stats.wallJumpForceY);
            ApplyFacing(jumpDir);

            wallJumpLockTimer = wallJumpLockTime;
            wallStickTimer = 0f;
            wallCoyoteTimer = 0f;
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

        private bool Attack(Vector2 dir)
        {
            if (attackPoint == null)
            {
                if (debugLogs) Debug.LogWarning("[PLAYER ATTACK] AttackPoint NO asignado");
                return false;
            }

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
    }
}

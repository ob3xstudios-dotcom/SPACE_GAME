using UnityEngine;

namespace Game.Player
{
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(PlayerController))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerAnimatorDriver : MonoBehaviour
    {
        [Header("Animator Params (deben existir en el Animator)")]
        [SerializeField] private string speedParam = "Speed";
        [SerializeField] private string runAnimSpeedParam = "RunAnimSpeed";
        [SerializeField] private string groundedParam = "Grounded";
        [SerializeField] private string vyParam = "Vy";
        [SerializeField] private string hangingParam = "Hanging";
        [SerializeField] private string wallSlidingParam = "WallSliding";
        [SerializeField] private string wallSlideEdgeParam = "WallSlideEdge";
        [SerializeField] private string wallSideParam = "WallSide";

        [Header("Jump Params")]
        [SerializeField] private string jumpStartTrigger = "JumpStart";
        [SerializeField] private string jumpEndTrigger = "JumpEnd";
        [SerializeField] private string jumpStartStateName = "Monkey_JumpStart";
        [SerializeField] private string wallImpactTrigger = "WallImpact";
        [SerializeField] private string wallJumpStartTrigger = "WallJumpStart";
        [SerializeField] private string wallImpactStateName = "Base Layer.Monkey_WallImpact";
        [SerializeField] private string wallSlideStateName = "Base Layer.Monkey_WallSlide";
        [SerializeField] private string wallJumpStartStateName = "Base Layer.Monkey_WallJumpStart";
        [SerializeField] private string jumpEndStateName = "Base Layer.Monkey_JumpEnd";
        [SerializeField] private string idleStateName = "Base Layer.Locomotion.Monkey_Idle";
        [SerializeField] private string runStateName = "Base Layer.Locomotion.Monkey_Run";
        [SerializeField] private string fallingStateName = "Base Layer.Locomotion.Monkey_Falling";

        [Header("Attack Params")]
        [SerializeField] private string attackTrigger = "Attack";
        [Tooltip("Int para seleccionar anim: 0=Side, 1=Up, 2=Down")]
        [SerializeField] private string attackDirIntParam = "AttackDir";

        [Header("Parry Params")]
        [SerializeField] private string parryTrigger = "Parry";

        [Header("Crouch / LayDown Params")]
        [SerializeField] private string crouchBool = "Crouch";
        [SerializeField] private string layDownBool = "LayDown";

        [Header("Tuning")]
        [SerializeField, Range(0f, 1f)] private float movingThreshold = 0.05f;
        [SerializeField, Range(0.1f, 1f)] private float minRunAnimSpeed = 0.35f;
        [SerializeField, Range(1f, 2f)] private float maxRunAnimSpeed = 1.25f;
        [SerializeField, Range(0f, 0.25f)] private float jumpEndVisualLockTime = 0.25f;

        private Animator anim;
        private PlayerController controller;
        private Rigidbody2D rb;

        private int speedHash, runAnimSpeedHash, groundedHash, vyHash, hangingHash;
        private int wallSlidingHash, wallSlideEdgeHash, wallSideHash;
        private int jumpStartHash, jumpEndHash, wallImpactHash, wallJumpStartHash;
        private int attackTrigHash, attackDirHash;
        private int parryTrigHash;
        private int crouchHash, layDownHash;
        private bool hasWallSlidingParam, hasWallSlideEdgeParam, hasWallSideParam, hasJumpStartParam, hasJumpEndParam;
        private bool hasWallImpactParam, hasWallJumpStartParam;
        private bool hasGroundedSample, wasGrounded, wasTouchingWall, wasWallSliding;
        private bool wallSlideEdgeLatched;
        private float jumpEndVisualLockTimer;

        private int cachedAttackDir = 0;

        private void Awake()
        {
            anim = GetComponent<Animator>();
            controller = GetComponent<PlayerController>();
            rb = GetComponent<Rigidbody2D>();

            speedHash = Animator.StringToHash(speedParam);
            runAnimSpeedHash = Animator.StringToHash(runAnimSpeedParam);
            groundedHash = Animator.StringToHash(groundedParam);
            vyHash = Animator.StringToHash(vyParam);
            hangingHash = Animator.StringToHash(hangingParam);
            wallSlidingHash = Animator.StringToHash(wallSlidingParam);
            wallSlideEdgeHash = Animator.StringToHash(wallSlideEdgeParam);
            wallSideHash = Animator.StringToHash(wallSideParam);
            jumpStartHash = Animator.StringToHash(jumpStartTrigger);
            jumpEndHash = Animator.StringToHash(jumpEndTrigger);
            wallImpactHash = Animator.StringToHash(wallImpactTrigger);
            wallJumpStartHash = Animator.StringToHash(wallJumpStartTrigger);

            attackTrigHash = Animator.StringToHash(attackTrigger);
            attackDirHash = Animator.StringToHash(attackDirIntParam);

            parryTrigHash = Animator.StringToHash(parryTrigger);

            crouchHash = Animator.StringToHash(crouchBool);
            layDownHash = Animator.StringToHash(layDownBool);

            hasWallSlidingParam = HasAnimatorParameter(wallSlidingHash, AnimatorControllerParameterType.Bool);
            hasWallSlideEdgeParam = HasAnimatorParameter(wallSlideEdgeHash, AnimatorControllerParameterType.Bool);
            hasWallSideParam = HasAnimatorParameter(wallSideHash, AnimatorControllerParameterType.Int);
            hasJumpStartParam = HasAnimatorParameter(jumpStartHash, AnimatorControllerParameterType.Trigger);
            hasJumpEndParam = HasAnimatorParameter(jumpEndHash, AnimatorControllerParameterType.Trigger);
            hasWallImpactParam = HasAnimatorParameter(wallImpactHash, AnimatorControllerParameterType.Trigger);
            hasWallJumpStartParam = HasAnimatorParameter(wallJumpStartHash, AnimatorControllerParameterType.Trigger);
        }

        private void Update()
        {
            float speedX = Mathf.Abs(rb.velocity.x);
            float vy = rb.velocity.y;
            bool isGrounded = controller != null && controller.IsGrounded;
            float visualSpeedX = GetVisualSpeedX(speedX, isGrounded);
            float maxRunSpeed = controller != null && controller.stats != null
                ? controller.stats.maxRunSpeed
                : 0f;
            float runAnimSpeed = maxRunSpeed > 0.01f
                ? Mathf.Clamp(visualSpeedX / maxRunSpeed, minRunAnimSpeed, maxRunAnimSpeed)
                : 1f;

            if (jumpEndVisualLockTimer > 0f)
                jumpEndVisualLockTimer = Mathf.Max(0f, jumpEndVisualLockTimer - Time.deltaTime);

            anim.SetFloat(speedHash, visualSpeedX > movingThreshold ? visualSpeedX : 0f);
            anim.SetFloat(runAnimSpeedHash, runAnimSpeed);
            anim.SetFloat(vyHash, vy);

            // ✅ Requiere que PlayerController exponga estas props
            bool isTouchingWall = !isGrounded && controller != null && controller.IsTouchingWall;
            bool isWallSliding = !isGrounded && controller != null && controller.IsWallSliding;
            bool isPushingIntoWall = !isGrounded && controller != null && controller.IsPushingIntoWall;
            anim.SetBool(groundedHash, isGrounded);
            anim.SetBool(hangingHash, controller != null && controller.IsHanging);
            if (hasGroundedSample && !wasGrounded && isGrounded && (controller == null || !controller.IsNormalJumpAnticipating))
                TriggerJumpEnd();
            wasGrounded = isGrounded;
            hasGroundedSample = true;

            if (hasWallSlidingParam)
                anim.SetBool(wallSlidingHash, isWallSliding);
            if (hasWallSlideEdgeParam)
            {
                bool isLosingContact = controller != null && controller.IsWallLosingContact;
                if (!isWallSliding)
                    wallSlideEdgeLatched = false;
                else if (isLosingContact)
                    wallSlideEdgeLatched = true;

                bool wallSlideEdge = isWallSliding && wallSlideEdgeLatched;
                anim.SetBool(wallSlideEdgeHash, wallSlideEdge);
            }
            if (hasWallSideParam)
                anim.SetInteger(wallSideHash, !isGrounded && controller != null ? controller.WallSide : 0);
            UpdateWallAnimations(isGrounded, isTouchingWall, isWallSliding, isPushingIntoWall);

            // Crouch/LayDown desde InputReader (si existe)
            var input = controller != null ? controller.input : null;
            if (input != null)
            {
                anim.SetBool(crouchHash, input.IsCrouching);
                anim.SetBool(layDownHash, input.IsLayDown);
            }

            anim.SetInteger(attackDirHash, cachedAttackDir);
        }

        private float GetVisualSpeedX(float speedX, bool isGrounded)
        {
            var input = controller != null ? controller.input : null;
            float inputX = input != null ? Mathf.Abs(input.MoveInput.x) : speedX;
            if (isGrounded && inputX <= movingThreshold)
                return 0f;

            return speedX;
        }

        private bool HasAnimatorParameter(int hash, AnimatorControllerParameterType type)
        {
            for (int i = 0; i < anim.parameterCount; i++)
            {
                AnimatorControllerParameter parameter = anim.parameters[i];
                if (parameter.nameHash == hash && parameter.type == type)
                    return true;
            }

            return false;
        }

        private void UpdateWallAnimations(bool isGrounded, bool isTouchingWall, bool isWallSliding, bool isPushingIntoWall)
        {
            if (jumpEndVisualLockTimer > 0f)
                return;

            if (isGrounded)
            {
                wasTouchingWall = false;
                wasWallSliding = false;
                return;
            }

            bool isInWallJumpStart = IsInState(wallJumpStartStateName);
            if (isInWallJumpStart)
            {
                if (GetCurrentStateNormalizedTime() >= 1f)
                    PlayManual(fallingStateName);

                wasTouchingWall = isTouchingWall;
                wasWallSliding = isWallSliding;
                return;
            }

            if (!wasTouchingWall && isTouchingWall && !isGrounded && isPushingIntoWall)
                TriggerWallImpact();

            if (!wasWallSliding && isWallSliding && !IsInState(wallImpactStateName))
                PlayManual(wallSlideStateName);
            else if (wasWallSliding && !isWallSliding && !isGrounded)
                PlayManual(fallingStateName);

            if (IsInState(wallImpactStateName) && GetCurrentStateNormalizedTime() >= 1f)
                PlayManual(isWallSliding ? wallSlideStateName : fallingStateName);

            wasTouchingWall = isTouchingWall;
            wasWallSliding = isWallSliding;
        }

        private bool IsInState(string stateName)
        {
            return anim.GetCurrentAnimatorStateInfo(0).IsName(stateName);
        }

        private float GetCurrentStateNormalizedTime()
        {
            return anim.GetCurrentAnimatorStateInfo(0).normalizedTime;
        }

        private void PlayManual(string stateName)
        {
            if (jumpEndVisualLockTimer > 0f && stateName != jumpEndStateName)
                return;

            int stateHash = Animator.StringToHash(stateName);
            if (!anim.HasState(0, stateHash))
            {
                Debug.LogWarning($"Animator state not found on layer 0: {stateName}", this);
                return;
            }

            anim.Play(stateHash, 0, 0f);
        }

        public void TriggerJumpStart()
        {
            if (!hasJumpStartParam) return;

            if (hasJumpEndParam)
                anim.ResetTrigger(jumpEndHash);
            anim.ResetTrigger(jumpStartHash);
            anim.SetTrigger(jumpStartHash);
            PlayManual(jumpStartStateName);
        }

        private void TriggerWallImpact()
        {
            if (hasWallImpactParam)
            {
                anim.ResetTrigger(wallImpactHash);
                anim.SetTrigger(wallImpactHash);
            }

            PlayManual(wallImpactStateName);
        }

        public void TriggerWallJumpStart()
        {
            if (hasWallJumpStartParam)
            {
                anim.ResetTrigger(wallJumpStartHash);
                anim.SetTrigger(wallJumpStartHash);
            }

            PlayManual(wallJumpStartStateName);
        }

        public void CancelJumpStart()
        {
            if (hasJumpStartParam)
                anim.ResetTrigger(jumpStartHash);
            if (hasJumpEndParam)
                anim.ResetTrigger(jumpEndHash);

            bool isGrounded = controller != null && controller.IsGrounded;
            float visualSpeedX = GetVisualSpeedX(Mathf.Abs(rb.velocity.x), isGrounded);
            string targetState = !isGrounded
                ? fallingStateName
                : visualSpeedX > movingThreshold ? runStateName : idleStateName;
            PlayManual(targetState);
        }

        private void TriggerJumpEnd()
        {
            if (!hasJumpEndParam) return;

            if (hasJumpStartParam)
                anim.ResetTrigger(jumpStartHash);
            anim.ResetTrigger(jumpEndHash);
            anim.SetTrigger(jumpEndHash);
            jumpEndVisualLockTimer = jumpEndVisualLockTime;
            PlayManual(jumpEndStateName);
        }

        public void TriggerAttack() => TriggerAttack(cachedAttackDir);

        public void TriggerAttack(int attackDir)
        {
            cachedAttackDir = Mathf.Clamp(attackDir, 0, 2);
            anim.SetInteger(attackDirHash, cachedAttackDir);
            anim.ResetTrigger(attackTrigHash);
            anim.SetTrigger(attackTrigHash);
        }

        public void TriggerParry()
        {
            anim.ResetTrigger(parryTrigHash);
            anim.SetTrigger(parryTrigHash);
        }
    }
}

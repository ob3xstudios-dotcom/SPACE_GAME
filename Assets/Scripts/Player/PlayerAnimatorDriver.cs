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
        [SerializeField] private string wallSideParam = "WallSide";

        [Header("Jump Params")]
        [SerializeField] private string jumpStartTrigger = "JumpStart";
        [SerializeField] private string jumpEndTrigger = "JumpEnd";
        [SerializeField] private string jumpStartStateName = "Monkey_JumpStart";
        [SerializeField] private string wallImpactTrigger = "WallImpact";
        [SerializeField] private string wallJumpStartTrigger = "WallJumpStart";
        [SerializeField] private string wallImpactStateName = "Monkey_WallImpact";
        [SerializeField] private string wallSlideStateName = "Monkey_WallSlide";
        [SerializeField] private string wallJumpStartStateName = "Monkey_WallJumpStart";
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

        private Animator anim;
        private PlayerController controller;
        private Rigidbody2D rb;

        private int speedHash, runAnimSpeedHash, groundedHash, vyHash, hangingHash;
        private int wallSlidingHash, wallSideHash;
        private int jumpStartHash, jumpEndHash, wallImpactHash, wallJumpStartHash;
        private int attackTrigHash, attackDirHash;
        private int parryTrigHash;
        private int crouchHash, layDownHash;
        private bool hasWallSlidingParam, hasWallSideParam, hasJumpStartParam, hasJumpEndParam;
        private bool hasWallImpactParam, hasWallJumpStartParam;
        private bool hasGroundedSample, wasGrounded, wasTouchingWall, wasWallSliding;

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
            float maxRunSpeed = controller != null && controller.stats != null
                ? controller.stats.maxRunSpeed
                : 0f;
            float runAnimSpeed = maxRunSpeed > 0.01f
                ? Mathf.Clamp(speedX / maxRunSpeed, minRunAnimSpeed, maxRunAnimSpeed)
                : 1f;

            anim.SetFloat(speedHash, speedX > movingThreshold ? speedX : 0f);
            anim.SetFloat(runAnimSpeedHash, runAnimSpeed);
            anim.SetFloat(vyHash, vy);

            // ✅ Requiere que PlayerController exponga estas props
            bool isGrounded = controller != null && controller.IsGrounded;
            bool isTouchingWall = controller != null && controller.IsTouchingWall;
            bool isWallSliding = controller != null && controller.IsWallSliding;
            bool isPushingIntoWall = controller != null && controller.IsPushingIntoWall;
            anim.SetBool(groundedHash, isGrounded);
            anim.SetBool(hangingHash, controller != null && controller.IsHanging);
            if (hasGroundedSample && !wasGrounded && isGrounded && (controller == null || !controller.IsNormalJumpAnticipating))
                TriggerJumpEnd();
            wasGrounded = isGrounded;
            hasGroundedSample = true;

            if (hasWallSlidingParam)
                anim.SetBool(wallSlidingHash, isWallSliding);
            if (hasWallSideParam)
                anim.SetInteger(wallSideHash, controller != null ? controller.WallSide : 0);
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
            bool isInWallJumpStart = IsInState(wallJumpStartStateName);
            if (isInWallJumpStart)
            {
                if (GetCurrentStateNormalizedTime() >= 1f)
                    anim.Play(fallingStateName, 0, 0f);

                wasTouchingWall = isTouchingWall;
                wasWallSliding = isWallSliding;
                return;
            }

            if (!wasTouchingWall && isTouchingWall && !isGrounded && isPushingIntoWall)
                TriggerWallImpact();

            if (!wasWallSliding && isWallSliding && !IsInState(wallImpactStateName))
                anim.Play(wallSlideStateName, 0, 0f);
            else if (wasWallSliding && !isWallSliding && !isGrounded)
                anim.Play(fallingStateName, 0, 0f);

            if (IsInState(wallImpactStateName) && GetCurrentStateNormalizedTime() >= 1f)
                anim.Play(isWallSliding ? wallSlideStateName : fallingStateName, 0, 0f);

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

        public void TriggerJumpStart()
        {
            if (!hasJumpStartParam) return;

            if (hasJumpEndParam)
                anim.ResetTrigger(jumpEndHash);
            anim.ResetTrigger(jumpStartHash);
            anim.SetTrigger(jumpStartHash);
            anim.Play(jumpStartStateName, 0, 0f);
        }

        private void TriggerWallImpact()
        {
            if (hasWallImpactParam)
            {
                anim.ResetTrigger(wallImpactHash);
                anim.SetTrigger(wallImpactHash);
            }

            anim.Play(wallImpactStateName, 0, 0f);
        }

        public void TriggerWallJumpStart()
        {
            if (hasWallJumpStartParam)
            {
                anim.ResetTrigger(wallJumpStartHash);
                anim.SetTrigger(wallJumpStartHash);
            }

            anim.Play(wallJumpStartStateName, 0, 0f);
        }

        public void CancelJumpStart()
        {
            if (hasJumpStartParam)
                anim.ResetTrigger(jumpStartHash);
            if (hasJumpEndParam)
                anim.ResetTrigger(jumpEndHash);

            bool isGrounded = controller != null && controller.IsGrounded;
            string targetState = !isGrounded
                ? fallingStateName
                : Mathf.Abs(rb.velocity.x) > movingThreshold ? runStateName : idleStateName;
            anim.Play(targetState, 0, 0f);
        }

        private void TriggerJumpEnd()
        {
            if (!hasJumpEndParam) return;

            anim.ResetTrigger(jumpEndHash);
            anim.SetTrigger(jumpEndHash);
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

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
        [SerializeField] private string groundedParam = "Grounded";
        [SerializeField] private string vyParam = "Vy";
        [SerializeField] private string hangingParam = "Hanging";

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

        private Animator anim;
        private PlayerController controller;
        private Rigidbody2D rb;

        private int speedHash, groundedHash, vyHash, hangingHash;
        private int attackTrigHash, attackDirHash;
        private int parryTrigHash;
        private int crouchHash, layDownHash;

        private int cachedAttackDir = 0;

        private void Awake()
        {
            anim = GetComponent<Animator>();
            controller = GetComponent<PlayerController>();
            rb = GetComponent<Rigidbody2D>();

            speedHash = Animator.StringToHash(speedParam);
            groundedHash = Animator.StringToHash(groundedParam);
            vyHash = Animator.StringToHash(vyParam);
            hangingHash = Animator.StringToHash(hangingParam);

            attackTrigHash = Animator.StringToHash(attackTrigger);
            attackDirHash = Animator.StringToHash(attackDirIntParam);

            parryTrigHash = Animator.StringToHash(parryTrigger);

            crouchHash = Animator.StringToHash(crouchBool);
            layDownHash = Animator.StringToHash(layDownBool);
        }

        private void Update()
        {
            float speedX = Mathf.Abs(rb.velocity.x);
            float vy = rb.velocity.y;

            anim.SetFloat(speedHash, speedX > movingThreshold ? speedX : 0f);
            anim.SetFloat(vyHash, vy);

            // ✅ Requiere que PlayerController exponga estas props
            anim.SetBool(groundedHash, controller != null && controller.IsGrounded);
            anim.SetBool(hangingHash, controller != null && controller.IsHanging);

            // Crouch/LayDown desde InputReader (si existe)
            var input = controller != null ? controller.input : null;
            if (input != null)
            {
                anim.SetBool(crouchHash, input.IsCrouching);
                anim.SetBool(layDownHash, input.IsLayDown);
            }

            anim.SetInteger(attackDirHash, cachedAttackDir);
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

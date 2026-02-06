using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Input
{
    public class InputReader : MonoBehaviour
    {
        public Vector2 MoveInput { get; private set; }

        // Latched (hasta consumir)
        public bool JumpPressed { get; private set; }
        public bool DashPressed { get; private set; }
        public bool AttackPressed { get; private set; }
        public bool ParryPressed { get; private set; }

        // Held (estado “real”)
        public bool JumpHeld { get; private set; }
        public bool CrouchHeld { get; private set; }

        // Toggle states (modo)
        public bool IsCrouching { get; private set; }   // toggle por pulsación
        public bool IsLayDown { get; private set; }     // se activa por mantener

        [Header("Crouch -> LayDown")]
        [Tooltip("Segundos manteniendo el botón Crouch para pasar a LayDown.")]
        [SerializeField, Range(0.2f, 5f)] private float holdToLayDownSeconds = 2f;

        [Header("Debug")]
        [SerializeField] private bool debugInput = false;

        private PlayerInputActions actions;

        private float crouchHoldTimer;
        private bool crouchWasHeld;

        private void Awake()
        {
            actions = new PlayerInputActions();
        }

        private void OnEnable()
        {
            if (actions == null) actions = new PlayerInputActions();
            actions.Enable();
            actions.Gameplay.Enable();

            // MOVE
            actions.Gameplay.Move.performed += OnMovePerformed;
            actions.Gameplay.Move.canceled += OnMoveCanceled;

            // JUMP
            actions.Gameplay.Jump.started += OnJumpStarted;
            actions.Gameplay.Jump.performed += OnJumpPerformed;
            actions.Gameplay.Jump.canceled += OnJumpCanceled;

            // DASH / ATTACK / PARRY
            actions.Gameplay.Dash.performed += OnDashPerformed;
            actions.Gameplay.Attack.performed += OnAttackPerformed;
            actions.Gameplay.Parry.performed += OnParryPerformed;

            // CROUCH (Press para toggle; pero también usamos IsPressed() en Update para el hold real)
            actions.Gameplay.Crouch.performed += OnCrouchPerformed;

            if (debugInput) Debug.Log("[INPUT] InputReader ENABLED");
        }

        private void OnDisable()
        {
            if (actions == null) return;

            actions.Gameplay.Move.performed -= OnMovePerformed;
            actions.Gameplay.Move.canceled -= OnMoveCanceled;

            actions.Gameplay.Jump.started -= OnJumpStarted;
            actions.Gameplay.Jump.performed -= OnJumpPerformed;
            actions.Gameplay.Jump.canceled -= OnJumpCanceled;

            actions.Gameplay.Dash.performed -= OnDashPerformed;
            actions.Gameplay.Attack.performed -= OnAttackPerformed;
            actions.Gameplay.Parry.performed -= OnParryPerformed;

            actions.Gameplay.Crouch.performed -= OnCrouchPerformed;

            actions.Gameplay.Disable();
            actions.Disable();

            if (debugInput) Debug.Log("[INPUT] InputReader DISABLED");
        }

        private void OnDestroy()
        {
            actions?.Dispose();
        }

        private void Update()
        {
            // ✅ Held real (aunque el action tenga Interaction Press)
            // OJO: actions.Gameplay es struct => NO se compara con null
            bool crouchDown = actions != null && actions.Gameplay.Crouch != null && actions.Gameplay.Crouch.IsPressed();
            CrouchHeld = crouchDown;

            // Detectar inicio de hold
            if (crouchDown && !crouchWasHeld)
                crouchHoldTimer = 0f;

            // Contar hold
            if (crouchDown)
            {
                crouchHoldTimer += Time.deltaTime;

                // ✅ Si ya estás crouch y mantienes X segundos -> LayDown
                if (IsCrouching && !IsLayDown && crouchHoldTimer >= holdToLayDownSeconds)
                {
                    IsLayDown = true;
                    if (debugInput) Debug.Log("[INPUT] LayDown ACTIVATED (hold)");
                }
            }

            crouchWasHeld = crouchDown;
        }

        // ----------------------------
        // CONSUME
        // ----------------------------
        public bool ConsumeJumpPressed()
        {
            if (!JumpPressed) return false;
            JumpPressed = false;
            return true;
        }

        public bool ConsumeDashPressed()
        {
            if (!DashPressed) return false;
            DashPressed = false;
            return true;
        }

        public bool ConsumeAttackPressed()
        {
            if (!AttackPressed) return false;
            AttackPressed = false;
            return true;
        }

        public bool ConsumeParryPressed()
        {
            if (!ParryPressed) return false;
            ParryPressed = false;
            return true;
        }

        // ----------------------------
        // CALLBACKS
        // ----------------------------
        private void OnMovePerformed(InputAction.CallbackContext ctx)
        {
            MoveInput = ctx.ReadValue<Vector2>();
            if (debugInput) Debug.Log($"[INPUT] MOVE {MoveInput}");
        }

        private void OnMoveCanceled(InputAction.CallbackContext ctx)
        {
            MoveInput = Vector2.zero;
            if (debugInput) Debug.Log("[INPUT] MOVE canceled");
        }

        private void OnJumpStarted(InputAction.CallbackContext ctx)
        {
            JumpHeld = true;
            if (debugInput) Debug.Log("[INPUT] JUMP started");
        }

        private void OnJumpCanceled(InputAction.CallbackContext ctx)
        {
            JumpHeld = false;
            if (debugInput) Debug.Log("[INPUT] JUMP canceled");
        }

        private void OnJumpPerformed(InputAction.CallbackContext ctx)
        {
            JumpPressed = true;
            if (debugInput) Debug.Log("[INPUT] JUMP performed");
        }

        private void OnDashPerformed(InputAction.CallbackContext ctx)
        {
            DashPressed = true;
            if (debugInput) Debug.Log("[INPUT] DASH performed");
        }

        private void OnAttackPerformed(InputAction.CallbackContext ctx)
        {
            AttackPressed = true;
            if (debugInput) Debug.Log("[INPUT] ATTACK performed");
        }

        private void OnParryPerformed(InputAction.CallbackContext ctx)
        {
            ParryPressed = true;
            if (debugInput) Debug.Log("[INPUT] PARRY performed");
        }

        private void OnCrouchPerformed(InputAction.CallbackContext ctx)
        {
            // Toggle:
            // - Si estás en crouch o laydown => vuelves a STAND
            // - Si estás de pie => entras en CROUCH
            if (IsCrouching || IsLayDown)
            {
                IsCrouching = false;
                IsLayDown = false;
                if (debugInput) Debug.Log("[INPUT] CROUCH OFF (stand)");
            }
            else
            {
                IsCrouching = true;
                IsLayDown = false;
                if (debugInput) Debug.Log("[INPUT] CROUCH ON");
            }
        }
    }
}

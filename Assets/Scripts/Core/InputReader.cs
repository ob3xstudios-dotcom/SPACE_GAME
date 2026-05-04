using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Input
{
    public class InputReader : MonoBehaviour
    {
        public Vector2 MoveInput { get; private set; }

        public bool JumpHeld { get; private set; }
        public bool IsCrouching { get; private set; }
        public bool IsLayDown { get; private set; }

        private bool jumpPressed;
        private bool attackPressed;
        private bool dashPressed;
        private bool parryPressed;

        [Header("Crouch / LayDown")]
        [SerializeField, Range(0.2f, 5f)] private float holdSecondsForLayDown = 2f;

        [Header("Debug")]
        [SerializeField] private bool debugInput = true;

        private PlayerInputActions actions;

        private bool crouchHeld;
        private float crouchHoldTimer;
        private bool layDownTriggeredThisHold;

        private void Awake()
        {
            actions = new PlayerInputActions();
        }

        private void OnEnable()
        {
            if (actions == null)
                actions = new PlayerInputActions();

            actions.Enable();
            actions.Gameplay.Enable();

            actions.Gameplay.Move.performed += OnMovePerformed;
            actions.Gameplay.Move.canceled += OnMoveCanceled;

            actions.Gameplay.Jump.started += OnJumpStarted;
            actions.Gameplay.Jump.canceled += OnJumpCanceled;

            actions.Gameplay.Attack.started += OnAttackStarted;
            actions.Gameplay.Dash.started += OnDashStarted;

            actions.Gameplay.Parry.started += OnParryStarted;
            actions.Gameplay.Parry.performed += OnParryPerformed;

            actions.Gameplay.Crouch.started += OnCrouchStarted;
            actions.Gameplay.Crouch.canceled += OnCrouchCanceled;

            if (debugInput)
            {
                Debug.Log("[INPUT] ENABLED");
                Debug.Log($"[INPUT] Parry enabled={actions.Gameplay.Parry.enabled} bindings={actions.Gameplay.Parry.bindings.Count}");
            }
        }

        private void OnDisable()
        {
            if (actions == null) return;

            actions.Gameplay.Move.performed -= OnMovePerformed;
            actions.Gameplay.Move.canceled -= OnMoveCanceled;

            actions.Gameplay.Jump.started -= OnJumpStarted;
            actions.Gameplay.Jump.canceled -= OnJumpCanceled;

            actions.Gameplay.Attack.started -= OnAttackStarted;
            actions.Gameplay.Dash.started -= OnDashStarted;

            actions.Gameplay.Parry.started -= OnParryStarted;
            actions.Gameplay.Parry.performed -= OnParryPerformed;

            actions.Gameplay.Crouch.started -= OnCrouchStarted;
            actions.Gameplay.Crouch.canceled -= OnCrouchCanceled;

            actions.Gameplay.Disable();
            actions.Disable();

            if (debugInput) Debug.Log("[INPUT] DISABLED");
        }

        private void OnDestroy()
        {
            actions?.Dispose();
        }

        private void Update()
        {
            // FALLBACK DIRECTO: si los callbacks no llegan, esto debería detectarlo.
            if (actions != null && actions.Gameplay.Parry.WasPressedThisFrame())
            {
                parryPressed = true;
                Debug.Log("[INPUT] PARRY WasPressedThisFrame");
            }

            if (!crouchHeld)
            {
                crouchHoldTimer = 0f;
                layDownTriggeredThisHold = false;
                return;
            }

            crouchHoldTimer += Time.deltaTime;

            if (!layDownTriggeredThisHold && crouchHoldTimer >= holdSecondsForLayDown)
            {
                IsLayDown = true;
                IsCrouching = false;
                layDownTriggeredThisHold = true;

                if (debugInput) Debug.Log("[INPUT] LAYDOWN ON");
            }
        }

        public bool ConsumeJumpPressed()
        {
            if (!jumpPressed) return false;
            jumpPressed = false;
            return true;
        }

        public bool ConsumeAttackPressed()
        {
            if (!attackPressed) return false;
            attackPressed = false;
            return true;
        }

        public bool ConsumeDashPressed()
        {
            if (!dashPressed) return false;
            dashPressed = false;
            return true;
        }

        public bool ConsumeParryPressed()
        {
            if (!parryPressed) return false;
            parryPressed = false;
            return true;
        }

        public void ForceStandUp()
        {
            IsCrouching = false;
            IsLayDown = false;
            crouchHeld = false;
            crouchHoldTimer = 0f;
            layDownTriggeredThisHold = false;

            if (debugInput) Debug.Log("[INPUT] FORCE STAND UP");
        }

        private void OnMovePerformed(InputAction.CallbackContext ctx)
        {
            MoveInput = ctx.ReadValue<Vector2>();
        }

        private void OnMoveCanceled(InputAction.CallbackContext ctx)
        {
            MoveInput = Vector2.zero;
        }

        private void OnJumpStarted(InputAction.CallbackContext ctx)
        {
            jumpPressed = true;
            JumpHeld = true;

            if (debugInput) Debug.Log("[INPUT] JUMP started");
        }

        private void OnJumpCanceled(InputAction.CallbackContext ctx)
        {
            JumpHeld = false;
        }

        private void OnAttackStarted(InputAction.CallbackContext ctx)
        {
            attackPressed = true;

            if (debugInput) Debug.Log("[INPUT] ATTACK started");
        }

        private void OnDashStarted(InputAction.CallbackContext ctx)
        {
            dashPressed = true;

            if (debugInput) Debug.Log("[INPUT] DASH started");
        }

        private void OnParryStarted(InputAction.CallbackContext ctx)
        {
            parryPressed = true;
            Debug.Log("[INPUT] PARRY started");
        }

        private void OnParryPerformed(InputAction.CallbackContext ctx)
        {
            parryPressed = true;
            Debug.Log("[INPUT] PARRY performed");
        }

        private void OnCrouchStarted(InputAction.CallbackContext ctx)
        {
            crouchHeld = true;
            crouchHoldTimer = 0f;
            layDownTriggeredThisHold = false;

            if (IsLayDown)
            {
                IsLayDown = false;
                IsCrouching = true;
            }
            else if (IsCrouching)
            {
                IsCrouching = false;
            }
            else
            {
                IsCrouching = true;
            }

            if (debugInput)
                Debug.Log($"[INPUT] CROUCH toggle | crouch={IsCrouching} layDown={IsLayDown}");
        }

        private void OnCrouchCanceled(InputAction.CallbackContext ctx)
        {
            crouchHeld = false;
            crouchHoldTimer = 0f;
            layDownTriggeredThisHold = false;
        }
    }
}
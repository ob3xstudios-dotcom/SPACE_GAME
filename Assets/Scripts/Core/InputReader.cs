using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Input
{
    public class InputReader : MonoBehaviour
    {
        public enum CrouchPressType
        {
            None,
            Preview,
            Short,
            Long
        }

        public enum PlayerPosture
        {
            Standing,
            Crouching,
            LayDown
        }

        public struct CrouchCommand
        {
            public CrouchPressType PressType { get; }
            public PlayerPosture OriginalPosture { get; }
            public PlayerPosture TargetPosture { get; }
            public bool IsValid => PressType != CrouchPressType.None;

            public CrouchCommand(CrouchPressType pressType, PlayerPosture originalPosture)
            {
                PressType = pressType;
                OriginalPosture = originalPosture;
                TargetPosture = InputReader.ResolveTargetPosture(pressType, originalPosture);
            }
        }

        public Vector2 MoveInput { get; private set; }

        public bool JumpHeld { get; private set; }
        public bool SprintHeld { get; private set; }
        public PlayerPosture CurrentPosture => posture;
        public bool IsCrouching => posture == PlayerPosture.Crouching;
        public bool IsLayDown => posture == PlayerPosture.LayDown;

        private bool jumpPressed;
        private bool attackPressed;
        private bool dashPressed;
        private bool parryPressed;
        private bool interactPressed;
        private int swingGrabPressedFrame = -1;
        private int swingGrabConsumedFrame = -1;

        [Header("Crouch / LayDown")]
        [SerializeField, Range(0.2f, 5f)] private float holdSecondsForLayDown = 0.4f;

        [Header("Debug")]
        [SerializeField] private bool debugInput = false;

        private PlayerInputActions actions;

        private PlayerPosture posture;
        private PlayerPosture crouchGestureOriginalPosture;
        private bool crouchHeld;
        private float crouchHoldTimer;
        private bool longCrouchTriggeredThisHold;
        private CrouchCommand pendingCrouchCommand;
        private int pendingCrouchPressFrame = -1;
        private string activeCrouchControlPath = "<none>";

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
            actions.Gameplay.Sprint.started += OnSprintStarted;
            actions.Gameplay.Sprint.canceled += OnSprintCanceled;

            actions.Gameplay.Attack.started += OnAttackStarted;
            actions.Gameplay.Dash.started += OnDashStarted;
            actions.Gameplay.Interact.started += OnInteractStarted;

            actions.Gameplay.Parry.started += OnParryStarted;
            actions.Gameplay.Parry.performed += OnParryPerformed;
            actions.Gameplay.SwingGrab.started += OnSwingGrabStarted;

            actions.Gameplay.Crouch.started += OnCrouchStarted;
            actions.Gameplay.Crouch.performed += OnCrouchPerformed;
            actions.Gameplay.Crouch.canceled += OnCrouchCanceled;

            if (debugInput)
            {
                Debug.Log("[INPUT] ENABLED");
                Debug.Log($"[INPUT] Parry enabled={actions.Gameplay.Parry.enabled} bindings={actions.Gameplay.Parry.bindings.Count}");
            }
        }

        private void OnDisable()
        {
            ResetCrouchPressTracking();
            SprintHeld = false;

            if (actions == null) return;

            actions.Gameplay.Move.performed -= OnMovePerformed;
            actions.Gameplay.Move.canceled -= OnMoveCanceled;

            actions.Gameplay.Jump.started -= OnJumpStarted;
            actions.Gameplay.Jump.canceled -= OnJumpCanceled;
            actions.Gameplay.Sprint.started -= OnSprintStarted;
            actions.Gameplay.Sprint.canceled -= OnSprintCanceled;

            actions.Gameplay.Attack.started -= OnAttackStarted;
            actions.Gameplay.Dash.started -= OnDashStarted;
            actions.Gameplay.Interact.started -= OnInteractStarted;

            actions.Gameplay.Parry.started -= OnParryStarted;
            actions.Gameplay.Parry.performed -= OnParryPerformed;
            actions.Gameplay.SwingGrab.started -= OnSwingGrabStarted;

            actions.Gameplay.Crouch.started -= OnCrouchStarted;
            actions.Gameplay.Crouch.performed -= OnCrouchPerformed;
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
            if (swingGrabPressedFrame >= 0 && swingGrabPressedFrame != Time.frameCount)
                swingGrabPressedFrame = -1;

            ExpirePendingCrouchPress();

            // FALLBACK DIRECTO: si los callbacks no llegan, esto debería detectarlo.
            if (actions != null && actions.Gameplay.Parry.WasPressedThisFrame())
            {
                parryPressed = true;
                if (debugInput) Debug.Log("[INPUT] PARRY WasPressedThisFrame");
            }

            if (actions != null && actions.Gameplay.Interact.WasPressedThisFrame())
            {
                interactPressed = true;
                if (debugInput) Debug.Log("[INPUT] INTERACT WasPressedThisFrame");
            }

            if (actions != null && actions.Gameplay.SwingGrab.WasPressedThisFrame())
            {
                RegisterSwingGrabPressed();
                if (debugInput) Debug.Log("[INPUT] SWING GRAB WasPressedThisFrame");
            }

            if (!crouchHeld || longCrouchTriggeredThisHold) return;

            crouchHoldTimer += Time.deltaTime;

            if (crouchHoldTimer >= holdSecondsForLayDown)
            {
                longCrouchTriggeredThisHold = true;
                LogCrouchDiagnostic(
                    "long.begin",
                    details: $"threshold={holdSecondsForLayDown:0.###}");
                RegisterCrouchPress(CrouchPressType.Long);

                if (debugInput) Debug.Log("[INPUT] CROUCH long");
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

        public bool ConsumeInteractPressed()
        {
            if (!interactPressed) return false;
            interactPressed = false;
            return true;
        }

        public bool ConsumeSwingGrabPressed()
        {
            int currentFrame = Time.frameCount;
            if (swingGrabPressedFrame != currentFrame || swingGrabConsumedFrame == currentFrame)
            {
                swingGrabPressedFrame = -1;
                return false;
            }

            swingGrabPressedFrame = -1;
            swingGrabConsumedFrame = currentFrame;
            return true;
        }

        private void RegisterSwingGrabPressed()
        {
            int currentFrame = Time.frameCount;
            if (swingGrabConsumedFrame == currentFrame) return;

            swingGrabPressedFrame = currentFrame;
        }

        public CrouchCommand ConsumeCrouchPress()
        {
            ExpirePendingCrouchPress();

            CrouchCommand result = pendingCrouchCommand;
            if (result.IsValid)
            {
                LogCrouchDiagnostic(
                    "order.consume",
                    details: $"order={result.PressType} original={result.OriginalPosture} " +
                        $"target={result.TargetPosture}");
            }

            ClearPendingCrouchPress();
            return result;
        }

        public void CommitPostureTransition(CrouchCommand command, string source)
        {
            if (!command.IsValid) return;

            CommitPosture(
                command.TargetPosture,
                source,
                $"order={command.PressType} original={command.OriginalPosture}");
        }

        public void CommitPosture(
            PlayerPosture nextPosture,
            string source,
            string commandDetails = null)
        {
            PlayerPosture previousPosture = posture;
            posture = nextPosture;
            string details = string.IsNullOrEmpty(commandDetails)
                ? string.Empty
                : $" {commandDetails}";

            LogCrouchDiagnostic(
                "posture.commit",
                details: $"source={source} previous={previousPosture} next={posture}{details}");

            if (debugInput && posture != previousPosture)
                Debug.Log($"[INPUT] POSTURE {previousPosture} -> {posture} ({source})");
        }

        public void CancelCrouchGesture(string source = "Unspecified")
        {
            LogCrouchDiagnostic(
                "gesture.cancel",
                details: $"source={source}");

            ResetCrouchPressTracking();

            if (debugInput) Debug.Log($"[INPUT] CROUCH GESTURE CANCELED ({source})");
        }

        private static PlayerPosture ResolveTargetPosture(
            CrouchPressType pressType,
            PlayerPosture originalPosture)
        {
            if (pressType == CrouchPressType.None)
                return originalPosture;

            if (pressType == CrouchPressType.Preview)
                return originalPosture == PlayerPosture.LayDown
                    ? PlayerPosture.Crouching
                    : originalPosture;

            switch (originalPosture)
            {
                case PlayerPosture.Standing:
                    return pressType == CrouchPressType.Short
                        ? PlayerPosture.Crouching
                        : PlayerPosture.LayDown;

                case PlayerPosture.Crouching:
                    return pressType == CrouchPressType.Short
                        ? PlayerPosture.Standing
                        : PlayerPosture.LayDown;

                case PlayerPosture.LayDown:
                    return pressType == CrouchPressType.Short
                        ? PlayerPosture.Standing
                        : PlayerPosture.Crouching;

                default:
                    return originalPosture;
            }
        }

        private void RegisterCrouchPress(CrouchPressType pressType)
        {
            ExpirePendingCrouchPress();
            CrouchCommand command = new CrouchCommand(pressType, crouchGestureOriginalPosture);
            if (pendingCrouchCommand.IsValid)
            {
                bool replacesPreview =
                    pendingCrouchCommand.PressType == CrouchPressType.Preview &&
                    pressType != CrouchPressType.Preview;
                bool longReplacesShort =
                    pendingCrouchCommand.PressType == CrouchPressType.Short &&
                    pressType == CrouchPressType.Long;
                if (replacesPreview || longReplacesShort)
                {
                    LogCrouchDiagnostic(
                        "order.replace",
                        details: $"previous={pendingCrouchCommand.PressType} " +
                            $"next={pressType} original={command.OriginalPosture} " +
                            $"target={command.TargetPosture}");
                    pendingCrouchCommand = command;
                    pendingCrouchPressFrame = Time.frameCount;
                    return;
                }

                LogCrouchDiagnostic(
                    "order.register_ignored",
                    details: $"requested={pressType} pending={pendingCrouchCommand.PressType}");
                return;
            }

            pendingCrouchCommand = command;
            pendingCrouchPressFrame = Time.frameCount;
            LogCrouchDiagnostic(
                "order.register",
                details: $"order={pressType} original={command.OriginalPosture} " +
                    $"target={command.TargetPosture}");
        }

        private void ExpirePendingCrouchPress()
        {
            if (!pendingCrouchCommand.IsValid) return;
            if (Time.frameCount - pendingCrouchPressFrame <= 1) return;

            LogCrouchDiagnostic(
                "order.expire",
                details: $"order={pendingCrouchCommand.PressType} " +
                    $"original={pendingCrouchCommand.OriginalPosture} " +
                    $"registeredFrame={pendingCrouchPressFrame}");
            ClearPendingCrouchPress();
        }

        private void ClearPendingCrouchPress()
        {
            pendingCrouchCommand = default;
            pendingCrouchPressFrame = -1;
        }

        private void ResetCrouchPressTracking()
        {
            crouchHeld = false;
            crouchHoldTimer = 0f;
            longCrouchTriggeredThisHold = false;
            ClearPendingCrouchPress();
            activeCrouchControlPath = "<none>";
        }

        private void LogCrouchDiagnostic(
            string eventName,
            string controlPath = null,
            float? controlValue = null,
            string details = null)
        {
            if (!debugInput) return;

            string resolvedControl = controlPath ?? GetCurrentCrouchControlPath();
            float resolvedValue = controlValue ?? GetCurrentCrouchValue();
            string suffix = string.IsNullOrEmpty(details) ? string.Empty : $" {details}";

            Debug.Log(
                $"[INPUT][CROUCH] frame={Time.frameCount} event={eventName} " +
                $"control={resolvedControl} value={resolvedValue:0.###} posture={posture} " +
                $"held={crouchHeld} long={longCrouchTriggeredThisHold} " +
                $"pending={pendingCrouchCommand.PressType}{suffix}");
        }

        private string GetCurrentCrouchControlPath()
        {
            if (actions == null) return activeCrouchControlPath;

            var control = actions.Gameplay.Crouch.activeControl;
            return control != null ? control.path : activeCrouchControlPath;
        }

        private float GetCurrentCrouchValue()
        {
            if (actions == null || !actions.Gameplay.Crouch.enabled) return 0f;
            return actions.Gameplay.Crouch.ReadValue<float>();
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

        private void OnSprintStarted(InputAction.CallbackContext ctx)
        {
            SprintHeld = true;

            if (debugInput) Debug.Log("[INPUT] SPRINT started");
        }

        private void OnSprintCanceled(InputAction.CallbackContext ctx)
        {
            SprintHeld = false;

            if (debugInput) Debug.Log("[INPUT] SPRINT canceled");
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

        private void OnInteractStarted(InputAction.CallbackContext ctx)
        {
            interactPressed = true;

            if (debugInput) Debug.Log("[INPUT] INTERACT started");
        }

        private void OnParryStarted(InputAction.CallbackContext ctx)
        {
            parryPressed = true;
            if (debugInput) Debug.Log("[INPUT] PARRY started");
        }

        private void OnParryPerformed(InputAction.CallbackContext ctx)
        {
            parryPressed = true;
            if (debugInput) Debug.Log("[INPUT] PARRY performed");
        }

        private void OnSwingGrabStarted(InputAction.CallbackContext ctx)
        {
            RegisterSwingGrabPressed();
            if (debugInput) Debug.Log("[INPUT] SWING GRAB started");
        }

        private void OnCrouchStarted(InputAction.CallbackContext ctx)
        {
            string controlPath = ctx.control != null ? ctx.control.path : "<unknown>";
            float controlValue = ctx.ReadValue<float>();
            if (crouchHeld)
            {
                LogCrouchDiagnostic(
                    "started.ignored",
                    controlPath,
                    controlValue,
                    $"phase={ctx.phase} reason=gesture_already_active");
                return;
            }

            LogCrouchDiagnostic(
                "started",
                controlPath,
                controlValue,
                $"phase={ctx.phase} result=await_performed");

            if (debugInput)
                Debug.Log("[INPUT] CROUCH started");
        }

        private void OnCrouchPerformed(InputAction.CallbackContext ctx)
        {
            string controlPath = ctx.control != null ? ctx.control.path : "<unknown>";
            float controlValue = ctx.ReadValue<float>();
            if (crouchHeld)
            {
                LogCrouchDiagnostic(
                    "performed.ignored",
                    controlPath,
                    controlValue,
                    $"phase={ctx.phase} reason=gesture_already_active");
                return;
            }

            activeCrouchControlPath = controlPath;
            crouchGestureOriginalPosture = posture;
            crouchHeld = true;
            crouchHoldTimer = 0f;
            longCrouchTriggeredThisHold = false;
            bool previewCrouchingFromLayDown =
                crouchGestureOriginalPosture == PlayerPosture.LayDown;
            bool deferShortUntilCanceled =
                crouchGestureOriginalPosture != PlayerPosture.Standing;
            string resolution = previewCrouchingFromLayDown
                ? "preview_crouching"
                : deferShortUntilCanceled ? "deferred_short" : "immediate_short";

            LogCrouchDiagnostic(
                "performed",
                activeCrouchControlPath,
                controlValue,
                $"phase={ctx.phase} original={crouchGestureOriginalPosture} " +
                $"resolution={resolution}");
            if (previewCrouchingFromLayDown)
                RegisterCrouchPress(CrouchPressType.Preview);
            else if (!deferShortUntilCanceled)
                RegisterCrouchPress(CrouchPressType.Short);

            if (debugInput)
                Debug.Log("[INPUT] CROUCH performed");
        }

        private void OnCrouchCanceled(InputAction.CallbackContext ctx)
        {
            string controlPath = ctx.control != null ? ctx.control.path : activeCrouchControlPath;
            float controlValue = ctx.ReadValue<float>();
            LogCrouchDiagnostic(
                "canceled",
                controlPath,
                controlValue,
                $"phase={ctx.phase}");

            if (!crouchHeld)
            {
                LogCrouchDiagnostic(
                    "canceled.ignored",
                    controlPath,
                    controlValue,
                    "reason=gesture_not_active");
                activeCrouchControlPath = "<none>";
                return;
            }

            bool registerDeferredShort =
                !longCrouchTriggeredThisHold &&
                crouchGestureOriginalPosture != PlayerPosture.Standing;

            if (longCrouchTriggeredThisHold)
            {
                LogCrouchDiagnostic(
                    "long.end",
                    controlPath,
                    controlValue,
                    "result=keep_long_posture");
            }
            else
            {
                LogCrouchDiagnostic(
                    "short.end",
                    controlPath,
                    controlValue,
                    registerDeferredShort
                        ? "result=register_deferred_short"
                        : "result=keep_short_posture");
            }

            crouchHeld = false;
            crouchHoldTimer = 0f;
            longCrouchTriggeredThisHold = false;

            if (registerDeferredShort)
                RegisterCrouchPress(CrouchPressType.Short);

            LogCrouchDiagnostic(
                "canceled.complete",
                controlPath,
                controlValue,
                $"registeredOrder={(registerDeferredShort ? CrouchPressType.Short : CrouchPressType.None)}");
            activeCrouchControlPath = "<none>";
        }
    }
}

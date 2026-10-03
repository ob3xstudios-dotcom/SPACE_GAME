using Game.Input;
using Game.Currency;
using Game.Player;
using Game.Progress;
using Game.Progression;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Interaction
{
    /// <summary>
    /// Reusable rest seat that locks a Player at a configurable point and restores
    /// canonical health, dash stamina and sprint reserve once on entry.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class RestBench : InteractableBase
    {
        public enum SeatFacing
        {
            Left = -1,
            Right = 1
        }

        [Header("References")]
        [SerializeField] private Collider2D interactionTrigger;
        [SerializeField] private Transform seatPoint;

        [Header("Persistent Progress")]
        [Tooltip("Stable save identifier configured per scene instance, for example muelle_bench_01.")]
        [SerializeField] private string benchId;

        [Header("Seating")]
        [SerializeField] private SeatFacing seatFacing = SeatFacing.Right;
        [SerializeField, Min(0f)] private float exitInputLockSeconds = 0.18f;
        [SerializeField, Range(0.1f, 1f)] private float horizontalExitThreshold = 0.35f;

        private PlayerController seatedController;
        private InputReader seatedInput;
        private PlayerStamina seatedStamina;
        private PlayerHealth seatedHealth;
        private Rigidbody2D seatedBody;

        private RigidbodyType2D cachedBodyType;
        private RigidbodyConstraints2D cachedConstraints;
        private float cachedGravityScale;
        private float exitInputLockTimer;
        private bool horizontalExitArmed;
        private bool occupied;

        public bool IsOccupied => occupied;
        public string BenchId => benchId;

        public bool IsOccupiedBy(PlayerController controller)
        {
            return occupied && controller != null && seatedController == controller;
        }

        private void Awake()
        {
            ResolveReferences();
            EnsureTrigger();
        }

        private void Update()
        {
            if (!occupied || seatedInput == null)
                return;

            bool interactPressed = seatedInput.ConsumeInteractPressed();
            bool jumpPressed = seatedInput.ConsumeJumpPressed();
            bool skillsPressed = seatedInput.ConsumeSkillsPressed();
            DiscardIncompatibleInputs();

            if (skillsPressed)
                AbilityLoadoutUI.ToggleForBench(this, seatedController);

            if (exitInputLockTimer > 0f)
            {
                exitInputLockTimer = Mathf.Max(0f, exitInputLockTimer - Time.deltaTime);
                return;
            }

            float horizontalInput = Mathf.Abs(seatedInput.MoveInput.x);
            if (!horizontalExitArmed)
            {
                if (horizontalInput < horizontalExitThreshold)
                    horizontalExitArmed = true;
            }

            bool moveRequested =
                horizontalExitArmed &&
                horizontalInput >= horizontalExitThreshold;

            if (interactPressed || jumpPressed || moveRequested)
                ExitSeat();
        }

        private void FixedUpdate()
        {
            if (!occupied || seatedBody == null)
                return;

            SnapPlayerToSeatPoint();
            seatedBody.velocity = Vector2.zero;
            seatedBody.angularVelocity = 0f;
        }

        private void OnDisable()
        {
            if (occupied)
                ExitSeat();
        }

        public override bool CanInteract(GameObject interactor)
        {
            if (!base.CanInteract(interactor) || occupied || seatPoint == null || interactor == null)
                return false;

            PlayerController controller = interactor.GetComponentInParent<PlayerController>();
            return CanSeat(controller);
        }

        public override void Interact(GameObject interactor)
        {
            if (interactor == null)
                return;

            PlayerController controller = interactor.GetComponentInParent<PlayerController>();
            TryEnterSeat(controller);
        }

        public bool TryEnterSeat(PlayerController controller)
        {
            if (!CanSeat(controller))
                return false;

            InputReader input = controller.GetComponent<InputReader>();
            PlayerStamina stamina = controller.GetComponent<PlayerStamina>();
            PlayerHealth health = controller.GetComponent<PlayerHealth>();
            Rigidbody2D body = controller.GetComponent<Rigidbody2D>();

            if (input == null || stamina == null || health == null || body == null)
                return false;

            seatedController = controller;
            seatedInput = input;
            seatedStamina = stamina;
            seatedHealth = health;
            seatedBody = body;

            cachedBodyType = body.bodyType;
            cachedConstraints = body.constraints;
            cachedGravityScale = body.gravityScale;
            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.bodyType = RigidbodyType2D.Kinematic;
            body.constraints = RigidbodyConstraints2D.FreezePosition | RigidbodyConstraints2D.FreezeRotation;
            body.gravityScale = 0f;

            controller.FaceDirection((int)seatFacing);
            SnapPlayerToSeatPoint();

            occupied = true;
            exitInputLockTimer = exitInputLockSeconds;
            horizontalExitArmed = Mathf.Abs(input.MoveInput.x) < horizontalExitThreshold;

            SubscribeToHealth();
            controller.SetExternalControlLocked(true);
            DiscardIncompatibleInputs();

            // Restore resources and commit progress exactly once on successful entry.
            seatedHealth.Heal(seatedHealth.MaxHealth);
            seatedStamina.Refill();
            seatedController.RefillSprintReserve();
            CurrencyService.Instance.DepositAllCarriedCoins();
            GameProgressService.Instance.RestAtBench(benchId, SceneManager.GetActiveScene().name);
            return true;
        }

        public void ExitSeat()
        {
            if (!occupied)
                return;

            AbilityLoadoutUI.NotifyBenchExited(this);
            UnsubscribeFromHealth();
            DiscardIncompatibleInputs();

            if (seatedBody != null)
            {
                seatedBody.bodyType = cachedBodyType;
                seatedBody.constraints = cachedConstraints;
                seatedBody.gravityScale = cachedGravityScale;
                seatedBody.velocity = Vector2.zero;
                seatedBody.angularVelocity = 0f;
            }

            if (seatedController != null)
                seatedController.SetExternalControlLocked(false);

            seatedController = null;
            seatedInput = null;
            seatedStamina = null;
            seatedHealth = null;
            seatedBody = null;
            exitInputLockTimer = 0f;
            horizontalExitArmed = false;
            occupied = false;
        }

        protected override void Reset()
        {
            base.Reset();
            ResolveReferences();
            EnsureTrigger();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            benchId = GameProgressData.NormalizeId(benchId);
            ResolveReferences();
            EnsureTrigger();
        }
#endif

        private bool CanSeat(PlayerController controller)
        {
            return
                !occupied &&
                seatPoint != null &&
                controller != null &&
                controller.enabled &&
                controller.IsGrounded &&
                !controller.IsDashing &&
                !controller.IsAttacking &&
                !controller.IsHanging &&
                !controller.IsSwinging &&
                !controller.IsSliding &&
                !controller.IsDiving &&
                !controller.IsPostDiveSliding &&
                !controller.IsCrouching &&
                !controller.IsLayDown &&
                !controller.IsCarrying;
        }

        private void SnapPlayerToSeatPoint()
        {
            if (seatPoint == null || seatedController == null || seatedBody == null)
                return;

            Vector3 targetPosition = seatPoint.position;
            targetPosition.z = seatedController.transform.position.z;
            seatedController.transform.position = targetPosition;
            seatedBody.position = targetPosition;
        }

        private void DiscardIncompatibleInputs()
        {
            if (seatedInput == null)
                return;

            seatedInput.ConsumeAttackPressed();
            seatedInput.ConsumeDashPressed();
            seatedInput.ConsumeParryPressed();
            seatedInput.ConsumeSkillsPressed();
            seatedInput.CancelCrouchGesture("RestBench.Seated");
            seatedInput.ClearSwingGrabBuffer();
        }

        private void SubscribeToHealth()
        {
            if (seatedHealth == null)
                return;

            seatedHealth.OnDamaged += HandlePlayerDamaged;
            seatedHealth.OnDied += HandlePlayerStateInterrupted;
            seatedHealth.OnRespawned += HandlePlayerStateInterrupted;
        }

        private void UnsubscribeFromHealth()
        {
            if (seatedHealth == null)
                return;

            seatedHealth.OnDamaged -= HandlePlayerDamaged;
            seatedHealth.OnDied -= HandlePlayerStateInterrupted;
            seatedHealth.OnRespawned -= HandlePlayerStateInterrupted;
        }

        private void HandlePlayerDamaged(int damage)
        {
            ExitSeat();
        }

        private void HandlePlayerStateInterrupted()
        {
            ExitSeat();
        }

        private void ResolveReferences()
        {
            if (interactionTrigger == null)
                interactionTrigger = GetComponent<Collider2D>();

            if (seatPoint == null)
            {
                Transform candidate = transform.Find("SeatPoint");
                if (candidate != null)
                    seatPoint = candidate;
            }
        }

        private void EnsureTrigger()
        {
            if (interactionTrigger != null)
                interactionTrigger.isTrigger = true;
        }
    }
}

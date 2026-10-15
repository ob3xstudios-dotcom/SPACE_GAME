using Game.Audio;
using Game.World;
using UnityEngine;

namespace Game.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerParkourFeedback : MonoBehaviour
    {
        private const string FootstepWalkEventId = "player.movement.footstep.walk";
        private const string FootstepRunEventId = "player.movement.footstep.run";
        private const string JumpEventId = "player.movement.jump";
        private const string LandEventId = "player.movement.land";
        private const string HardLandEventId = "player.movement.land.hard";
        private const string DashEventId = "player.movement.dash";
        private const string SprintStartEventId = "player.movement.sprint.start";
        private const string SlideStartEventId = "player.movement.slide.start";
        private const string SlideLoopEventId = "player.movement.slide.loop";
        private const string SlideEndEventId = "player.movement.slide.end";
        private const string DolphinDiveEventId = "player.movement.dolphin_dive";
        private const string DolphinDiveLoopEventId = "player.movement.dolphin_dive.loop";
        private const string WallSlideStartEventId = "player.movement.wall_slide.start";
        private const string WallSlideLoopEventId = "player.movement.wall_slide.loop";
        private const string WallSlideEndEventId = "player.movement.wall_slide.end";
        private const string WallJumpEventId = "player.movement.wall_jump";
        private const string RopeGrabEventId = "player.movement.rope.grab";
        private const string RopeReleaseEventId = "player.movement.rope.release";
        private const string SwingStartEventId = "player.movement.swing.start";
        private const string SwingLoopEventId = "player.movement.swing.loop";
        private const string SwingEndEventId = "player.movement.swing.end";
        private const string OverheadRailGrabEventId = "player.movement.overhead_rail.grab";
        private const string OverheadRailLoopEventId = "player.movement.overhead_rail.loop";
        private const string OverheadRailReleaseEventId = "player.movement.overhead_rail.release";
        private const string CapeOpenEventId = "player.movement.cape.open";
        private const string CapeLoopEventId = "player.movement.cape.loop";
        private const string CapeCloseEventId = "player.movement.cape.close";
        private const string NormalImpactEventId = "player.combat.impact.normal";
        private const string StrongImpactEventId = "player.combat.impact.strong";
        private const string ArmoredImpactEventId = "player.combat.impact.armored";
        private const string ParrySuccessEventId = "player.combat.parry.success";
        private const string DamageReceivedEventId = "player.combat.damage.received";

        [Header("References")]
        [SerializeField] private PlayerController controller;
        [SerializeField] private Rigidbody2D rb;
        [SerializeField] private CapsuleCollider2D capsule;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private SpriteRenderer[] visualRenderers;

        [Header("Particles")]
        [SerializeField] private ParticleSystem wallSlideDust;
        [SerializeField] private ParticleSystem wallJumpBurst;
        [SerializeField] private ParticleSystem landBurst;
        [SerializeField] private bool createDefaultParticles = true;

        [Header("Landing")]
        [SerializeField, Min(0f)] private float minLandingSpeed = 6f;
        [SerializeField, Min(0f)] private float hardLandingSpeed = 14f;

        [Header("Footsteps")]
        [SerializeField, Min(0f)] private float minFootstepSpeed = 0.75f;
        [SerializeField, Min(0f)] private float runFootstepSpeed = 6f;
        [SerializeField, Min(0.1f)] private float walkStepDistance = 1.35f;
        [SerializeField, Min(0.1f)] private float runStepDistance = 1.6f;
        [SerializeField, Min(0f)] private float minFootstepInterval = 0.12f;

        [Header("Continuous Movement Audio")]
        [SerializeField, Min(0f)] private float minSwingLoopSpeed = 1.25f;
        [SerializeField, Min(0f)] private float minOverheadRailLoopSpeed = 0.35f;
        [SerializeField, Min(0f)] private float minCapeLoopSpeed = 1.25f;

        [Header("Wall Slide Visual")]
        [SerializeField] private bool enableWallSlideLean = true;
        [SerializeField, Range(0f, 12f)] private float wallSlideLeanDegrees = 5f;
        [SerializeField, Range(1f, 30f)] private float wallSlideLeanLerp = 14f;
        [SerializeField] private bool enableWallSlideTint = true;
        [SerializeField] private Color wallSlideTint = new Color(0.78f, 0.92f, 1f, 1f);
        [SerializeField, Range(1f, 30f)] private float wallSlideTintLerp = 18f;

        private bool wasGrounded;
        private bool wasWallSliding;
        private bool wasDashing;
        private bool wasSprinting;
        private bool wasSwingAttached;
        private bool wasOverheadRailHanging;
        private bool wasCapeDeployed;
        private PlayerController.SprintSlideDiveState previousSlideDiveState;
        private SwingAttachmentMode previousSwingMode;
        private bool movementAudioInitialized;
        private bool movementAudioSuspended;
        private bool movementAudioBlocked;
        private float lastAirDownSpeed;
        private float footstepDistance;
        private float lastFootstepTime = float.NegativeInfinity;
        private Vector2 previousFootstepPosition;
        private Vector2 previousGroundPosition;
        private Collider2D sampledGroundCollider;
        private FootstepAudioSurface2D sampledFootstepSurface;
        private Quaternion visualBaseRotation;
        private Color[] visualBaseColors;
        private PlayerParry playerParry;
        private PlayerHealth playerHealth;
        private PlayerUpdraftCape updraftCape;
        private AudioPlaybackHandle wallSlideLoopHandle;
        private AudioPlaybackHandle slideLoopHandle;
        private AudioPlaybackHandle dolphinDiveLoopHandle;
        private AudioPlaybackHandle swingLoopHandle;
        private AudioPlaybackHandle overheadRailLoopHandle;
        private AudioPlaybackHandle capeLoopHandle;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<PlayerController>();
            if (rb == null) rb = GetComponent<Rigidbody2D>();
            if (capsule == null) capsule = GetComponent<CapsuleCollider2D>();
            playerParry = GetComponent<PlayerParry>();
            playerHealth = GetComponent<PlayerHealth>();
            updraftCape = GetComponent<PlayerUpdraftCape>();
            Transform leanRoot = visualRoot;
            if (leanRoot == null)
            {
                SpriteRenderer sprite = GetComponentInChildren<SpriteRenderer>();
                if (sprite != null && sprite.transform != transform)
                    leanRoot = sprite.transform;
            }

            if (leanRoot == transform)
                leanRoot = null;

            visualRoot = leanRoot;
            if (visualRoot == null)
                enableWallSlideLean = false;

            if (visualRenderers == null || visualRenderers.Length == 0)
                visualRenderers = GetComponentsInChildren<SpriteRenderer>();

            visualBaseColors = new Color[visualRenderers.Length];
            for (int i = 0; i < visualRenderers.Length; i++)
                visualBaseColors[i] = visualRenderers[i] != null ? visualRenderers[i].color : Color.white;

            if (visualRoot != null)
            {
                try
                {
                    visualBaseRotation = visualRoot.localRotation;
                }
                catch (UnassignedReferenceException)
                {
                    visualRoot = null;
                    enableWallSlideLean = false;
                }
            }
            else
            {
                enableWallSlideLean = false;
            }

            if (createDefaultParticles)
            {
                if (wallSlideDust == null) wallSlideDust = CreateParticleSystem("WallSlideDust", true);
                if (wallJumpBurst == null) wallJumpBurst = CreateParticleSystem("WallJumpBurst", false);
                if (landBurst == null) landBurst = CreateParticleSystem("LandBurst", false);
            }
        }

        private void OnEnable()
        {
            if (controller != null)
            {
                controller.NormalJumpPerformed += OnNormalJumpPerformed;
                controller.WallJumped += OnWallJumped;
                controller.AttackImpactResolved += OnAttackImpactResolved;
            }

            if (playerParry != null)
                playerParry.ParrySucceeded += OnParrySucceeded;

            if (playerHealth != null)
            {
                playerHealth.OnDamaged += OnPlayerDamaged;
                playerHealth.OnDied += OnPlayerDied;
                playerHealth.OnRespawned += OnPlayerRespawned;
            }

            movementAudioBlocked = false;
            movementAudioSuspended = false;
            movementAudioInitialized = false;
            ResetFootstepTracking();
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.NormalJumpPerformed -= OnNormalJumpPerformed;
                controller.WallJumped -= OnWallJumped;
                controller.AttackImpactResolved -= OnAttackImpactResolved;
            }

            if (playerParry != null)
                playerParry.ParrySucceeded -= OnParrySucceeded;

            if (playerHealth != null)
            {
                playerHealth.OnDamaged -= OnPlayerDamaged;
                playerHealth.OnDied -= OnPlayerDied;
                playerHealth.OnRespawned -= OnPlayerRespawned;
            }

            StopMovementLoops();
            ResetFootstepTracking();

            RestoreVisuals();
        }

        private void Update()
        {
            if (controller == null || rb == null) return;

            if (movementAudioBlocked)
            {
                StopMovementLoops();
                ResetFootstepTracking();
                return;
            }

            if (Time.timeScale <= 0f)
            {
                if (!movementAudioSuspended)
                {
                    movementAudioSuspended = true;
                    StopMovementLoops();
                    ResetFootstepTracking();
                }

                return;
            }

            ResolveUpdraftCape();

            bool grounded = controller.IsGrounded;
            bool wallSliding = controller.IsWallSliding;
            bool dashing = controller.IsDashing;
            bool sprinting = controller.IsSprinting;
            bool swingAttached = controller.IsSwingAttached;
            bool overheadRailHanging = controller.IsOverheadRailHanging;
            SwingAttachmentMode swingMode = controller.ActiveSwingMode;
            PlayerController.SprintSlideDiveState slideDiveState = controller.SlideDiveState;
            bool capeDeployed = updraftCape != null && updraftCape.IsDeployed;
            bool swingMoving = swingAttached && rb.velocity.sqrMagnitude >= minSwingLoopSpeed * minSwingLoopSpeed;
            bool overheadRailMoving =
                overheadRailHanging &&
                rb.velocity.sqrMagnitude >= minOverheadRailLoopSpeed * minOverheadRailLoopSpeed;
            bool capeMoving = IsCapeMoving(capeDeployed);

            if (!movementAudioInitialized)
            {
                movementAudioInitialized = true;
                wasGrounded = grounded;
                wasWallSliding = wallSliding;
                wasDashing = dashing;
                wasSprinting = sprinting;
                wasSwingAttached = swingAttached;
                wasOverheadRailHanging = overheadRailHanging;
                wasCapeDeployed = capeDeployed;
                previousSlideDiveState = slideDiveState;
                previousSwingMode = swingMode;
                lastAirDownSpeed = grounded ? 0f : Mathf.Max(0f, -rb.velocity.y);
                ResetFootstepTracking();

                if (wallSliding)
                    OnWallSlideStarted();

                SynchronizeMovementLoops(
                    wallSliding,
                    slideDiveState,
                    swingMoving,
                    overheadRailMoving,
                    capeMoving);

                UpdateWallSlideVisuals(wallSliding);
                return;
            }

            if (movementAudioSuspended)
            {
                movementAudioSuspended = false;
                wasGrounded = grounded;
                wasWallSliding = wallSliding;
                wasDashing = dashing;
                wasSprinting = sprinting;
                wasSwingAttached = swingAttached;
                wasOverheadRailHanging = overheadRailHanging;
                wasCapeDeployed = capeDeployed;
                previousSlideDiveState = slideDiveState;
                previousSwingMode = swingMode;
                lastAirDownSpeed = grounded ? 0f : Mathf.Max(0f, -rb.velocity.y);
                ResetFootstepTracking();

                SynchronizeMovementLoops(
                    wallSliding,
                    slideDiveState,
                    swingMoving,
                    overheadRailMoving,
                    capeMoving);

                UpdateWallSlideVisuals(wallSliding);
                return;
            }

            if (!grounded)
                lastAirDownSpeed = Mathf.Max(lastAirDownSpeed, -rb.velocity.y);

            if (wallSliding && !wasWallSliding)
                OnWallSlideStarted();
            else if (!wallSliding && wasWallSliding)
                OnWallSlideStopped();

            bool justLanded = !wasGrounded && grounded;
            if (justLanded)
                OnLanded(lastAirDownSpeed);

            if (dashing && !wasDashing)
                AudioService.PlayAt(DashEventId, transform.position);

            if (sprinting && !wasSprinting)
                AudioService.PlayAt(SprintStartEventId, transform.position);

            UpdateSlideDiveAudio(previousSlideDiveState, slideDiveState);
            UpdateSwingAudio(wasSwingAttached, swingAttached, previousSwingMode, swingMode);
            UpdateOverheadRailAudio(wasOverheadRailHanging, overheadRailHanging);
            UpdateCapeAudio(wasCapeDeployed, capeDeployed);

            SynchronizeMovementLoops(
                wallSliding,
                slideDiveState,
                swingMoving,
                overheadRailMoving,
                capeMoving);

            UpdateFootsteps(grounded, justLanded, wallSliding, dashing);
            UpdateWallSlideVisuals(wallSliding);

            if (grounded)
                lastAirDownSpeed = 0f;

            wasGrounded = grounded;
            wasWallSliding = wallSliding;
            wasDashing = dashing;
            wasSprinting = sprinting;
            wasSwingAttached = swingAttached;
            wasOverheadRailHanging = overheadRailHanging;
            wasCapeDeployed = capeDeployed;
            previousSlideDiveState = slideDiveState;
            previousSwingMode = swingMode;
        }

        private void UpdateSlideDiveAudio(
            PlayerController.SprintSlideDiveState previousState,
            PlayerController.SprintSlideDiveState currentState)
        {
            if (previousState == currentState) return;

            bool previousWasGroundSlide = IsGroundSlideState(previousState);
            bool currentIsGroundSlide = IsGroundSlideState(currentState);

            if (previousState == PlayerController.SprintSlideDiveState.Dive &&
                currentState != PlayerController.SprintSlideDiveState.Dive)
            {
                StopDolphinDiveLoop();
            }

            if (previousWasGroundSlide && !currentIsGroundSlide)
            {
                StopSlideLoop();
                if (currentState != PlayerController.SprintSlideDiveState.Dive)
                    AudioService.PlayAt(SlideEndEventId, transform.position);
            }

            if (currentState == PlayerController.SprintSlideDiveState.Slide && !previousWasGroundSlide)
                AudioService.PlayAt(SlideStartEventId, transform.position);

            if (currentState == PlayerController.SprintSlideDiveState.Dive &&
                previousState != PlayerController.SprintSlideDiveState.Dive)
            {
                AudioService.PlayAt(DolphinDiveEventId, transform.position);
            }
        }

        private void UpdateSwingAudio(
            bool previouslyAttached,
            bool currentlyAttached,
            SwingAttachmentMode previousMode,
            SwingAttachmentMode currentMode)
        {
            if (currentlyAttached && !previouslyAttached)
            {
                if (currentMode == SwingAttachmentMode.Rope)
                    AudioService.PlayAt(RopeGrabEventId, transform.position);

                AudioService.PlayAt(SwingStartEventId, transform.position);
                return;
            }

            if (!currentlyAttached && previouslyAttached)
            {
                StopSwingLoop();
                AudioService.PlayAt(SwingEndEventId, transform.position);
                if (previousMode == SwingAttachmentMode.Rope)
                    AudioService.PlayAt(RopeReleaseEventId, transform.position);
                return;
            }

            if (!currentlyAttached || previousMode == currentMode) return;

            if (previousMode == SwingAttachmentMode.Rope)
                AudioService.PlayAt(RopeReleaseEventId, transform.position);
            if (currentMode == SwingAttachmentMode.Rope)
                AudioService.PlayAt(RopeGrabEventId, transform.position);
        }

        private void UpdateCapeAudio(bool previouslyDeployed, bool currentlyDeployed)
        {
            if (currentlyDeployed && !previouslyDeployed)
            {
                AudioService.PlayAt(CapeOpenEventId, transform.position);
                return;
            }

            if (currentlyDeployed || !previouslyDeployed) return;

            StopCapeLoop();
            AudioService.PlayAt(CapeCloseEventId, transform.position);
        }

        private void UpdateOverheadRailAudio(bool previouslyHanging, bool currentlyHanging)
        {
            if (currentlyHanging && !previouslyHanging)
            {
                AudioService.PlayAt(OverheadRailGrabEventId, transform.position);
                return;
            }

            if (currentlyHanging || !previouslyHanging) return;

            StopOverheadRailLoop();
            AudioService.PlayAt(OverheadRailReleaseEventId, transform.position);
        }

        private void OnWallSlideStarted()
        {
            AudioService.PlayAt(WallSlideStartEventId, transform.position);
            wallSlideLoopHandle = StartAttachedLoop(WallSlideLoopEventId);
            if (wallSlideDust != null && !wallSlideDust.isPlaying)
                wallSlideDust.Play();
        }

        private void OnWallSlideStopped()
        {
            StopWallSlideLoop();
            AudioService.PlayAt(WallSlideEndEventId, transform.position);
            if (wallSlideDust != null)
                wallSlideDust.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        private void OnWallJumped(int jumpedWallSide)
        {
            PositionAtWallSide(wallJumpBurst, jumpedWallSide, 0.05f);
            if (wallJumpBurst != null)
                wallJumpBurst.Play();

            AudioService.PlayAt(WallJumpEventId, transform.position);
        }

        private void OnLanded(float downSpeed)
        {
            string eventId = downSpeed < minLandingSpeed
                ? null
                : downSpeed >= hardLandingSpeed
                    ? HardLandEventId
                    : LandEventId;

            if (string.IsNullOrEmpty(eventId)) return;

            PositionAtFeet(landBurst);
            if (landBurst != null)
                landBurst.Play();

            AudioService.PlayAt(eventId, transform.position);
        }

        private void OnNormalJumpPerformed()
        {
            AudioService.PlayAt(JumpEventId, transform.position);
        }

        private void OnAttackImpactResolved(
            Game.Combat.PlayerAttackResolution resolution,
            Game.Combat.PlayerAttackDirection direction,
            Vector2 hitPosition)
        {
            string eventId;
            switch (resolution)
            {
                case Game.Combat.PlayerAttackResolution.Armored:
                    eventId = ArmoredImpactEventId;
                    break;
                case Game.Combat.PlayerAttackResolution.BellyUp:
                    eventId = StrongImpactEventId;
                    break;
                default:
                    eventId = direction == Game.Combat.PlayerAttackDirection.Down
                        ? StrongImpactEventId
                        : NormalImpactEventId;
                    break;
            }

            AudioService.PlayAt(eventId, hitPosition);
        }

        private void OnParrySucceeded(Game.Enemies.EnemyBase enemy)
        {
            Vector3 position = enemy != null ? enemy.transform.position : transform.position;
            AudioService.PlayAt(ParrySuccessEventId, position);
        }

        private void OnPlayerDamaged(int damage)
        {
            if (damage > 0)
                AudioService.PlayAt(DamageReceivedEventId, transform.position);
        }

        private void OnPlayerDied()
        {
            movementAudioBlocked = true;
            StopMovementLoops();
            ResetFootstepTracking();
        }

        private void OnPlayerRespawned()
        {
            movementAudioBlocked = false;
            movementAudioSuspended = false;
            movementAudioInitialized = false;
            lastAirDownSpeed = 0f;
            StopMovementLoops();
            ResetFootstepTracking();
        }

        private void ResolveUpdraftCape()
        {
            if (updraftCape == null)
                updraftCape = GetComponent<PlayerUpdraftCape>();
        }

        private bool IsCapeMoving(bool capeDeployed)
        {
            if (!capeDeployed || !controller.CanUseUpdraftCapeMotion)
                return false;

            return (updraftCape != null && updraftCape.IsLiftActive) ||
                   rb.velocity.sqrMagnitude >= minCapeLoopSpeed * minCapeLoopSpeed;
        }

        private static bool IsGroundSlideState(PlayerController.SprintSlideDiveState state)
        {
            return state == PlayerController.SprintSlideDiveState.Slide ||
                   state == PlayerController.SprintSlideDiveState.ProneSlide;
        }

        private void SynchronizeMovementLoops(
            bool wallSliding,
            PlayerController.SprintSlideDiveState slideDiveState,
            bool swingMoving,
            bool overheadRailMoving,
            bool capeMoving)
        {
            SetAttachedLoop(ref wallSlideLoopHandle, WallSlideLoopEventId, wallSliding, 0.12f);
            SetAttachedLoop(ref slideLoopHandle, SlideLoopEventId, IsGroundSlideState(slideDiveState), 0.12f);
            SetAttachedLoop(
                ref dolphinDiveLoopHandle,
                DolphinDiveLoopEventId,
                slideDiveState == PlayerController.SprintSlideDiveState.Dive,
                0.1f);
            SetAttachedLoop(ref swingLoopHandle, SwingLoopEventId, swingMoving, 0.12f);
            SetAttachedLoop(
                ref overheadRailLoopHandle,
                OverheadRailLoopEventId,
                overheadRailMoving,
                0.1f);
            SetAttachedLoop(ref capeLoopHandle, CapeLoopEventId, capeMoving, 0.15f);
        }

        private void SetAttachedLoop(
            ref AudioPlaybackHandle handle,
            string eventId,
            bool shouldPlay,
            float fadeOutSeconds)
        {
            if (shouldPlay)
            {
                if (!handle.IsValid)
                    handle = StartAttachedLoop(eventId);
                return;
            }

            AudioService.Stop(handle, fadeOutSeconds);
            handle = default;
        }

        private AudioPlaybackHandle StartAttachedLoop(string eventId)
        {
            AudioService service = AudioService.Instance;
            AudioEventCatalog catalog = service != null ? service.Catalog : null;
            if (catalog == null || !catalog.TryGet(eventId, out AudioEventDefinition definition))
                return default;

            return AudioService.StartLoop(definition, transform);
        }

        private void StopWallSlideLoop()
        {
            AudioService.Stop(wallSlideLoopHandle, 0.12f);
            wallSlideLoopHandle = default;
        }

        private void StopSlideLoop()
        {
            AudioService.Stop(slideLoopHandle, 0.12f);
            slideLoopHandle = default;
        }

        private void StopDolphinDiveLoop()
        {
            AudioService.Stop(dolphinDiveLoopHandle, 0.1f);
            dolphinDiveLoopHandle = default;
        }

        private void StopSwingLoop()
        {
            AudioService.Stop(swingLoopHandle, 0.12f);
            swingLoopHandle = default;
        }

        private void StopOverheadRailLoop()
        {
            AudioService.Stop(overheadRailLoopHandle, 0.1f);
            overheadRailLoopHandle = default;
        }

        private void StopCapeLoop()
        {
            AudioService.Stop(capeLoopHandle, 0.15f);
            capeLoopHandle = default;
        }

        private void StopMovementLoops()
        {
            StopWallSlideLoop();
            StopSlideLoop();
            StopDolphinDiveLoop();
            StopSwingLoop();
            StopOverheadRailLoop();
            StopCapeLoop();
        }

        private void UpdateFootsteps(
            bool grounded,
            bool justLanded,
            bool wallSliding,
            bool dashing)
        {
            Collider2D groundCollider = controller.CurrentGroundCollider;
            Vector2 currentPosition = transform.position;
            Vector2 currentGroundPosition = groundCollider != null
                ? (Vector2)groundCollider.transform.position
                : Vector2.zero;

            bool locomotionBlocked =
                !grounded ||
                justLanded ||
                wallSliding ||
                dashing ||
                !controller.isActiveAndEnabled ||
                controller.IsExternalControlLocked ||
                controller.IsHanging ||
                controller.IsSwinging ||
                controller.IsSliding ||
                controller.IsDiving ||
                controller.IsPostDiveSliding;

            if (locomotionBlocked || groundCollider != sampledGroundCollider)
            {
                SetFootstepSample(groundCollider, currentPosition, currentGroundPosition);
                footstepDistance = 0f;
                return;
            }

            Vector2 playerDelta = currentPosition - previousFootstepPosition;
            Vector2 groundDelta = groundCollider != null
                ? currentGroundPosition - previousGroundPosition
                : Vector2.zero;
            Vector2 relativeDelta = playerDelta - groundDelta;

            previousFootstepPosition = currentPosition;
            previousGroundPosition = currentGroundPosition;

            float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
            float relativeSpeed = relativeDelta.magnitude / deltaTime;
            if (relativeSpeed < minFootstepSpeed)
            {
                footstepDistance = 0f;
                return;
            }

            bool running = Mathf.Abs(rb.velocity.x) >= runFootstepSpeed;
            float stepDistance = running ? runStepDistance : walkStepDistance;
            footstepDistance += relativeDelta.magnitude;

            if (footstepDistance < stepDistance ||
                Time.time - lastFootstepTime < minFootstepInterval)
            {
                return;
            }

            footstepDistance = Mathf.Repeat(footstepDistance, stepDistance);
            lastFootstepTime = Time.time;

            AudioEventDefinition surfaceEvent = sampledFootstepSurface != null
                ? sampledFootstepSurface.GetEvent(running)
                : null;
            if (surfaceEvent != null)
            {
                AudioService.PlayAt(surfaceEvent, transform.position);
                return;
            }

            AudioService.PlayAt(
                running ? FootstepRunEventId : FootstepWalkEventId,
                transform.position);
        }

        private void ResetFootstepTracking()
        {
            Collider2D groundCollider = controller != null
                ? controller.CurrentGroundCollider
                : null;
            Vector2 currentPosition = transform.position;
            Vector2 currentGroundPosition = groundCollider != null
                ? (Vector2)groundCollider.transform.position
                : Vector2.zero;

            SetFootstepSample(groundCollider, currentPosition, currentGroundPosition);
            footstepDistance = 0f;
        }

        private void SetFootstepSample(
            Collider2D groundCollider,
            Vector2 playerPosition,
            Vector2 groundPosition)
        {
            if (sampledGroundCollider != groundCollider)
            {
                sampledGroundCollider = groundCollider;
                sampledFootstepSurface = groundCollider != null
                    ? groundCollider.GetComponentInParent<FootstepAudioSurface2D>()
                    : null;
            }

            previousFootstepPosition = playerPosition;
            previousGroundPosition = groundPosition;
        }

        private void UpdateWallSlideVisuals(bool wallSliding)
        {
            UpdateWallSlideDust(wallSliding);
            UpdateWallSlideLean(wallSliding);
            UpdateWallSlideTint(wallSliding);
        }

        private void UpdateWallSlideDust(bool wallSliding)
        {
            if (!wallSliding || wallSlideDust == null) return;

            PositionAtWallSide(wallSlideDust, controller.WallSide, -0.25f);
            if (!wallSlideDust.isPlaying)
                wallSlideDust.Play();
        }

        private void UpdateWallSlideLean(bool wallSliding)
        {
            if (!enableWallSlideLean || visualRoot == null) return;

            float targetZ = wallSliding ? -controller.WallSide * wallSlideLeanDegrees : 0f;
            Quaternion target = visualBaseRotation * Quaternion.Euler(0f, 0f, targetZ);
            visualRoot.localRotation = Quaternion.Slerp(
                visualRoot.localRotation,
                target,
                1f - Mathf.Exp(-wallSlideLeanLerp * Time.deltaTime)
            );
        }

        private void UpdateWallSlideTint(bool wallSliding)
        {
            if (!enableWallSlideTint || visualRenderers == null || visualBaseColors == null) return;

            float t = 1f - Mathf.Exp(-wallSlideTintLerp * Time.deltaTime);
            for (int i = 0; i < visualRenderers.Length; i++)
            {
                SpriteRenderer renderer = visualRenderers[i];
                if (renderer == null) continue;

                Color target = wallSliding ? MultiplyColor(visualBaseColors[i], wallSlideTint) : visualBaseColors[i];
                target.a = visualBaseColors[i].a;
                renderer.color = Color.Lerp(renderer.color, target, t);
            }
        }

        private static Color MultiplyColor(Color a, Color b)
        {
            return new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);
        }

        private void PositionAtWallSide(ParticleSystem system, int side, float yOffset)
        {
            if (system == null || capsule == null) return;
            if (side == 0) side = controller != null && controller.FacingLeft ? -1 : 1;

            Bounds b = capsule.bounds;
            system.transform.position = new Vector3(
                b.center.x + b.extents.x * side,
                b.center.y + yOffset,
                transform.position.z
            );
            system.transform.rotation = Quaternion.Euler(0f, side > 0 ? 180f : 0f, 0f);
        }

        private void PositionAtFeet(ParticleSystem system)
        {
            if (system == null || capsule == null) return;

            Bounds b = capsule.bounds;
            system.transform.position = new Vector3(b.center.x, b.min.y + 0.05f, transform.position.z);
            system.transform.rotation = Quaternion.identity;
        }

        private void RestoreVisuals()
        {
            if (visualRoot != null)
                visualRoot.localRotation = visualBaseRotation;

            if (visualRenderers == null || visualBaseColors == null) return;
            for (int i = 0; i < visualRenderers.Length; i++)
            {
                if (visualRenderers[i] != null)
                    visualRenderers[i].color = visualBaseColors[i];
            }
        }

        private ParticleSystem CreateParticleSystem(string objectName, bool looping)
        {
            GameObject go = new GameObject(objectName);
            go.transform.SetParent(transform, false);

            ParticleSystem system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.loop = looping;
            main.startLifetime = looping ? 0.22f : 0.28f;
            main.startSpeed = looping ? 1.2f : 2.4f;
            main.startSize = looping ? 0.055f : 0.11f;
            main.maxParticles = looping ? 40 : 20;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = looping ? 18f : 0f;
            emission.SetBursts(looping
                ? new ParticleSystem.Burst[0]
                : new[] { new ParticleSystem.Burst(0f, (short)12) });

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = looping ? 18f : 35f;
            shape.radius = looping ? 0.04f : 0.1f;

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.82f, 0.78f, 0.68f), 0f),
                    new GradientColorKey(new Color(0.55f, 0.52f, 0.45f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.75f, 0f),
                    new GradientAlphaKey(0f, 1f)
                });
            color.color = gradient;

            return system;
        }
    }
}

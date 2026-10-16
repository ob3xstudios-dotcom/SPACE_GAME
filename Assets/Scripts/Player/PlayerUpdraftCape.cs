using System.Collections.Generic;
using Game.Input;
using Game.World.Traversal;
using UnityEngine;

namespace Game.Player
{
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PlayerUpdraftCape : MonoBehaviour
    {
        private const float StrengthComparisonEpsilon = 0.0001f;
        private const float EndContainmentSafetyMargin = 0.02f;
        [Header("Provisional Visual")]
        [SerializeField] private bool createPlaceholderVisual = true;
        [SerializeField] private Color stowedColor = new Color(0.15f, 0.75f, 0.95f, 0.9f);
        [SerializeField] private Color deployedColor = new Color(0.35f, 0.95f, 1f, 0.95f);

        [Header("Aerodynamic Resistance")]
        [Tooltip("Vertical air resistance while rising, in inverse seconds. Higher values reduce jump height more when the cape is opened early.")]
        [SerializeField, Range(0f, 5f)] private float ascentAirResistance = 1f;
        [Tooltip("Vertical air resistance while falling, in inverse seconds. Higher values produce a lower effective terminal fall speed.")]
        [SerializeField, Range(0f, 12f)] private float descentAirResistance = 6f;

        [Header("Diagnostics")]
#if UNITY_EDITOR
        [SerializeField] private bool diagnosticLogs = true;
#else
        [SerializeField] private bool diagnosticLogs;
#endif

        private readonly Dictionary<UpdraftZone, HashSet<Collider2D>> overlappingZones =
            new Dictionary<UpdraftZone, HashSet<Collider2D>>();
        private readonly List<UpdraftZone> staleZones = new List<UpdraftZone>();

        private PlayerController controller;
        private InputReader input;
        private PlayerHealth health;
        private Rigidbody2D body;
        private Collider2D mainCollider;
        private GameObject capeVisual;
        private SpriteRenderer capeRenderer;
        private Sprite placeholderSprite;
        private bool deployed;
        private bool playerAlive = true;
        private bool subscribed;
        private bool liftActive;
        private bool hovering;
        private UpdraftZone activeZone;
        private UpdraftZone liftZone;
        private UpdraftZone lastUpPassZone;
        private float lastUpPassVelocityBeforeEnd;
        private bool hasUpPassVelocitySample;
        private string lastMotionBlockReason;
        private Vector2 postFlowTraceDirection;
        private string postFlowTraceReason;
        private int postFlowTraceFixedStep;
        private int postFlowTraceFixedStepsRemaining;

        private bool DiagnosticsEnabled
        {
            get
            {
#if UNITY_EDITOR
                return diagnosticLogs;
#else
                return false;
#endif
            }
        }

        public bool IsDeployed => deployed;
        public bool IsLiftActive => liftActive;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            controller = GetComponent<PlayerController>();
            input = GetComponent<InputReader>();
            health = GetComponent<PlayerHealth>();
            ResolveMainCollider();
            CreatePlaceholderVisual();
        }

        private void OnEnable()
        {
            Subscribe();
            UpdateVisual();
        }

        private void OnDisable()
        {
            Unsubscribe();
            StopLift("component-disabled");
            SetActiveZone(null);
            overlappingZones.Clear();
            SetDeployed(false);
        }

        private void OnDestroy()
        {
            if (placeholderSprite != null)
                Destroy(placeholderSprite);
        }

        internal void Bind(PlayerController playerController)
        {
            Unsubscribe();
            SetHorizontalAirCurrentInfluence(null, false);
            controller = playerController;
            body = GetComponent<Rigidbody2D>();
            input = GetComponent<InputReader>();
            health = GetComponent<PlayerHealth>();
            ResolveMainCollider();
            playerAlive = true;
            CreatePlaceholderVisual();
            Subscribe();
            UpdateVisual();
            LogBindState();
        }

        private void Update()
        {
            SetDeployed(playerAlive && input != null && input.CapeHeld);
            UpdateVisual();
        }

        private void FixedUpdate()
        {
            LogPostFlowTrace();

            string basicBlockReason = GetBasicMotionBlockReason();
            if (basicBlockReason != null)
            {
                SetMotionBlockReason(basicBlockReason);
                StopLift(basicBlockReason, basicBlockReason == "released");
                return;
            }

            if (!TryGetEffectiveZone(body.velocity, out UpdraftZone zone))
            {
                SetMotionBlockReason("no-zone");
                StopLift("no-zone", true);
                SetActiveZone(null);
                ApplyAerodynamicResistance();
                return;
            }

            SetActiveZone(zone);
            if (!controller.CanUseUpdraftCapeMotion)
            {
                string playerBlockReason = GetPlayerMotionBlockReason();
                SetMotionBlockReason(playerBlockReason);
                StopLift(playerBlockReason);
                return;
            }

            SetMotionBlockReason(null);
            StartLift(zone);
            SetHorizontalAirCurrentInfluence(zone, false);
            ApplyFlowMotion(zone);
        }

        private void ApplyFlowMotion(UpdraftZone zone)
        {
            Vector2 direction = zone.FlowDirection;
            float currentFlowSpeed = Vector2.Dot(body.velocity, direction);
            float fixedDeltaTime = Time.fixedDeltaTime;
            if (zone.EndBehavior == AirCurrentEndBehavior.PassThrough)
            {
                hovering = false;
                ApplyFlowAcceleration(zone, direction, currentFlowSpeed, fixedDeltaTime);
                return;
            }

            ApplyHoldInsideFlow(
                zone,
                direction,
                currentFlowSpeed,
                fixedDeltaTime);
        }

        private void ApplyHoldInsideFlow(
            UpdraftZone zone,
            Vector2 direction,
            float currentFlowSpeed,
            float fixedDeltaTime)
        {
            if (mainCollider == null || !mainCollider.enabled ||
                !zone.TryGetEndHoldCoordinates(
                    out float holdCoordinate,
                    out float boundaryCoordinate))
            {
                hovering = false;
                ApplyFlowAcceleration(zone, direction, currentFlowSpeed, fixedDeltaTime);
                return;
            }

            // Control the Player collider's leading edge instead of the transform pivot.
            // This leaves the configured inset physically inside the trigger volume.
            float playerLeadingEdge = UpdraftZone.GetLeadingEdgeCoordinate(
                mainCollider.bounds,
                direction);
            float positionError = holdCoordinate - playerLeadingEdge;
            float tolerance = zone.HoverTolerance;
            if (positionError > tolerance)
            {
                hovering = false;
                float safeFlowSpeed = Mathf.Sqrt(
                    2f * zone.HoverStrength * (positionError - tolerance));
                if (safeFlowSpeed >= zone.MaxFlowSpeed)
                {
                    ApplyFlowAcceleration(zone, direction, currentFlowSpeed, fixedDeltaTime);
                    return;
                }

                float targetFlowSpeed = Mathf.Min(zone.MaxFlowSpeed, safeFlowSpeed);
                SetEndControlledVelocity(
                    zone,
                    direction,
                    currentFlowSpeed,
                    targetFlowSpeed,
                    boundaryCoordinate - playerLeadingEdge,
                    fixedDeltaTime);
                return;
            }

            StartHover();
            float holdTargetSpeed = 0f;
            if (positionError < -tolerance)
            {
                float correctionDistance = -positionError - tolerance;
                holdTargetSpeed = -Mathf.Min(
                    zone.HoverCorrectionSpeed,
                    correctionDistance * Mathf.Sqrt(zone.HoverStrength));
            }

            SetEndControlledVelocity(
                zone,
                direction,
                currentFlowSpeed,
                holdTargetSpeed,
                boundaryCoordinate - playerLeadingEdge,
                fixedDeltaTime);
        }

        private void ApplyFlowAcceleration(
            UpdraftZone zone,
            Vector2 direction,
            float currentFlowSpeed,
            float fixedDeltaTime)
        {
            GetFlowMultipliers(
                zone,
                direction,
                out float accelerationMultiplier,
                out float maxSpeedMultiplier);
            float nextFlowSpeed = zone.GetProjectedFlowSpeed(
                currentFlowSpeed,
                fixedDeltaTime,
                accelerationMultiplier,
                maxSpeedMultiplier);
            if (!Mathf.Approximately(nextFlowSpeed, currentFlowSpeed))
            {
                if (zone.Direction == AirCurrentDirection.Up &&
                    zone.EndBehavior == AirCurrentEndBehavior.PassThrough &&
                    nextFlowSpeed > currentFlowSpeed)
                {
                    controller?.ProtectUpwardAirCurrentMomentum();
                }

                // Unity applies gravity after FixedUpdate. Compensate only that pending
                // gravity step for Up so Flow Acceleration keeps the validated net rate.
                // Horizontal currents deliberately preserve normal vertical gravity.
                float gravityDelta = GetPendingGravityDelta(zone, fixedDeltaTime);
                SetFlowVelocity(direction, nextFlowSpeed - gravityDelta);
            }

            RecordUpPassVelocitySample(
                zone,
                currentFlowSpeed,
                nextFlowSpeed,
                fixedDeltaTime);
        }

        private void GetFlowMultipliers(
            UpdraftZone zone,
            Vector2 direction,
            out float accelerationMultiplier,
            out float maxSpeedMultiplier)
        {
            accelerationMultiplier = 1f;
            maxSpeedMultiplier = 1f;
            if (direction.x == 0f || input == null)
                return;

            float inputX = Mathf.Clamp(input.MoveInput.x, -1f, 1f);
            if (inputX * direction.x <= 0.01f)
                return;

            accelerationMultiplier = zone.WithFlowAccelerationMultiplier;
            maxSpeedMultiplier = zone.WithFlowMaxSpeedMultiplier;
        }

        private void RecordUpPassVelocitySample(
            UpdraftZone zone,
            float currentFlowSpeed,
            float nextFlowSpeed,
            float fixedDeltaTime)
        {
            if (zone.Direction != AirCurrentDirection.Up ||
                zone.EndBehavior != AirCurrentEndBehavior.PassThrough)
            {
                return;
            }

            lastUpPassZone = zone;
            hasUpPassVelocitySample = true;
            lastUpPassVelocityBeforeEnd =
                !Mathf.Approximately(nextFlowSpeed, currentFlowSpeed)
                    ? nextFlowSpeed
                    : currentFlowSpeed + GetPendingGravityDelta(zone, fixedDeltaTime);
        }

        private void SetEndControlledVelocity(
            UpdraftZone zone,
            Vector2 direction,
            float currentFlowSpeed,
            float targetFlowSpeedAfterGravity,
            float distanceToEndBoundary,
            float fixedDeltaTime)
        {
            float gravityDelta = GetPendingGravityDelta(zone, fixedDeltaTime);
            float controlAcceleration = zone.HoverStrength;
            if (currentFlowSpeed > 0f)
            {
                float usableDistance = Mathf.Max(
                    0.01f,
                    distanceToEndBoundary - EndContainmentSafetyMargin);
                float requiredStoppingAcceleration =
                    currentFlowSpeed * currentFlowSpeed / (2f * usableDistance);
                controlAcceleration = Mathf.Max(
                    controlAcceleration,
                    requiredStoppingAcceleration);
            }

            float nextFlowSpeedAfterGravity = Mathf.MoveTowards(
                currentFlowSpeed,
                targetFlowSpeedAfterGravity,
                controlAcceleration * fixedDeltaTime);
            SetFlowVelocity(direction, nextFlowSpeedAfterGravity - gravityDelta);
        }

        private float GetPendingGravityDelta(UpdraftZone zone, float fixedDeltaTime)
        {
            return zone.Direction == AirCurrentDirection.Up
                ? Physics2D.gravity.y * body.gravityScale * fixedDeltaTime
                : 0f;
        }

        private void SetFlowVelocity(Vector2 direction, float targetFlowSpeed)
        {
            Vector2 currentVelocity = body.velocity;
            float currentFlowSpeed = Vector2.Dot(currentVelocity, direction);
            body.velocity = currentVelocity +
                direction * (targetFlowSpeed - currentFlowSpeed);
        }

        private void ApplyAerodynamicResistance()
        {
            if (!deployed || controller == null ||
                !controller.CanUseUpdraftCapeMotion)
            {
                return;
            }

            float verticalSpeed = body.velocity.y;
            if (verticalSpeed > 0.01f &&
                controller.HasProtectedUpwardAirCurrentMomentum)
            {
                return;
            }

            float resistance = verticalSpeed > 0.01f
                ? ascentAirResistance
                : verticalSpeed < -0.01f
                    ? descentAirResistance
                    : 0f;
            if (resistance <= 0f)
                return;

            float dragAcceleration = -verticalSpeed * resistance;
            body.AddForce(
                Vector2.up * (dragAcceleration * body.mass),
                ForceMode2D.Force);
        }

        private void ResolveMainCollider()
        {
            mainCollider = GetComponent<CapsuleCollider2D>();
            if (mainCollider == null)
                mainCollider = GetComponent<Collider2D>();
        }

        internal bool RegisterZoneContact(
            UpdraftZone zone,
            Collider2D playerCollider,
            string reason)
        {
            if (zone == null || playerCollider == null)
            {
                LogZoneEnter(zone, playerCollider, false, "invalid-contact");
                return false;
            }

            if (!overlappingZones.TryGetValue(zone, out HashSet<Collider2D> colliders))
            {
                colliders = new HashSet<Collider2D>();
                overlappingZones.Add(zone, colliders);
            }

            if (!colliders.Add(playerCollider))
                return true;

            string registrationReason = colliders.Count == 1
                ? reason
                : "additional-player-collider";
            LogZoneEnter(zone, playerCollider, true, registrationReason);
            return true;
        }

        internal void UnregisterZoneContact(UpdraftZone zone, Collider2D playerCollider)
        {
            if (zone == null || playerCollider == null ||
                !overlappingZones.TryGetValue(zone, out HashSet<Collider2D> colliders) ||
                !colliders.Remove(playerCollider))
            {
                return;
            }

            if (colliders.Count > 0)
                return;

            overlappingZones.Remove(zone);
            if (liftActive && liftZone == zone)
            {
                LogFlowExit(zone);
                LogUpPassExit(zone);
            }

            if (activeZone == zone)
                SetActiveZone(null);
            if (liftZone == zone)
                StopLift("exited", true);
        }

        private void Subscribe()
        {
            if (subscribed)
                return;

            if (health != null)
            {
                health.OnDied += HandlePlayerDied;
                health.OnRespawned += HandlePlayerRespawned;
            }

            subscribed = health != null;
        }

        private void Unsubscribe()
        {
            if (health != null)
            {
                health.OnDied -= HandlePlayerDied;
                health.OnRespawned -= HandlePlayerRespawned;
            }

            subscribed = false;
        }

        private void HandlePlayerDied()
        {
            playerAlive = false;
            SetDeployed(false);
            StopLift("dead");
        }

        private void HandlePlayerRespawned()
        {
            playerAlive = true;
        }

        private void SetDeployed(bool value)
        {
            if (deployed == value)
                return;

            deployed = value;
            if (DiagnosticsEnabled)
            {
                bool held = input != null && input.CapeHeld;
                Debug.Log($"[CAPE][DEPLOY] held={held}", this);
            }
        }

        private bool TryGetEffectiveZone(
            Vector2 currentVelocity,
            out UpdraftZone effectiveZone)
        {
            effectiveZone = null;
            float bestProjectedSpeed = float.NegativeInfinity;
            staleZones.Clear();

            foreach (KeyValuePair<UpdraftZone, HashSet<Collider2D>> entry in overlappingZones)
            {
                UpdraftZone candidate = entry.Key;
                if (candidate == null || !candidate.isActiveAndEnabled ||
                    entry.Value == null || entry.Value.Count == 0)
                {
                    staleZones.Add(candidate);
                    continue;
                }

                float projectedSpeed = candidate.GetProjectedFlowSpeed(
                    currentVelocity,
                    Time.fixedDeltaTime);
                if (IsStronger(candidate, projectedSpeed, effectiveZone, bestProjectedSpeed))
                {
                    effectiveZone = candidate;
                    bestProjectedSpeed = projectedSpeed;
                }
            }

            for (int i = 0; i < staleZones.Count; i++)
                overlappingZones.Remove(staleZones[i]);

            return effectiveZone != null;
        }

        private static bool IsStronger(
            UpdraftZone candidate,
            float candidateProjectedSpeed,
            UpdraftZone currentBest,
            float currentBestProjectedSpeed)
        {
            if (currentBest == null)
                return true;
            if (candidateProjectedSpeed > currentBestProjectedSpeed + StrengthComparisonEpsilon)
                return true;
            if (candidateProjectedSpeed < currentBestProjectedSpeed - StrengthComparisonEpsilon)
                return false;
            if (candidate.MaxFlowSpeed > currentBest.MaxFlowSpeed + StrengthComparisonEpsilon)
                return true;
            if (candidate.MaxFlowSpeed < currentBest.MaxFlowSpeed - StrengthComparisonEpsilon)
                return false;
            if (candidate.EffectiveFlowAcceleration >
                currentBest.EffectiveFlowAcceleration + StrengthComparisonEpsilon)
            {
                return true;
            }
            if (candidate.EffectiveFlowAcceleration <
                currentBest.EffectiveFlowAcceleration - StrengthComparisonEpsilon)
            {
                return false;
            }

            return candidate.GetInstanceID() < currentBest.GetInstanceID();
        }

        private string GetBasicMotionBlockReason()
        {
            if (!playerAlive)
                return "dead";
            if (input == null)
                return "input-unresolved";
            if (!input.CapeHeld)
                return "released";
            if (body == null)
                return "rigidbody-unresolved";
            if (!body.simulated)
                return "rigidbody-not-simulated";
            if (controller == null)
                return "controller-unresolved";

            return null;
        }

        private string GetPlayerMotionBlockReason()
        {
            if (!controller.isActiveAndEnabled)
                return "controller-disabled";
            if (body.bodyType != RigidbodyType2D.Dynamic)
                return "rigidbody-not-dynamic";
            if (controller.IsExternalControlLocked)
                return "external-lock";
            if (controller.IsGrounded)
                return "grounded";
            if (controller.IsDashing)
                return "dash";
            if (controller.IsWallSliding)
                return "wall-slide";
            if (controller.IsNormalJumpAnticipating)
                return "jump-transition";
            if (controller.IsHanging)
                return "ledge";
            if (controller.IsSwinging)
                return "swing";
            if (controller.IsOverheadRailHanging)
                return "overhead-rail";
            if (controller.IsSliding)
                return "slide";
            if (controller.IsDiving)
                return "dive";
            if (controller.IsPostDiveSliding)
                return "post-dive-slide";
            if (controller.IsMoveLockedByParry)
                return "parry-lock";

            Joint2D[] joints = GetComponents<Joint2D>();
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] != null && joints[i].enabled)
                    return "joint";
            }

            return "player-transition";
        }

        private void LogBindState()
        {
            if (!DiagnosticsEnabled)
                return;

            Debug.Log(
                "[CAPE][BIND] baseAbility=True " +
                $"inputResolved={input != null} " +
                $"controllerResolved={controller != null} " +
                $"rigidbodyResolved={body != null} " +
                $"mainColliderResolved={mainCollider != null}",
                this);
        }

        private void LogZoneEnter(
            UpdraftZone zone,
            Collider2D playerCollider,
            bool registered,
            string reason)
        {
            if (!DiagnosticsEnabled)
                return;

            string zoneName = zone != null ? zone.name : "<null>";
            string colliderName = playerCollider != null
                ? $"{playerCollider.name}:{playerCollider.GetType().Name}"
                : "<null>";
            Debug.Log(
                $"[CAPE][ZONE ENTER] zone={zoneName} collider={colliderName} " +
                $"registered={registered} reason={reason}",
                this);
        }

        private void SetActiveZone(UpdraftZone zone)
        {
            if (activeZone == zone)
                return;

            activeZone = zone;
            if (DiagnosticsEnabled && activeZone != null)
                Debug.Log($"[CAPE][ACTIVE ZONE] zone={activeZone.name}", this);
        }

        private void SetMotionBlockReason(string reason)
        {
            if (lastMotionBlockReason == reason)
                return;

            lastMotionBlockReason = reason;
            if (!string.IsNullOrEmpty(reason))
                LogFlowBlocked(reason);
        }

        private void StartLift(UpdraftZone zone)
        {
            if (liftActive && liftZone == zone)
                return;

            if (liftActive)
                StopLift("active-zone-changed", false);

            liftActive = true;
            liftZone = zone;
            hovering = false;
            lastUpPassZone = null;
            hasUpPassVelocitySample = false;
            LogFlowEnter(zone);
        }

        private void StartHover()
        {
            if (hovering)
                return;

            hovering = true;
            if (DiagnosticsEnabled)
                Debug.Log("[CAPE][HOVER START]", this);
        }

        private void StopLift(string reason, bool preserveHorizontalMomentum = false)
        {
            if (liftActive && preserveHorizontalMomentum)
                BeginPostFlowTrace(liftZone, reason);

            SetHorizontalAirCurrentInfluence(null, preserveHorizontalMomentum);
            if (!liftActive)
                return;

            liftActive = false;
            liftZone = null;
            hovering = false;
            if (DiagnosticsEnabled)
                Debug.Log($"[CAPE][LIFT STOP] reason={reason}", this);
        }

        private void SetHorizontalAirCurrentInfluence(
            UpdraftZone zone,
            bool preserveMomentumOnRelease)
        {
            if (controller == null)
                return;

            bool active = zone != null && zone.Direction != AirCurrentDirection.Up;
            controller.SetHorizontalAirCurrentInfluence(
                active,
                active ? zone.FlowDirection.x : 0f,
                active ? zone.AgainstFlowControl : 1f,
                preserveMomentumOnRelease);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogFlowEnter(UpdraftZone zone)
        {
            if (!DiagnosticsEnabled || zone == null || body == null)
                return;

            Vector2 velocity = body.velocity;
            float projectedSpeed = Vector2.Dot(velocity, zone.FlowDirection);
            Debug.Log(
                $"[CAPE][FLOW ENTER] zone={zone.name} " +
                $"direction={zone.Direction} endBehavior={zone.EndBehavior} " +
                $"velocity={velocity} projectedSpeed={projectedSpeed:0.###}",
                this);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogFlowBlocked(string reason)
        {
            if (!DiagnosticsEnabled)
                return;

            UpdraftZone diagnosticZone = liftZone != null ? liftZone : activeZone;
            string direction = diagnosticZone != null
                ? diagnosticZone.Direction.ToString()
                : "None";
            Debug.Log(
                $"[CAPE][FLOW BLOCKED] direction={direction} reason={reason}",
                this);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogFlowExit(UpdraftZone zone)
        {
            if (!DiagnosticsEnabled || zone == null || body == null)
                return;

            Debug.Log(
                $"[CAPE][FLOW EXIT] direction={zone.Direction} velocity={body.velocity}",
                this);
        }

        private void BeginPostFlowTrace(UpdraftZone zone, string reason)
        {
            if (zone == null)
                return;

            postFlowTraceDirection = zone.FlowDirection;
            postFlowTraceReason = reason;
            postFlowTraceFixedStep = 0;
            postFlowTraceFixedStepsRemaining = 3;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogPostFlowTrace()
        {
            if (postFlowTraceFixedStepsRemaining <= 0 || body == null)
                return;

            if (DiagnosticsEnabled)
            {
                float projectedSpeed = Vector2.Dot(body.velocity, postFlowTraceDirection);
                Debug.Log(
                    $"[CAPE][FLOW POST EXIT] reason={postFlowTraceReason} " +
                    $"fixedStep=+{postFlowTraceFixedStep} velocity={body.velocity} " +
                    $"projectedSpeed={projectedSpeed:0.###}",
                    this);
            }

            postFlowTraceFixedStep++;
            postFlowTraceFixedStepsRemaining--;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogUpPassExit(UpdraftZone zone)
        {
            if (!DiagnosticsEnabled || body == null ||
                !hasUpPassVelocitySample || lastUpPassZone != zone ||
                zone.Direction != AirCurrentDirection.Up ||
                zone.EndBehavior != AirCurrentEndBehavior.PassThrough)
            {
                return;
            }

            Debug.Log(
                $"[CAPE][UP PASS] velocityBeforeEnd={lastUpPassVelocityBeforeEnd:0.###} " +
                $"velocityAtExit={body.velocity.y:0.###}",
                this);
        }

        private void CreatePlaceholderVisual()
        {
            if (!createPlaceholderVisual || capeVisual != null)
                return;

            SpriteRenderer playerRenderer = GetComponentInChildren<SpriteRenderer>(true);
            capeVisual = new GameObject("[UpdraftCapeVisual]");
            capeVisual.layer = gameObject.layer;
            capeVisual.transform.SetParent(transform, false);
            capeRenderer = capeVisual.AddComponent<SpriteRenderer>();
            placeholderSprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f);
            placeholderSprite.name = "UpdraftCapePlaceholder";
            capeRenderer.sprite = placeholderSprite;

            if (playerRenderer != null)
            {
                capeRenderer.sortingLayerID = playerRenderer.sortingLayerID;
                capeRenderer.sortingOrder = playerRenderer.sortingOrder - 1;
            }

            UpdateVisual();
        }

        private void UpdateVisual()
        {
            if (capeVisual == null || capeRenderer == null)
                return;

            capeVisual.SetActive(true);

            int facing = controller != null && controller.FacingLeft ? -1 : 1;
            capeVisual.transform.localPosition = new Vector3(-0.28f * facing, 0.05f, 0f);
            capeVisual.transform.localRotation = Quaternion.Euler(
                0f,
                0f,
                (deployed ? 58f : 12f) * facing);
            capeVisual.transform.localScale = deployed
                ? new Vector3(0.3f, 0.9f, 1f)
                : new Vector3(0.22f, 0.62f, 1f);
            capeRenderer.color = deployed ? deployedColor : stowedColor;
        }
    }
}

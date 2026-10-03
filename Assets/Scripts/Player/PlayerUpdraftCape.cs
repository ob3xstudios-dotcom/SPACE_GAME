using System.Collections.Generic;
using Game.Input;
using Game.Progression;
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
        private const float TopContainmentSafetyMargin = 0.02f;

        [Header("Provisional Visual")]
        [SerializeField] private bool createPlaceholderVisual = true;
        [SerializeField] private Color stowedColor = new Color(0.15f, 0.75f, 0.95f, 0.9f);
        [SerializeField] private Color deployedColor = new Color(0.35f, 0.95f, 1f, 0.95f);

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
        private ProgressionService progression;
        private Rigidbody2D body;
        private Collider2D mainCollider;
        private GameObject capeVisual;
        private SpriteRenderer capeRenderer;
        private Sprite placeholderSprite;
        private bool equipped;
        private bool deployed;
        private bool playerAlive = true;
        private bool subscribed;
        private bool liftActive;
        private bool hovering;
        private UpdraftZone activeZone;
        private UpdraftZone liftZone;
        private string lastMotionBlockReason;

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
            RefreshEquippedState();
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

        internal void Bind(PlayerController playerController, ProgressionService progressionService)
        {
            Unsubscribe();
            controller = playerController;
            progression = progressionService;
            body = GetComponent<Rigidbody2D>();
            input = GetComponent<InputReader>();
            health = GetComponent<PlayerHealth>();
            ResolveMainCollider();
            playerAlive = true;
            CreatePlaceholderVisual();
            Subscribe();
            RefreshEquippedState();
            LogBindState();
        }

        private void Update()
        {
            SetDeployed(equipped && playerAlive && input != null && input.CapeHeld);
            UpdateVisual();
        }

        private void FixedUpdate()
        {
            string basicBlockReason = GetBasicMotionBlockReason();
            if (basicBlockReason != null)
            {
                SetMotionBlockReason(basicBlockReason);
                StopLift(basicBlockReason);
                return;
            }

            if (!TryGetEffectiveZone(body.velocity.y, out UpdraftZone zone))
            {
                SetMotionBlockReason("no-zone");
                StopLift("no-zone");
                SetActiveZone(null);
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
            ApplyVerticalMotion(zone);
        }

        private void ApplyVerticalMotion(UpdraftZone zone)
        {
            float currentVerticalSpeed = body.velocity.y;
            float fixedDeltaTime = Time.fixedDeltaTime;
            if (mainCollider == null || !mainCollider.enabled ||
                !zone.TryGetTopHoverY(out float hoverY, out float topBoundaryY))
            {
                hovering = false;
                ApplyNormalLift(zone, currentVerticalSpeed, fixedDeltaTime);
                return;
            }

            // Control the Player collider's upper edge instead of the transform pivot.
            // This leaves the configured inset physically inside the trigger volume.
            float positionError = hoverY - mainCollider.bounds.max.y;
            float tolerance = zone.HoverTolerance;
            if (positionError > tolerance)
            {
                hovering = false;
                float safeRiseSpeed = Mathf.Sqrt(
                    2f * zone.HoverStrength * (positionError - tolerance));
                if (safeRiseSpeed >= zone.MaxRiseSpeed)
                {
                    ApplyNormalLift(zone, currentVerticalSpeed, fixedDeltaTime);
                    return;
                }

                float targetVerticalSpeed = Mathf.Min(zone.MaxRiseSpeed, safeRiseSpeed);
                SetHoverControlledVelocity(
                    zone,
                    currentVerticalSpeed,
                    targetVerticalSpeed,
                    topBoundaryY - mainCollider.bounds.max.y,
                    fixedDeltaTime);
                return;
            }

            StartHover();
            float hoverTargetSpeed = 0f;
            if (positionError < -tolerance)
            {
                float correctionDistance = -positionError - tolerance;
                hoverTargetSpeed = -Mathf.Min(
                    zone.HoverCorrectionSpeed,
                    correctionDistance * Mathf.Sqrt(zone.HoverStrength));
            }

            SetHoverControlledVelocity(
                zone,
                currentVerticalSpeed,
                hoverTargetSpeed,
                topBoundaryY - mainCollider.bounds.max.y,
                fixedDeltaTime);
        }

        private void ApplyNormalLift(
            UpdraftZone zone,
            float currentVerticalSpeed,
            float fixedDeltaTime)
        {
            float nextVerticalSpeed = zone.GetProjectedVerticalSpeed(
                currentVerticalSpeed,
                fixedDeltaTime);
            if (nextVerticalSpeed > currentVerticalSpeed)
            {
                // Unity applies gravity after FixedUpdate. Compensate only that pending
                // gravity step so Lift Acceleration remains the net approach rate.
                float gravityDelta = Physics2D.gravity.y * body.gravityScale * fixedDeltaTime;
                float speedBeforeGravity = nextVerticalSpeed - gravityDelta;
                body.velocity = new Vector2(body.velocity.x, speedBeforeGravity);
            }
        }

        private void SetHoverControlledVelocity(
            UpdraftZone zone,
            float currentVerticalSpeed,
            float targetVerticalSpeedAfterGravity,
            float distanceToTopBoundary,
            float fixedDeltaTime)
        {
            float gravityDelta = Physics2D.gravity.y * body.gravityScale * fixedDeltaTime;
            float controlAcceleration = zone.HoverStrength;
            if (currentVerticalSpeed > 0f)
            {
                float usableDistance = Mathf.Max(
                    0.01f,
                    distanceToTopBoundary - TopContainmentSafetyMargin);
                float requiredStoppingAcceleration =
                    currentVerticalSpeed * currentVerticalSpeed / (2f * usableDistance);
                controlAcceleration = Mathf.Max(
                    controlAcceleration,
                    requiredStoppingAcceleration);
            }

            float nextVerticalSpeedAfterGravity = Mathf.MoveTowards(
                currentVerticalSpeed,
                targetVerticalSpeedAfterGravity,
                controlAcceleration * fixedDeltaTime);
            float nextVerticalSpeedBeforeGravity =
                nextVerticalSpeedAfterGravity - gravityDelta;
            body.velocity = new Vector2(body.velocity.x, nextVerticalSpeedBeforeGravity);
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
            if (DiagnosticsEnabled)
                Debug.Log($"[CAPE][ZONE EXIT] zone={zone.name}", this);

            if (activeZone == zone)
                SetActiveZone(null);
            if (liftZone == zone)
                StopLift("exited");
        }

        private void Subscribe()
        {
            if (subscribed)
                return;

            if (progression != null)
                progression.OnProgressionChanged += RefreshEquippedState;
            if (health != null)
            {
                health.OnDied += HandlePlayerDied;
                health.OnRespawned += HandlePlayerRespawned;
            }

            subscribed = progression != null || health != null;
        }

        private void Unsubscribe()
        {
            if (progression != null)
                progression.OnProgressionChanged -= RefreshEquippedState;
            if (health != null)
            {
                health.OnDied -= HandlePlayerDied;
                health.OnRespawned -= HandlePlayerRespawned;
            }

            subscribed = false;
        }

        private void RefreshEquippedState()
        {
            bool nextEquipped = progression != null &&
                progression.IsAbilityEquipped(ProgressionAbilityIds.UpdraftCape);
            equipped = nextEquipped;
            if (!equipped)
            {
                SetDeployed(false);
                StopLift("ability-unequipped");
            }
            UpdateVisual();
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
            float currentVerticalSpeed,
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

                float projectedSpeed = candidate.GetProjectedVerticalSpeed(
                    currentVerticalSpeed,
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
            if (candidate.MaxRiseSpeed > currentBest.MaxRiseSpeed + StrengthComparisonEpsilon)
                return true;
            if (candidate.MaxRiseSpeed < currentBest.MaxRiseSpeed - StrengthComparisonEpsilon)
                return false;
            if (candidate.EffectiveLiftAcceleration >
                currentBest.EffectiveLiftAcceleration + StrengthComparisonEpsilon)
            {
                return true;
            }
            if (candidate.EffectiveLiftAcceleration <
                currentBest.EffectiveLiftAcceleration - StrengthComparisonEpsilon)
            {
                return false;
            }

            return candidate.GetInstanceID() < currentBest.GetInstanceID();
        }

        private string GetBasicMotionBlockReason()
        {
            if (!equipped)
                return "not-equipped";
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
                $"[CAPE][BIND] equipped={equipped} " +
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
            if (DiagnosticsEnabled && !string.IsNullOrEmpty(reason))
                Debug.Log($"[CAPE][MOTION BLOCKED] reason={reason}", this);
        }

        private void StartLift(UpdraftZone zone)
        {
            if (liftActive && liftZone == zone)
                return;

            if (liftActive)
                StopLift("active-zone-changed");

            liftActive = true;
            liftZone = zone;
            hovering = false;
            if (DiagnosticsEnabled)
                Debug.Log($"[CAPE][LIFT START] zone={zone.name}", this);
        }

        private void StartHover()
        {
            if (hovering)
                return;

            hovering = true;
            if (DiagnosticsEnabled)
                Debug.Log("[CAPE][HOVER START]", this);
        }

        private void StopLift(string reason)
        {
            if (!liftActive)
                return;

            liftActive = false;
            liftZone = null;
            hovering = false;
            if (DiagnosticsEnabled)
                Debug.Log($"[CAPE][LIFT STOP] reason={reason}", this);
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

            capeVisual.SetActive(equipped);
            if (!equipped)
                return;

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

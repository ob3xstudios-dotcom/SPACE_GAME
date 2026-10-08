using Game.Input;
using Game.Player;
using UnityEngine;

namespace Game.World.Traversal
{
    public enum FlagSpringPoleState
    {
        Idle,
        Compressing,
        Rebounding,
        Settling,
        HangingStable
    }

    /// <summary>
    /// Deterministic spring-pole traversal. The pole owns its elastic cycle while
    /// PlayerController owns player physics and input state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlagSpringPole : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform polePivot;
        [SerializeField] private Transform grabPoint;
        [SerializeField] private CircleCollider2D grabTrigger;

        [Header("Spring Cycle")]
        [SerializeField, Min(0.01f)] private float compressionDuration = 0.7f;
        [SerializeField] private float compressedAngle = -45f;
        [SerializeField, Min(0.01f)] private float reboundDuration = 0.25f;
        [SerializeField] private float reboundOvershootAngle = 25f;
        [SerializeField, Min(0.01f)] private float settleDuration = 0.3f;

        [Header("Grab")]
        [SerializeField, Min(0.05f)] private float grabRadius = 0.45f;
        [SerializeField, Min(0f)] private float grabAssistHorizontal = 0.6f;
        [SerializeField, Min(0f)] private float grabAssistAboveHead = 0.65f;
        [SerializeField, Range(0f, 1f)] private float grabAssistLowestBodyFraction = 0.5f;
        [SerializeField, Min(0f)] private float samePoleRegrabCooldown = 0.15f;
        [SerializeField] private bool trackPlayerAtGrabPoint = true;
        [SerializeField] private LayerMask playerLayers = 512;

        [Header("Launch")]
        [SerializeField, Min(0f)] private float minReboundJumpMultiplier = 1f;
        [SerializeField, Min(0f)] private float maxReboundJumpMultiplier = 1.7f;
        [SerializeField, Min(0f)] private float horizontalLaunchSpeed = 2f;
        [SerializeField, Range(0f, 1f)] private float horizontalInputDeadZone = 0.2f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs;
        [SerializeField] private bool drawGizmos = true;

        private readonly Collider2D[] grabCandidates = new Collider2D[8];
        private PlayerController attachedPlayer;
        private PlayerController lastDetachedPlayer;
        private FlagSpringPoleState currentState = FlagSpringPoleState.Idle;
        private float stateTimer;
        private float currentAngle;
        private float regrabAllowedTime;
        private bool samePlayerMustExitGrabRadius;
        private bool rearmLogged;

        public FlagSpringPoleState CurrentState => currentState;
        public Vector2 GrabPosition => grabPoint != null
            ? grabPoint.position
            : transform.position;
        public float GrabRadius => Mathf.Max(0.05f, grabRadius);
        public float CurrentAngle => currentAngle;
        public float ReboundProgress => currentState == FlagSpringPoleState.Rebounding
            ? Mathf.InverseLerp(compressedAngle, reboundOvershootAngle, currentAngle)
            : 0f;

        private void Awake()
        {
            ResolveReferences();
            SetPoleAngle(0f);
        }

        private void Update()
        {
            UpdateRearmRequirement();
            TryGrabNearbyPlayer();
        }

        private void FixedUpdate()
        {
            if (attachedPlayer == null)
            {
                ReturnUnoccupiedPoleToIdle();
                return;
            }

            AdvanceSpringCycle(Time.fixedDeltaTime);
            if (trackPlayerAtGrabPoint)
                attachedPlayer.FollowFlagSpringPole(this, GrabPosition);
        }

        private void OnDisable()
        {
            PlayerController player = attachedPlayer;
            attachedPlayer = null;
            if (player != null)
                player.ForceExitFlagSpringPole(this);

            currentState = FlagSpringPoleState.Idle;
            stateTimer = 0f;
            SetPoleAngle(0f);
        }

        private void Reset()
        {
            ResolveReferences();
        }

        private void OnValidate()
        {
            compressionDuration = Mathf.Max(0.01f, compressionDuration);
            reboundDuration = Mathf.Max(0.01f, reboundDuration);
            settleDuration = Mathf.Max(0.01f, settleDuration);
            grabRadius = Mathf.Max(0.05f, grabRadius);
            grabAssistHorizontal = Mathf.Max(0f, grabAssistHorizontal);
            grabAssistAboveHead = Mathf.Max(0f, grabAssistAboveHead);
            grabAssistLowestBodyFraction = Mathf.Clamp01(grabAssistLowestBodyFraction);
            samePoleRegrabCooldown = Mathf.Max(0f, samePoleRegrabCooldown);
            minReboundJumpMultiplier = Mathf.Max(0f, minReboundJumpMultiplier);
            maxReboundJumpMultiplier = Mathf.Max(
                minReboundJumpMultiplier,
                maxReboundJumpMultiplier);
            horizontalLaunchSpeed = Mathf.Max(0f, horizontalLaunchSpeed);
            ResolveReferences();

            if (grabTrigger != null)
            {
                grabTrigger.isTrigger = true;
                grabTrigger.radius = grabRadius;
            }
        }

        public bool CanGrab(PlayerController player)
        {
            if (player == null || attachedPlayer != null || !isActiveAndEnabled)
                return false;
            if (player.IsGrounded || player.IsExternalControlLocked)
                return false;
            if (!IsWithinGrabReach(player, GrabRadius, 0f))
                return false;

            return player != lastDetachedPlayer ||
                   (!samePlayerMustExitGrabRadius && Time.time >= regrabAllowedTime);
        }

        public bool Attach(PlayerController player)
        {
            if (!CanGrab(player))
                return false;

            attachedPlayer = player;
            lastDetachedPlayer = null;
            samePlayerMustExitGrabRadius = false;
            rearmLogged = true;
            stateTimer = 0f;
            SetPoleAngle(0f);
            TransitionTo(FlagSpringPoleState.Compressing);
            Log("[FLAGPOLE][GRAB]");
            return true;
        }

        public Vector2 CalculateLaunchVelocity(float normalJumpSpeed, float horizontalInput)
        {
            float progress = ReboundProgress;
            float multiplier = currentState == FlagSpringPoleState.Rebounding
                ? Mathf.Lerp(minReboundJumpMultiplier, maxReboundJumpMultiplier, progress)
                : 1f;
            float launchX = Mathf.Abs(horizontalInput) > horizontalInputDeadZone
                ? Mathf.Sign(horizontalInput) * horizontalLaunchSpeed
                : 0f;
            return new Vector2(launchX, Mathf.Max(0f, normalJumpSpeed) * multiplier);
        }

        public void NotifyPlayerLaunched(PlayerController player, Vector2 launchVelocity)
        {
            if (player != attachedPlayer)
                return;

            Log(
                $"[FLAGPOLE][LAUNCH] progress={ReboundProgress:0.###} " +
                $"angle={currentAngle:0.###} launchVelocity={launchVelocity}");
            Detach(player);
        }

        public void NotifyPlayerDropped(PlayerController player)
        {
            if (player != attachedPlayer)
                return;

            Log("[FLAGPOLE][DROP]");
            Detach(player);
        }

        private void Detach(PlayerController player)
        {
            attachedPlayer = null;
            lastDetachedPlayer = player;
            regrabAllowedTime = Time.time + samePoleRegrabCooldown;
            samePlayerMustExitGrabRadius = true;
            rearmLogged = false;
            stateTimer = 0f;
            currentState = FlagSpringPoleState.Idle;
        }

        private void TryGrabNearbyPlayer()
        {
            if (attachedPlayer != null || playerLayers.value == 0)
                return;

            int count = Physics2D.OverlapCircleNonAlloc(
                GrabPosition,
                GrabRadius,
                grabCandidates,
                playerLayers);
            if (TryGrabCandidatePlayers(count))
                return;

            if (grabAssistHorizontal <= 0f)
                return;

            count = Physics2D.OverlapBoxNonAlloc(
                GrabPosition,
                GetAssistCandidateSearchSize(),
                0f,
                grabCandidates,
                playerLayers);
            TryGrabCandidatePlayers(count);
        }

        private bool TryGrabCandidatePlayers(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Collider2D candidate = grabCandidates[i];
                if (candidate == null || candidate.isTrigger)
                    continue;

                PlayerController player = candidate.GetComponentInParent<PlayerController>();
                if (!CanGrab(player))
                    continue;

                InputReader playerInput = player.GetComponent<InputReader>();
                if (playerInput == null || !playerInput.HasBufferedSwingGrabPressed())
                    continue;

                if (player.TryEnterFlagSpringPole(this))
                    return true;
            }

            return false;
        }

        private void UpdateRearmRequirement()
        {
            if (lastDetachedPlayer == null || rearmLogged)
                return;

            if (samePlayerMustExitGrabRadius)
            {
                float exitRadius = GrabRadius + 0.1f;
                if (IsWithinGrabReach(lastDetachedPlayer, exitRadius, 0.1f))
                    return;

                samePlayerMustExitGrabRadius = false;
            }

            if (Time.time >= regrabAllowedTime)
            {
                rearmLogged = true;
                Log("[FLAGPOLE][REARM]");
            }
        }

        private bool IsWithinGrabReach(
            PlayerController player,
            float normalRadius,
            float assistPadding)
        {
            if (player == null)
                return false;

            float radius = Mathf.Max(0.05f, normalRadius);
            if ((player.SwingHandPosition - GrabPosition).sqrMagnitude <= radius * radius)
                return true;

            if (!TryGetPlayerPhysicalBounds(player, out Bounds playerBounds))
                return false;

            float padding = Mathf.Max(0f, assistPadding);
            float horizontalDelta = Mathf.Abs(GrabPosition.x - playerBounds.center.x);
            float lowestAssistY = Mathf.Lerp(
                playerBounds.min.y,
                playerBounds.max.y,
                grabAssistLowestBodyFraction);
            float highestAssistY = playerBounds.max.y + grabAssistAboveHead;
            return horizontalDelta <= grabAssistHorizontal + padding &&
                   GrabPosition.y >= lowestAssistY - padding &&
                   GrabPosition.y <= highestAssistY + padding;
        }

        private Vector2 GetAssistCandidateSearchSize()
        {
            float halfHeight = Mathf.Max(0.01f, grabAssistAboveHead);
            return new Vector2(grabAssistHorizontal * 2f, halfHeight * 2f);
        }

        private static bool TryGetPlayerPhysicalBounds(
            PlayerController player,
            out Bounds playerBounds)
        {
            playerBounds = default;
            Collider2D physicalCollider = player.GetComponent<Collider2D>();
            if (physicalCollider == null || physicalCollider.isTrigger || !physicalCollider.enabled)
            {
                Collider2D[] colliders = player.GetComponentsInChildren<Collider2D>(true);
                physicalCollider = null;
                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider2D candidate = colliders[i];
                    if (candidate == null || candidate.isTrigger || !candidate.enabled)
                        continue;

                    physicalCollider = candidate;
                    break;
                }
            }

            if (physicalCollider == null)
                return false;

            playerBounds = physicalCollider.bounds;
            return true;
        }

        private void AdvanceSpringCycle(float deltaTime)
        {
            switch (currentState)
            {
                case FlagSpringPoleState.Compressing:
                    SetPoleAngle(EaseAngle(0f, compressedAngle, compressionDuration, deltaTime));
                    if (stateTimer >= compressionDuration)
                        TransitionTo(FlagSpringPoleState.Rebounding);
                    break;

                case FlagSpringPoleState.Rebounding:
                    SetPoleAngle(EaseAngle(
                        compressedAngle,
                        reboundOvershootAngle,
                        reboundDuration,
                        deltaTime));
                    if (stateTimer >= reboundDuration)
                        TransitionTo(FlagSpringPoleState.Settling);
                    break;

                case FlagSpringPoleState.Settling:
                    SetPoleAngle(EaseAngle(
                        reboundOvershootAngle,
                        0f,
                        settleDuration,
                        deltaTime));
                    if (stateTimer >= settleDuration)
                    {
                        SetPoleAngle(0f);
                        TransitionTo(FlagSpringPoleState.HangingStable);
                    }
                    break;
            }
        }

        private float EaseAngle(float from, float to, float duration, float deltaTime)
        {
            stateTimer += deltaTime;
            float t = Mathf.Clamp01(stateTimer / Mathf.Max(0.01f, duration));
            float smoothT = t * t * (3f - 2f * t);
            return Mathf.Lerp(from, to, smoothT);
        }

        private void ReturnUnoccupiedPoleToIdle()
        {
            if (Mathf.Abs(currentAngle) <= 0.01f)
            {
                SetPoleAngle(0f);
                return;
            }

            float returnSpeed = Mathf.Max(
                Mathf.Abs(compressedAngle),
                Mathf.Abs(reboundOvershootAngle)) / Mathf.Max(0.01f, settleDuration);
            SetPoleAngle(Mathf.MoveTowards(
                currentAngle,
                0f,
                returnSpeed * Time.fixedDeltaTime));
        }

        private void TransitionTo(FlagSpringPoleState nextState)
        {
            FlagSpringPoleState previous = currentState;
            currentState = nextState;
            stateTimer = 0f;
            Log($"[FLAGPOLE][STATE] {previous} -> {nextState}");
        }

        private void SetPoleAngle(float angle)
        {
            currentAngle = angle;
            if (polePivot != null)
                polePivot.localRotation = Quaternion.Euler(0f, 0f, currentAngle);
        }

        private void ResolveReferences()
        {
            if (polePivot == null)
                polePivot = transform.Find("PolePivot");
            if (polePivot != null && grabPoint == null)
                grabPoint = polePivot.Find("GrabPoint");
            if (grabPoint != null && grabTrigger == null)
                grabTrigger = grabPoint.GetComponent<CircleCollider2D>();
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void Log(string message)
        {
            if (debugLogs)
                Debug.Log(message, this);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
                return;

            ResolveReferences();
            Vector3 pivotPosition = polePivot != null
                ? polePivot.position
                : transform.position;
            Vector3 grabPosition = grabPoint != null
                ? grabPoint.position
                : pivotPosition + Vector3.right * 3f;
            float poleLength = Mathf.Max(0.1f, Vector3.Distance(pivotPosition, grabPosition));

            Gizmos.color = new Color(1f, 0.8f, 0.15f, 1f);
            Gizmos.DrawSphere(pivotPosition, 0.07f);
            Gizmos.DrawWireSphere(grabPosition, GrabRadius);

            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.8f);
            Gizmos.DrawWireCube(
                grabPosition,
                GetAssistCandidateSearchSize());

            Vector3 previous = GetArcPoint(pivotPosition, poleLength, compressedAngle);
            const int segments = 20;
            for (int i = 1; i <= segments; i++)
            {
                float angle = Mathf.Lerp(
                    compressedAngle,
                    reboundOvershootAngle,
                    i / (float)segments);
                Vector3 next = GetArcPoint(pivotPosition, poleLength, angle);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(pivotPosition, GetArcPoint(pivotPosition, poleLength, 0f));
        }

        private static Vector3 GetArcPoint(Vector3 pivotPosition, float length, float angle)
        {
            Vector3 direction = Quaternion.Euler(0f, 0f, angle) * Vector3.right;
            return pivotPosition + direction * length;
        }
    }
}

using Game.Player;
using UnityEngine;

namespace Game.World.Traps
{
    public enum CaveStalactiteState
    {
        Armed,
        Warning,
        WaitingForDropZone,
        Falling,
        Embedded
    }

    /// <summary>
    /// One-shot proximity trap. It predicts a vertical interception from a configured
    /// reference walking speed, but never follows or moves horizontally toward the Player.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class CaveStalactiteController : MonoBehaviour
    {
        private const int CastCapacity = 16;
        private const int PlayerOverlapCapacity = 8;
        private const float MinimumDimension = 0.01f;

        [Header("References")]
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private GameObject fallingDamageRoot;
        [Tooltip("Also defines the full body volume used by the downward safety casts.")]
        [SerializeField] private BoxCollider2D fallingDamageCollider;
        [SerializeField] private BoxCollider2D embeddedPlatformCollider;
        [SerializeField] private CaveStalactitePrototypeVisual prototypeVisual;

        [Header("Proximity")]
        [SerializeField, Min(0.1f)] private float anticipationWidth = 10f;
        [SerializeField, Min(0.1f)] private float anticipationDepth = 8f;
        [SerializeField, Min(0f)] private float warningDuration = 0.45f;
        [SerializeField] private LayerMask playerMask = 1 << 9;

        [Header("Drop Timing")]
        [Tooltip("Stable horizontal speed used to predict when a normally walking Player reaches the fall line.")]
        [SerializeField, Min(0f)] private float referencePlayerHorizontalSpeed = 7f;
        [Tooltip("Used by edit-mode gizmos. Runtime timing uses the detected Player collider.")]
        [SerializeField, Min(0.1f)] private float referencePlayerHeight = 1.82f;
        [Tooltip("Used by edit-mode gizmos. Runtime timing uses the detected Player collider.")]
        [SerializeField, Min(0.1f)] private float referencePlayerWidth = 1.02f;
        [Tooltip("Positive values release earlier; negative values release later.")]
        [SerializeField] private float dropTimingOffset;

        [Header("Vertical Fall")]
        [SerializeField, Min(0.1f)] private float fallSpeed = 14f;
        [SerializeField, Min(0.1f)] private float maximumFallDistance = 12f;
        [SerializeField, Min(0f)] private float collisionSkin = 0.03f;
        [SerializeField, Range(0f, 1f)] private float minimumLandingNormalY = 0.35f;
        [SerializeField] private LayerMask worldMask = (1 << 3) | (1 << 6) | (1 << 13);

        [Header("Debug")]
        [SerializeField] private bool debugGizmos = true;
        [SerializeField] private bool debugLogs = true;

        private readonly RaycastHit2D[] castHits = new RaycastHit2D[CastCapacity];
        private readonly Collider2D[] playerOverlaps = new Collider2D[PlayerOverlapCapacity];

        private Vector2 anchorPosition;
        private Collider2D trackedPlayerCollider;
        private float stateTimer;
        private float travelledDistance;
        private float plannedFallDistance;
        private float lastDropZoneHalfWidth;
        private bool landingPlanValid;
        private bool initialized;
        private bool invalidPlanReported;

        public CaveStalactiteState CurrentState { get; private set; } =
            CaveStalactiteState.Armed;
        public float WarningProgress =>
            CurrentState != CaveStalactiteState.Warning || warningDuration <= 0f
                ? CurrentState == CaveStalactiteState.WaitingForDropZone ? 1f : 0f
                : Mathf.Clamp01(stateTimer / warningDuration);
        public float PlannedFallDistance => plannedFallDistance;
        public float EffectiveDropZoneWidth => lastDropZoneHalfWidth * 2f;
        public bool IsDamageActive =>
            CurrentState == CaveStalactiteState.Falling &&
            fallingDamageRoot != null &&
            fallingDamageRoot.activeSelf;
        public bool IsPlatformActive =>
            embeddedPlatformCollider != null && embeddedPlatformCollider.enabled;

        private void Awake()
        {
            ResolveReferences();
            ConfigurePhysics();

            anchorPosition = body != null ? body.position : (Vector2)transform.position;
            initialized = true;
            SetDamageActive(false);
            SetPlatformActive(false);
            CurrentState = CaveStalactiteState.Armed;
            RefreshLandingPlan();
            NotifyVisualStateChanged();
        }

        private void OnEnable()
        {
            ResolveReferences();
            ConfigurePhysics();

            if (!initialized)
            {
                anchorPosition = body != null ? body.position : (Vector2)transform.position;
                initialized = true;
                CurrentState = CaveStalactiteState.Armed;
                RefreshLandingPlan();
            }

            SetDamageActive(CurrentState == CaveStalactiteState.Falling);
            if (CurrentState == CaveStalactiteState.Embedded)
                TryEnablePlatformWhenClear();
            else
                SetPlatformActive(false);

            NotifyVisualStateChanged();
        }

        private void OnDisable()
        {
            SetDamageActive(false);
            trackedPlayerCollider = null;
        }

        private void FixedUpdate()
        {
            switch (CurrentState)
            {
                case CaveStalactiteState.Armed:
                    UpdateArmed();
                    break;
                case CaveStalactiteState.Warning:
                    UpdateWarning();
                    break;
                case CaveStalactiteState.WaitingForDropZone:
                    UpdateWaitingForDropZone();
                    break;
                case CaveStalactiteState.Falling:
                    UpdateFalling();
                    break;
                case CaveStalactiteState.Embedded:
                    TryEnablePlatformWhenClear();
                    break;
            }
        }

        private void UpdateArmed()
        {
            if (!TryFindPlayerInAnticipationZone(out Collider2D playerCollider))
                return;

            if (!RefreshLandingPlan())
            {
                ReportInvalidPlan("no valid upward-facing surface is available within Maximum Fall Distance");
                return;
            }

            trackedPlayerCollider = playerCollider;
            stateTimer = 0f;
            SetState(CaveStalactiteState.Warning);
        }

        private void UpdateWarning()
        {
            if (!TryFindPlayerInAnticipationZone(out Collider2D playerCollider))
            {
                ReturnToArmed();
                return;
            }

            trackedPlayerCollider = playerCollider;
            stateTimer += Time.fixedDeltaTime;
            if (stateTimer < warningDuration)
                return;

            stateTimer = 0f;
            SetState(CaveStalactiteState.WaitingForDropZone);
        }

        private void UpdateWaitingForDropZone()
        {
            if (!TryFindPlayerInAnticipationZone(out Collider2D playerCollider))
            {
                ReturnToArmed();
                return;
            }

            trackedPlayerCollider = playerCollider;
            lastDropZoneHalfWidth = CalculateDropZoneHalfWidth(playerCollider, plannedFallDistance);

            float playerCenterX = playerCollider.bounds.center.x;
            if (Mathf.Abs(playerCenterX - anchorPosition.x) > lastDropZoneHalfWidth)
                return;

            BeginFalling();
        }

        private void BeginFalling()
        {
            // Revalidate immediately before committing to the one-shot movement.
            if (!RefreshLandingPlan())
            {
                ReportInvalidPlan("the landing route became invalid before release");
                ReturnToArmed();
                return;
            }

            travelledDistance = 0f;
            stateTimer = 0f;
            trackedPlayerCollider = null;
            SetPlatformActive(false);
            SetState(CaveStalactiteState.Falling);
            SetDamageActive(true);
        }

        private void UpdateFalling()
        {
            if (body == null)
            {
                AbortFall("missing Rigidbody2D");
                return;
            }

            float remainingBudget = maximumFallDistance - travelledDistance;
            if (remainingBudget <= 0.0001f)
            {
                AbortFall("Maximum Fall Distance reached without a solid landing surface");
                return;
            }

            float step = Mathf.Min(fallSpeed * Time.fixedDeltaTime, remainingBudget);
            GetFallingBodyGeometry(body.position, out Vector2 castCenter, out Vector2 castSize, out float castAngle);

            if (TryFindClosestBlockingHit(
                    castCenter,
                    castSize,
                    castAngle,
                    step + collisionSkin,
                    out RaycastHit2D hit))
            {
                float allowedDistance = Mathf.Clamp(hit.distance - collisionSkin, 0f, step);
                Vector2 finalPosition = body.position + Vector2.down * allowedDistance;
                body.position = finalPosition;
                travelledDistance += allowedDistance;

                if (hit.normal.y >= minimumLandingNormalY)
                {
                    EmbedAtCurrentPosition();
                    return;
                }

                AbortFall($"blocked by {hit.collider.name} on a non-landable surface");
                return;
            }

            body.MovePosition(body.position + Vector2.down * step);
            travelledDistance += step;

            if (travelledDistance >= maximumFallDistance - 0.0001f)
                AbortFall("Maximum Fall Distance reached without a solid landing surface");
        }

        private void EmbedAtCurrentPosition()
        {
            SetDamageActive(false);
            if (body != null)
            {
                body.velocity = Vector2.zero;
                body.angularVelocity = 0f;
            }

            stateTimer = 0f;
            SetState(CaveStalactiteState.Embedded);
            prototypeVisual?.PlayImpact();
            TryEnablePlatformWhenClear();
        }

        private void TryEnablePlatformWhenClear()
        {
            if (CurrentState != CaveStalactiteState.Embedded ||
                embeddedPlatformCollider == null ||
                embeddedPlatformCollider.enabled)
            {
                return;
            }

            GetBoxWorldGeometry(
                embeddedPlatformCollider,
                body != null ? body.position : (Vector2)transform.position,
                out Vector2 center,
                out Vector2 size,
                out float angle);

            int overlapCount = Physics2D.OverlapBoxNonAlloc(
                center,
                size,
                angle,
                playerOverlaps,
                playerMask);
            for (int i = 0; i < overlapCount; i++)
            {
                if (FindPlayerHealth(playerOverlaps[i]) != null)
                    return;
            }

            SetPlatformActive(true);
        }

        private bool RefreshLandingPlan()
        {
            landingPlanValid = false;
            plannedFallDistance = 0f;

            Vector2 bodyPosition = body != null ? body.position : (Vector2)transform.position;
            GetFallingBodyGeometry(bodyPosition, out Vector2 center, out Vector2 size, out float angle);
            if (!TryFindClosestBlockingHit(
                    center,
                    size,
                    angle,
                    maximumFallDistance + collisionSkin,
                    out RaycastHit2D hit))
            {
                return false;
            }

            if (hit.normal.y < minimumLandingNormalY)
                return false;

            plannedFallDistance = Mathf.Clamp(
                hit.distance - collisionSkin,
                0f,
                maximumFallDistance);
            landingPlanValid = true;
            invalidPlanReported = false;
            lastDropZoneHalfWidth = CalculateDropZoneHalfWidth(null, plannedFallDistance);
            return true;
        }

        private float CalculateDropZoneHalfWidth(Collider2D playerCollider, float fallDistance)
        {
            Vector2 bodyPosition = body != null ? body.position : (Vector2)transform.position;
            GetFallingBodyGeometry(bodyPosition, out Vector2 bodyCenter, out Vector2 bodySize, out _);

            float playerTop;
            float playerWidth;
            if (playerCollider != null && playerCollider.enabled)
            {
                Bounds bounds = playerCollider.bounds;
                playerTop = bounds.max.y;
                playerWidth = bounds.size.x;
            }
            else
            {
                float estimatedFloorY =
                    bodyCenter.y - bodySize.y * 0.5f - fallDistance - collisionSkin;
                playerTop = estimatedFloorY + referencePlayerHeight;
                playerWidth = referencePlayerWidth;
            }

            float bodyBottom = bodyCenter.y - bodySize.y * 0.5f;
            float distanceUntilPlayerHeight = Mathf.Clamp(
                bodyBottom - playerTop,
                0f,
                Mathf.Max(0f, fallDistance));
            float timeUntilContact = distanceUntilPlayerHeight / Mathf.Max(0.1f, fallSpeed);
            float predictedLead =
                referencePlayerHorizontalSpeed * timeUntilContact + dropTimingOffset;

            // For very short drops, the real horizontal extents still provide the
            // minimum region in which a physical contact can occur.
            float contactHalfWidth = (bodySize.x + Mathf.Max(MinimumDimension, playerWidth)) * 0.5f;
            return Mathf.Max(contactHalfWidth, predictedLead, MinimumDimension);
        }

        private bool TryFindPlayerInAnticipationZone(out Collider2D playerCollider)
        {
            playerCollider = null;
            Vector2 center = GetAnticipationZoneCenter(anchorPosition);
            Vector2 size = new Vector2(anticipationWidth, anticipationDepth);
            int count = Physics2D.OverlapBoxNonAlloc(
                center,
                size,
                0f,
                playerOverlaps,
                playerMask);

            float closestHorizontalDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider2D candidate = playerOverlaps[i];
                if (candidate == null || !candidate.enabled || FindPlayerHealth(candidate) == null)
                    continue;

                float horizontalDistance = Mathf.Abs(candidate.bounds.center.x - anchorPosition.x);
                if (horizontalDistance >= closestHorizontalDistance)
                    continue;

                closestHorizontalDistance = horizontalDistance;
                playerCollider = candidate;
            }

            return playerCollider != null;
        }

        private bool TryFindClosestBlockingHit(
            Vector2 origin,
            Vector2 size,
            float angle,
            float distance,
            out RaycastHit2D closestHit)
        {
            closestHit = default;
            if (worldMask.value == 0 || distance <= 0f)
                return false;

            int count = Physics2D.BoxCastNonAlloc(
                origin,
                size,
                angle,
                Vector2.down,
                castHits,
                distance,
                worldMask);

            float closestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit2D candidate = castHits[i];
                Collider2D candidateCollider = candidate.collider;
                if (candidateCollider == null || candidateCollider.isTrigger)
                    continue;
                if (candidateCollider.transform == transform ||
                    candidateCollider.transform.IsChildOf(transform))
                {
                    continue;
                }
                if (candidate.distance >= closestDistance)
                    continue;

                closestDistance = candidate.distance;
                closestHit = candidate;
            }

            if (count == castHits.Length && debugLogs)
            {
                Debug.LogWarning(
                    $"[CAVE STALACTITE] {name}: world cast buffer is full; simplify nearby collision if landing selection is inconsistent.",
                    this);
            }

            return closestHit.collider != null;
        }

        private void ReturnToArmed()
        {
            trackedPlayerCollider = null;
            stateTimer = 0f;
            SetDamageActive(false);
            SetPlatformActive(false);
            SetState(CaveStalactiteState.Armed);
        }

        private void AbortFall(string reason)
        {
            SetDamageActive(false);
            SetPlatformActive(false);
            if (body != null)
            {
                body.velocity = Vector2.zero;
                body.angularVelocity = 0f;
            }

            prototypeVisual?.HideAfterFailedFall();
            if (debugLogs)
                Debug.LogWarning($"[CAVE STALACTITE] {name}: fall aborted safely ({reason}).", this);

            enabled = false;
        }

        private void ReportInvalidPlan(string reason)
        {
            if (!debugLogs || invalidPlanReported)
                return;

            invalidPlanReported = true;
            Debug.LogWarning($"[CAVE STALACTITE] {name}: cannot arm drop because {reason}.", this);
        }

        private void SetState(CaveStalactiteState state)
        {
            if (CurrentState == state)
                return;

            CurrentState = state;
            NotifyVisualStateChanged();
        }

        private void NotifyVisualStateChanged()
        {
            prototypeVisual?.NotifyStateChanged(CurrentState);
        }

        private void SetDamageActive(bool active)
        {
            if (fallingDamageRoot != null && fallingDamageRoot.activeSelf != active)
                fallingDamageRoot.SetActive(active);
        }

        private void SetPlatformActive(bool active)
        {
            if (embeddedPlatformCollider != null)
                embeddedPlatformCollider.enabled = active;
        }

        private Vector2 GetAnticipationZoneCenter(Vector2 rootPosition)
        {
            GetFallingBodyGeometry(rootPosition, out Vector2 bodyCenter, out Vector2 bodySize, out _);
            float bodyBottom = bodyCenter.y - bodySize.y * 0.5f;
            return new Vector2(rootPosition.x, bodyBottom - anticipationDepth * 0.5f);
        }

        private void GetFallingBodyGeometry(
            Vector2 rootPosition,
            out Vector2 center,
            out Vector2 size,
            out float angle)
        {
            if (fallingDamageCollider == null)
            {
                center = rootPosition;
                size = new Vector2(0.9f, 2.4f);
                angle = 0f;
                return;
            }

            GetBoxWorldGeometry(fallingDamageCollider, rootPosition, out center, out size, out angle);
        }

        private void GetBoxWorldGeometry(
            BoxCollider2D box,
            Vector2 rootPosition,
            out Vector2 center,
            out Vector2 size,
            out float angle)
        {
            Transform boxTransform = box.transform;
            Vector2 currentRootPosition = body != null
                ? body.position
                : (Vector2)transform.position;
            Vector2 rootDelta = rootPosition - currentRootPosition;
            center = (Vector2)boxTransform.TransformPoint(box.offset) + rootDelta;
            Vector3 scale = boxTransform.lossyScale;
            size = new Vector2(
                Mathf.Max(MinimumDimension, Mathf.Abs(box.size.x * scale.x)),
                Mathf.Max(MinimumDimension, Mathf.Abs(box.size.y * scale.y)));
            angle = boxTransform.eulerAngles.z;
        }

        private void ResolveReferences()
        {
            if (body == null)
                body = GetComponent<Rigidbody2D>();
            if (fallingDamageRoot == null)
            {
                Transform damageTransform = transform.Find("FallingDamage");
                if (damageTransform != null)
                    fallingDamageRoot = damageTransform.gameObject;
            }
            if (fallingDamageCollider == null && fallingDamageRoot != null)
                fallingDamageCollider = fallingDamageRoot.GetComponent<BoxCollider2D>();
            if (embeddedPlatformCollider == null)
            {
                Transform platformTransform = transform.Find("EmbeddedPlatform");
                if (platformTransform != null)
                    embeddedPlatformCollider = platformTransform.GetComponent<BoxCollider2D>();
            }
            if (prototypeVisual == null)
                prototypeVisual = GetComponent<CaveStalactitePrototypeVisual>();
        }

        private void ConfigurePhysics()
        {
            if (body != null)
            {
                body.bodyType = RigidbodyType2D.Kinematic;
                body.gravityScale = 0f;
                body.freezeRotation = true;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            }

            if (fallingDamageCollider != null)
                fallingDamageCollider.isTrigger = true;
            if (embeddedPlatformCollider != null)
                embeddedPlatformCollider.isTrigger = false;
        }

        private static PlayerHealth FindPlayerHealth(Collider2D candidate)
        {
            return candidate == null
                ? null
                : candidate.GetComponent<PlayerHealth>() ??
                  candidate.GetComponentInParent<PlayerHealth>();
        }

        private void OnValidate()
        {
            anticipationWidth = Mathf.Max(0.1f, anticipationWidth);
            anticipationDepth = Mathf.Max(0.1f, anticipationDepth);
            warningDuration = Mathf.Max(0f, warningDuration);
            referencePlayerHorizontalSpeed = Mathf.Max(0f, referencePlayerHorizontalSpeed);
            referencePlayerHeight = Mathf.Max(0.1f, referencePlayerHeight);
            referencePlayerWidth = Mathf.Max(0.1f, referencePlayerWidth);
            fallSpeed = Mathf.Max(0.1f, fallSpeed);
            maximumFallDistance = Mathf.Max(0.1f, maximumFallDistance);
            collisionSkin = Mathf.Max(0f, collisionSkin);

            ResolveReferences();
            ConfigurePhysics();
        }

        private void OnDrawGizmosSelected()
        {
            if (!debugGizmos)
                return;

            ResolveReferences();
            Vector2 rootPosition = Application.isPlaying && body != null
                ? body.position
                : (Vector2)transform.position;
            Vector2 planningPosition = Application.isPlaying ? anchorPosition : rootPosition;

            Vector2 anticipationCenter = GetAnticipationZoneCenter(planningPosition);
            DrawWireBox(
                anticipationCenter,
                new Vector2(anticipationWidth, anticipationDepth),
                0f,
                new Color(1f, 0.72f, 0.08f, 0.9f));

            float previewFallDistance = Application.isPlaying && landingPlanValid
                ? plannedFallDistance
                : GetPreviewFallDistance(planningPosition);
            float previewDropHalfWidth = CalculateDropZoneHalfWidth(null, previewFallDistance);
            DrawWireBox(
                anticipationCenter,
                new Vector2(previewDropHalfWidth * 2f, anticipationDepth),
                0f,
                new Color(1f, 0.16f, 0.08f, 0.95f));

            GetFallingBodyGeometry(planningPosition, out Vector2 startCenter, out Vector2 bodySize, out float bodyAngle);
            Vector2 landingRoot = planningPosition + Vector2.down * previewFallDistance;
            Vector2 landingCenter = startCenter + Vector2.down * previewFallDistance;

            Color previousColor = Gizmos.color;
            Gizmos.color = new Color(1f, 0.35f, 0.08f, 0.95f);
            Gizmos.DrawLine(startCenter, landingCenter);
            Gizmos.color = previousColor;
            DrawWireBox(startCenter, bodySize, bodyAngle, new Color(0.95f, 0.45f, 0.12f, 0.9f));
            DrawWireBox(landingCenter, bodySize, bodyAngle, new Color(0.4f, 0.9f, 0.35f, 0.9f));

            if (embeddedPlatformCollider != null)
            {
                GetBoxWorldGeometry(
                    embeddedPlatformCollider,
                    landingRoot,
                    out Vector2 platformCenter,
                    out Vector2 platformSize,
                    out float platformAngle);
                DrawWireBox(
                    platformCenter,
                    platformSize,
                    platformAngle,
                    new Color(0.15f, 0.85f, 1f, 0.95f));
            }
        }

        private float GetPreviewFallDistance(Vector2 rootPosition)
        {
            GetFallingBodyGeometry(rootPosition, out Vector2 center, out Vector2 size, out float angle);
            if (!TryFindClosestBlockingHit(
                    center,
                    size,
                    angle,
                    maximumFallDistance + collisionSkin,
                    out RaycastHit2D hit) ||
                hit.normal.y < minimumLandingNormalY)
            {
                return maximumFallDistance;
            }

            return Mathf.Clamp(hit.distance - collisionSkin, 0f, maximumFallDistance);
        }

        private static void DrawWireBox(Vector2 center, Vector2 size, float angle, Color color)
        {
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = Matrix4x4.TRS(center, Quaternion.Euler(0f, 0f, angle), Vector3.one);
            Gizmos.color = color;
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}

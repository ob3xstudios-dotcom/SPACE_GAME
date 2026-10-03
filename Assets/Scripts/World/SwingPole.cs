using Game.Player;
using Game.Input;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Game.World
{
    public enum SwingAttachmentMode
    {
        Rope = 0,
        HandPivot = 1
    }

    [RequireComponent(typeof(Collider2D))]
    public class SwingPole : MonoBehaviour
    {
        [Header("Attachment")]
        [SerializeField] private SwingAttachmentMode attachmentMode = SwingAttachmentMode.Rope;

        [Header("Anchor")]
        [SerializeField] private Transform swingPoint;
        [SerializeField, Min(0.05f)] private float swingDistance = 1.15f;
        [SerializeField] private Vector2 hangOffset = new Vector2(0f, -1.15f);
        [SerializeField] private bool snapOnEnter = true;

        [Header("Rope Climb")]
        [SerializeField] private bool allowRopeClimb = true;
        [SerializeField, Min(0f)] private float ropeClimbSpeed = 1.5f;
        [SerializeField, Min(0.05f)] private float ropeMinDistance = 0.55f;
        [SerializeField, Min(0.05f)] private float ropeMaxDistance = 1.15f;

        [Header("Catch")]
        [SerializeField] private bool requireAirborne = true;
        [SerializeField, Min(0f)] private float catchRadius = 1.15f;
        [SerializeField, Range(0f, 180f)] private float catchHalfAngle = 105f;
        [SerializeField] private float maxUpwardVelocityToCatch = 12f;
        [SerializeField] private LayerMask playerLayer;

        [Header("Refs")]
        [SerializeField] private Rigidbody2D connectedBody;

        [Header("Gizmos")]
        [SerializeField] private bool drawGizmos = true;
        [Tooltip("Draw every SwingPole while playing, even when it is not selected. The Game view Gizmos toggle must be enabled.")]
        [SerializeField] private bool drawGizmosInPlayMode = true;
        [Tooltip("Show the detailed eligibility label and Player-to-anchor diagnostic line.")]
        [SerializeField] private bool drawCatchDebug = false;
        [SerializeField] private Color triggerGizmoColor = new Color(1f, 0.25f, 0.85f, 0.85f);
        [SerializeField] private Color catchGizmoColor = new Color(0.2f, 0.8f, 1f, 0.35f);
        [SerializeField] private Color catchSectorGizmoColor = new Color(0.2f, 1f, 0.4f, 0.25f);
        [SerializeField] private Color anchorGizmoColor = new Color(1f, 0.85f, 0.2f, 1f);

        [Header("Grab Feedback")]
        [Tooltip("Show a green anchor pulse and the G / L1 prompt only while a grab can really succeed.")]
        [SerializeField] private bool showGrabAvailableFeedback = true;
        [SerializeField] private string grabPromptText = "G / L1";
        [SerializeField] private Vector2 grabPromptWorldOffset = new Vector2(0f, 0.35f);
        [SerializeField] private Color grabAvailableColor = new Color(0.25f, 1f, 0.35f, 1f);

        public Vector2 AnchorPosition => swingPoint != null ? swingPoint.position : transform.position;
        public Rigidbody2D ConnectedBody => connectedBody;
        public SwingAttachmentMode AttachmentMode => attachmentMode;
        public bool SnapOnEnter => snapOnEnter;
        public bool AllowRopeClimb => allowRopeClimb;
        public float RopeClimbSpeed => Mathf.Max(0f, ropeClimbSpeed);

        private Collider2D triggerCollider;
        private PlayerController playerInRange;
        private InputReader playerInput;
        private PlayerController coyotePlayer;
        private InputReader coyoteInput;
        private float lastValidGrabTime = float.NegativeInfinity;
        private int coyotePlayerTemporalVersion;
        private GUIStyle grabPromptStyle;

        private void Update()
        {
            RefreshCoyoteOpportunity();

            if (TryConsumeImmediateSwingGrab()) return;
            TryConsumeCoyoteSwingGrab();
        }

        private bool TryConsumeImmediateSwingGrab()
        {
            if (playerInRange == null || playerInput == null) return false;
            if (!playerInRange.CanEnterSwingForFeedback(this)) return false;
            if (!playerInput.HasBufferedSwingGrabPressed()) return false;

            bool enteredSwing = playerInRange.TryEnterSwing(this);
            if (enteredSwing)
                ClearCoyoteOpportunity();

            return true;
        }

        private void TryConsumeCoyoteSwingGrab()
        {
            if (!HasActiveCoyoteOpportunity()) return;
            if (!coyotePlayer.CanEnterSwingFromCoyoteForFeedback(this))
            {
                ClearCoyoteOpportunity();
                return;
            }
            if (!coyoteInput.HasBufferedSwingGrabPressed()) return;

            coyotePlayer.TryEnterSwingFromCoyote(this);
            ClearCoyoteOpportunity();
        }

        private void RefreshCoyoteOpportunity()
        {
            if (playerInRange == null || playerInput == null) return;
            if (!playerInRange.CanEnterSwingForFeedback(this)) return;

            coyotePlayer = playerInRange;
            coyoteInput = playerInput;
            lastValidGrabTime = Time.time;
            coyotePlayerTemporalVersion = playerInRange.SwingGrabTemporalVersion;
        }

        private bool HasActiveCoyoteOpportunity()
        {
            if (coyotePlayer == null || coyoteInput == null) return false;
            if (coyotePlayer.SwingGrabTemporalVersion != coyotePlayerTemporalVersion)
            {
                ClearCoyoteOpportunity();
                return false;
            }

            float coyoteTime = coyotePlayer.SwingGrabCoyoteTime;
            if (coyoteTime <= 0f || Time.time - lastValidGrabTime > coyoteTime)
            {
                ClearCoyoteOpportunity();
                return false;
            }

            return true;
        }

        private void ClearCoyoteOpportunity()
        {
            coyotePlayer = null;
            coyoteInput = null;
            lastValidGrabTime = float.NegativeInfinity;
            coyotePlayerTemporalVersion = 0;
        }

        private bool IsGrabAvailable()
        {
            return playerInRange != null &&
                playerInput != null &&
                playerInRange.CanEnterSwingForFeedback(this);
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || !showGrabAvailableFeedback || !IsGrabAvailable()) return;

            Camera gameCamera = Camera.main;
            if (gameCamera == null) return;

            Vector3 screenPoint = gameCamera.WorldToScreenPoint(AnchorPosition + grabPromptWorldOffset);
            if (screenPoint.z <= 0f) return;

            const float width = 86f;
            const float height = 30f;
            Rect promptRect = new Rect(
                screenPoint.x - width * 0.5f,
                Screen.height - screenPoint.y - height * 0.5f,
                width,
                height);

            if (grabPromptStyle == null)
            {
                grabPromptStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 16,
                    fontStyle = FontStyle.Bold
                };
                grabPromptStyle.normal.textColor = Color.white;
            }

            Color previousBackgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = grabAvailableColor;
            GUI.Box(promptRect, string.IsNullOrWhiteSpace(grabPromptText) ? "G / L1" : grabPromptText, grabPromptStyle);
            GUI.backgroundColor = previousBackgroundColor;
        }

        private void Awake()
        {
            ResolveRefs();
        }

        private void OnDisable()
        {
            ClearCoyoteOpportunity();
        }

        private void Reset()
        {
            ResolveRefs();
        }

        private void OnValidate()
        {
            ropeClimbSpeed = Mathf.Max(0f, ropeClimbSpeed);
            ropeMinDistance = Mathf.Max(0.05f, ropeMinDistance);
            ropeMaxDistance = Mathf.Max(ropeMinDistance, ropeMaxDistance);
            ResolveRefs();
        }

        public bool CanCatch(PlayerController player, Rigidbody2D playerBody)
        {
            if (!CanCatchState(player, playerBody)) return false;

            Vector2 toAnchor = AnchorPosition - (Vector2)player.transform.position;
            float sqrDistance = toAnchor.sqrMagnitude;
            if (sqrDistance > catchRadius * catchRadius) return false;
            if (sqrDistance <= 0.00000001f) return false;

            return Vector2.Angle(Vector2.up, toAnchor) <= catchHalfAngle;
        }

        public bool CanCatchDuringCoyote(PlayerController player, Rigidbody2D playerBody)
        {
            return CanCatchState(player, playerBody);
        }

        private bool CanCatchState(PlayerController player, Rigidbody2D playerBody)
        {
            if (!isActiveAndEnabled || player == null || playerBody == null) return false;
            if (player.IsSwinging || player.IsHanging) return false;
            if (requireAirborne && player.IsGrounded) return false;
            if (playerBody.velocity.y > maxUpwardVelocityToCatch) return false;
            return true;
        }

        public Vector2 GetInitialPlayerPosition()
        {
            return AnchorPosition + hangOffset;
        }

        public Vector2 GetConnectedAnchor()
        {
            Vector2 anchor = AnchorPosition;
            return connectedBody != null
                ? (Vector2)connectedBody.transform.InverseTransformPoint(anchor)
                : anchor;
        }

        public float GetSwingDistance(Vector2 playerPosition)
        {
            if (snapOnEnter)
                return swingDistance;

            return Mathf.Max(0.05f, Vector2.Distance(playerPosition, AnchorPosition));
        }

        public void GetRopeDistanceLimits(
            float initialDistance,
            out float minDistance,
            out float maxDistance)
        {
            float safeInitialDistance = Mathf.Max(0.05f, initialDistance);
            float configuredMin = Mathf.Max(0.05f, ropeMinDistance);
            float configuredMax = Mathf.Max(configuredMin, ropeMaxDistance);

            // Never invalidate a legacy/non-snapping entry whose initial radius falls
            // outside newly configured climb limits.
            minDistance = Mathf.Min(configuredMin, safeInitialDistance);
            maxDistance = Mathf.Max(configuredMax, safeInitialDistance);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            CachePlayer(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (playerInRange == null)
                CachePlayer(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null || player != playerInRange) return;

            playerInRange = null;
            playerInput = null;
        }

        private void CachePlayer(Collider2D other)
        {
            if (!IsPlayerLayer(other.gameObject.layer)) return;

            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null) return;

            playerInRange = player;
            playerInput = player.GetComponentInParent<InputReader>();
        }

        private bool IsPlayerLayer(int layer)
        {
            return playerLayer.value == 0 || (playerLayer.value & (1 << layer)) != 0;
        }

        private void ResolveRefs()
        {
            if (triggerCollider == null)
                triggerCollider = GetComponent<Collider2D>();

            if (triggerCollider != null)
                triggerCollider.isTrigger = true;

            if (connectedBody == null)
                connectedBody = GetComponentInParent<Rigidbody2D>();
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || !drawGizmosInPlayMode) return;
            DrawCatchVisualization();
        }

        private void OnDrawGizmosSelected()
        {
            if (Application.isPlaying && drawGizmosInPlayMode) return;
            DrawCatchVisualization();
        }

        private void DrawCatchVisualization()
        {
            Vector2 anchor = AnchorPosition;
            Collider2D debugTrigger = triggerCollider != null
                ? triggerCollider
                : GetComponent<Collider2D>();

            if (drawGizmos)
            {
                DrawTriggerShape(debugTrigger);
                DrawWorldCircle(anchor, catchRadius, catchGizmoColor, 48);
                DrawCatchSector(anchor);

                Gizmos.color = anchorGizmoColor;
                Gizmos.DrawSphere(anchor, 0.065f);
                Gizmos.DrawWireSphere(anchor, 0.11f);

                if (attachmentMode == SwingAttachmentMode.Rope)
                {
                    Color ropePreviewColor = anchorGizmoColor;
                    ropePreviewColor.a *= 0.45f;
                    Gizmos.color = ropePreviewColor;
                    Gizmos.DrawLine(anchor, anchor + hangOffset);
                    Gizmos.DrawWireSphere(anchor + hangOffset, 0.12f);
                }
            }

            if (showGrabAvailableFeedback && IsGrabAvailable())
            {
                float pulse = Application.isPlaying
                    ? 0.17f + Mathf.Sin(Time.unscaledTime * 7f) * 0.025f
                    : 0.17f;
                Gizmos.color = grabAvailableColor;
                Gizmos.DrawSphere(anchor, 0.075f);
                Gizmos.DrawWireSphere(anchor, pulse);
            }

            if (drawCatchDebug)
                DrawCatchDebug(anchor);
        }

        private void DrawTriggerShape(Collider2D debugTrigger)
        {
            if (debugTrigger == null) return;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            BoxCollider2D box = debugTrigger as BoxCollider2D;

            if (box != null)
            {
                Gizmos.matrix = box.transform.localToWorldMatrix;
                Vector3 boxCenter = new Vector3(box.offset.x, box.offset.y, 0f);
                Vector3 boxSize = new Vector3(box.size.x, box.size.y, 0.02f);

                Color fillColor = triggerGizmoColor;
                fillColor.a *= 0.16f;
                Gizmos.color = fillColor;
                Gizmos.DrawCube(boxCenter, boxSize);

                Gizmos.color = triggerGizmoColor;
                Gizmos.DrawWireCube(boxCenter, boxSize);
            }
            else
            {
                Color fillColor = triggerGizmoColor;
                fillColor.a *= 0.16f;
                Gizmos.color = fillColor;
                Gizmos.DrawCube(debugTrigger.bounds.center, debugTrigger.bounds.size);
                Gizmos.color = triggerGizmoColor;
                Gizmos.DrawWireCube(debugTrigger.bounds.center, debugTrigger.bounds.size);
            }

            Gizmos.matrix = previousMatrix;
        }

        private void DrawCatchSector(Vector2 anchor)
        {
            const int sectorSegments = 32;
            Vector2 firstBoundary = GetCatchSectorDirection(-catchHalfAngle);
            Vector2 previousPoint = anchor + firstBoundary * catchRadius;

            Color fillColor = catchSectorGizmoColor;
            fillColor.a *= 0.5f;
            Gizmos.color = fillColor;
            for (int i = 0; i <= sectorSegments; i += 2)
            {
                float angle = Mathf.Lerp(-catchHalfAngle, catchHalfAngle, i / (float)sectorSegments);
                Gizmos.DrawLine(anchor, anchor + GetCatchSectorDirection(angle) * catchRadius);
            }

            Color outlineColor = catchSectorGizmoColor;
            outlineColor.a = Mathf.Max(0.8f, outlineColor.a);
            Gizmos.color = outlineColor;
            Gizmos.DrawLine(anchor, previousPoint);
            for (int i = 1; i <= sectorSegments; i++)
            {
                float angle = Mathf.Lerp(-catchHalfAngle, catchHalfAngle, i / (float)sectorSegments);
                Vector2 nextPoint = anchor + GetCatchSectorDirection(angle) * catchRadius;
                Gizmos.DrawLine(previousPoint, nextPoint);
                previousPoint = nextPoint;
            }
            Gizmos.DrawLine(anchor, previousPoint);
        }

        private static void DrawWorldCircle(Vector2 center, float radius, Color color, int segments)
        {
            if (radius <= 0f || segments < 3) return;

            Gizmos.color = color;
            Vector2 previousPoint = center + Vector2.right * radius;
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector2 nextPoint = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                Gizmos.DrawLine(previousPoint, nextPoint);
                previousPoint = nextPoint;
            }
        }

        private void DrawCatchDebug(Vector2 anchor)
        {
            Collider2D debugTrigger = triggerCollider != null ? triggerCollider : GetComponent<Collider2D>();
            PlayerController debugPlayer = playerInRange != null
                ? playerInRange
                : FindObjectOfType<PlayerController>();
            if (debugPlayer == null)
            {
                DrawCatchDebugLabel(anchor, "SwingPole: " + name + "\nPlayer: NOT FOUND");
                return;
            }

            Vector2 playerPosition = debugPlayer.transform.position;
            Vector2 toAnchor = anchor - playerPosition;
            float distance = toAnchor.magnitude;
            float angle = toAnchor.sqrMagnitude > 0.00000001f
                ? Vector2.Angle(Vector2.up, toAnchor)
                : 0f;
            Rigidbody2D debugBody = debugPlayer.GetComponent<Rigidbody2D>();
            float verticalVelocity = debugBody != null ? debugBody.velocity.y : 0f;
            bool detectedByTrigger = playerInRange == debugPlayer;
            bool hasSwingInput = detectedByTrigger && playerInput != null;
            bool canEnterSwing = debugBody != null && debugPlayer.CanEnterSwing(this);
            bool canCompleteSwingEntry =
                debugBody != null && debugPlayer.CanEnterSwingForFeedback(this);
            bool canCatch = detectedByTrigger && hasSwingInput && canCompleteSwingEntry;
            string triggerInfo = GetCatchDebugTriggerInfo(debugTrigger);

            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(anchor, 0.08f);
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(playerPosition, 0.08f);
            Gizmos.color = canCatch ? Color.green : Color.red;
            Gizmos.DrawLine(anchor, playerPosition);

            string reason = GetCatchDebugReason(
                debugPlayer,
                debugBody,
                detectedByTrigger,
                hasSwingInput,
                canEnterSwing,
                canCompleteSwingEntry,
                distance,
                angle,
                toAnchor.sqrMagnitude);

            string label =
                $"SwingPole: {name}\n" +
                $"Anchor: {anchor}\n" +
                $"Player point: {playerPosition}\n" +
                $"Player detected by trigger: {(detectedByTrigger ? "YES" : "NO")}\n" +
                $"SwingGrab input found: {(hasSwingInput ? "YES" : "NO")}\n" +
                $"{triggerInfo}\n" +
                $"Distance: {distance:0.00} / {catchRadius:0.00}\n" +
                $"Angle: {angle:0.0} / {catchHalfAngle:0.0}\n" +
                $"Grounded: {(debugPlayer.IsGrounded ? "YES" : "NO")}\n" +
                $"Vertical velocity: {verticalVelocity:0.00} / {maxUpwardVelocityToCatch:0.00}\n" +
                $"CanEnterSwing: {(canEnterSwing ? "YES" : "NO")}\n" +
                $"Complete swing entry: {(canCompleteSwingEntry ? "YES" : "NO")}\n" +
                $"Eligibility: {(canCatch ? "CAN CATCH" : "CANNOT CATCH")}\n" +
                $"Reason: {reason}";

            DrawCatchDebugLabel(anchor, label);
        }

        private string GetCatchDebugReason(
            PlayerController debugPlayer,
            Rigidbody2D debugBody,
            bool detectedByTrigger,
            bool hasSwingInput,
            bool canEnterSwing,
            bool canCompleteSwingEntry,
            float distance,
            float angle,
            float sqrDistance)
        {
            if (!detectedByTrigger) return "PLAYER NOT DETECTED BY TRIGGER";
            if (!hasSwingInput) return "SWING INPUT MISSING";
            if (debugBody == null) return "PLAYER RIGIDBODY MISSING";
            if (debugPlayer.IsSwinging || debugPlayer.IsHanging) return "PLAYER STATE BLOCKED";
            if (requireAirborne && debugPlayer.IsGrounded) return "GROUNDED";
            if (debugBody.velocity.y > maxUpwardVelocityToCatch) return "ASCENDING TOO FAST";
            if (sqrDistance <= 0.00000001f) return "ZERO DISTANCE";
            if (distance > catchRadius) return "OUT OF RADIUS";
            if (angle > catchHalfAngle) return "OUT OF ANGLE";
            if (!canEnterSwing) return "PLAYER CONTROLLER BLOCKED";
            if (!canCompleteSwingEntry) return "STANDING POSTURE BLOCKED";
            return "READY TO CATCH";
        }

        private string GetCatchDebugTriggerInfo(Collider2D debugTrigger)
        {
            if (debugTrigger == null) return "Trigger: MISSING";

            BoxCollider2D box = debugTrigger as BoxCollider2D;
            if (box != null)
                return $"Trigger: Box size {box.size}, offset {box.offset}, world {debugTrigger.bounds.size}";

            return $"Trigger: {debugTrigger.GetType().Name}, world {debugTrigger.bounds.size}";
        }

        private void DrawCatchDebugLabel(Vector2 anchor, string text)
        {
#if UNITY_EDITOR
            Handles.Label(anchor + Vector2.up * (catchRadius + 0.2f), text);
#endif
        }

        private Vector2 GetCatchSectorDirection(float angle)
        {
            return Quaternion.Euler(0f, 0f, angle) * Vector2.down;
        }
    }
}

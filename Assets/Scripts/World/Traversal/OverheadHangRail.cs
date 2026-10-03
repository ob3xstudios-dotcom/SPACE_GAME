using System.Collections.Generic;
using Game.Input;
using Game.Player;
using UnityEngine;

namespace Game.World.Traversal
{
    public enum OverheadRailTransportMode
    {
        Static,
        Driven
    }

    public enum OverheadRailEndBehavior
    {
        Clamp,
        Drop
    }

    public enum RailTraversalInputAxis
    {
        Horizontal,
        Vertical
    }

    /// <summary>
    /// Defines an art-independent segment that can be grabbed with SwingHandAnchor.
    /// The trigger only advertises nearby candidates; PlayerController owns movement.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class OverheadHangRail : MonoBehaviour
    {
        private static readonly HashSet<OverheadHangRail> ActiveRails =
            new HashSet<OverheadHangRail>();

        [Header("Geometry")]
        [SerializeField] private Transform pointA;
        [SerializeField] private Transform pointB;
        [SerializeField, Min(0.05f)] private float maxGrabDistance = 1.25f;

        [Header("Traversal")]
        [SerializeField] private RailTraversalInputAxis traversalInputAxis =
            RailTraversalInputAxis.Horizontal;
        [SerializeField] private OverheadRailTransportMode transportMode =
            OverheadRailTransportMode.Static;
        [SerializeField] private OverheadRailEndBehavior endBehavior =
            OverheadRailEndBehavior.Clamp;
        [SerializeField, Min(0f)] private float playerMoveSpeed = 3f;
        [SerializeField, Min(0f)] private float playerAcceleration = 18f;
        [SerializeField, Min(0f)] private float playerDeceleration = 24f;
        [SerializeField] private float transportSpeed = 2.5f;
        [SerializeField, Min(0f)] private float jumpOffVerticalSpeed = 12f;

        [Header("Grab Trigger")]
        [SerializeField] private BoxCollider2D grabTrigger;
        [SerializeField] private LayerMask playerLayers;

        [Header("Gizmos")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private Color railColor = new Color(0.15f, 0.9f, 1f, 1f);
        [SerializeField] private Color triggerColor = new Color(0.2f, 0.8f, 1f, 0.2f);

        private readonly Dictionary<Collider2D, PlayerController> contactOwners =
            new Dictionary<Collider2D, PlayerController>();
        private readonly Dictionary<PlayerController, int> contactCounts =
            new Dictionary<PlayerController, int>();

        private PlayerController candidatePlayer;
        private InputReader candidateInput;

        public RailTraversalInputAxis TraversalInputAxis => traversalInputAxis;
        public OverheadRailTransportMode TransportMode => transportMode;
        public OverheadRailEndBehavior EndBehavior => endBehavior;
        public float PlayerMoveSpeed => Mathf.Max(0f, playerMoveSpeed);
        public float PlayerAcceleration => Mathf.Max(0f, playerAcceleration);
        public float PlayerDeceleration => Mathf.Max(0f, playerDeceleration);
        public float TransportSpeed => transportMode == OverheadRailTransportMode.Driven
            ? transportSpeed
            : 0f;
        public float JumpOffVerticalSpeed => Mathf.Max(0f, jumpOffVerticalSpeed);
        public Vector2 PointA => pointA != null ? pointA.position : transform.position;
        public Vector2 PointB => pointB != null ? pointB.position : transform.position;
        public float Length => Vector2.Distance(PointA, PointB);
        public bool HasValidSegment => pointA != null && pointB != null && Length > 0.05f;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ActiveRails.Add(this);
        }

        private void OnDisable()
        {
            ActiveRails.Remove(this);
            contactOwners.Clear();
            contactCounts.Clear();
            candidatePlayer = null;
            candidateInput = null;
        }

        private void Reset()
        {
            ResolveReferences();
        }

        private void OnValidate()
        {
            maxGrabDistance = Mathf.Max(0.05f, maxGrabDistance);
            playerMoveSpeed = Mathf.Max(0f, playerMoveSpeed);
            playerAcceleration = Mathf.Max(0f, playerAcceleration);
            playerDeceleration = Mathf.Max(0f, playerDeceleration);
            jumpOffVerticalSpeed = Mathf.Max(0f, jumpOffVerticalSpeed);
            ResolveReferences();
            WarnIfTraversalAxisIsNearlyPerpendicular();
        }

        private void LateUpdate()
        {
            if (!TryGetCandidate(out PlayerController player, out InputReader playerInput))
                return;
            if (!playerInput.HasBufferedSwingGrabPressed())
                return;
            if (!player.CanEnterOverheadRailForFeedback(this))
                return;
            if (!IsClosestEligibleRail(player))
                return;

            player.TryEnterOverheadRail(this);
        }

        public Vector2 GetTangent()
        {
            Vector2 delta = PointB - PointA;
            return delta.sqrMagnitude > 0.0025f ? delta.normalized : Vector2.right;
        }

        public float GetPlayerTraversalInput(Vector2 moveInput)
        {
            float axisInput = traversalInputAxis == RailTraversalInputAxis.Vertical
                ? moveInput.y
                : moveInput.x;
            Vector2 tangent = GetTangent();
            float tangentAxis = traversalInputAxis == RailTraversalInputAxis.Vertical
                ? tangent.y
                : tangent.x;

            // A perpendicular rail has no meaningful positive direction on the selected
            // world axis. Preserve the legacy A -> B control direction in that ambiguous
            // configuration; OnValidate warns the designer so the intended axis can be set.
            if (Mathf.Abs(tangentAxis) <= 0.0001f)
                return axisInput;

            return tangentAxis > 0f ? axisInput : -axisInput;
        }

        public float ProjectPointToRail(Vector2 worldPoint)
        {
            Vector2 a = PointA;
            Vector2 segment = PointB - a;
            float sqrLength = segment.sqrMagnitude;
            if (sqrLength <= 0.0025f)
                return 0f;

            return Mathf.Clamp01(Vector2.Dot(worldPoint - a, segment) / sqrLength);
        }

        public Vector2 GetPositionAtNormalized(float normalizedPosition)
        {
            return Vector2.Lerp(PointA, PointB, Mathf.Clamp01(normalizedPosition));
        }

        public Vector2 GetClosestPoint(Vector2 worldPoint)
        {
            return GetPositionAtNormalized(ProjectPointToRail(worldPoint));
        }

        public bool CanGrabHandAt(Vector2 handPosition)
        {
            if (!isActiveAndEnabled || !HasValidSegment)
                return false;

            float maxDistance = Mathf.Max(0.05f, maxGrabDistance);
            return (GetClosestPoint(handPosition) - handPosition).sqrMagnitude <=
                maxDistance * maxDistance;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            RegisterContact(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            RegisterContact(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (other == null || !contactOwners.TryGetValue(other, out PlayerController player))
                return;

            contactOwners.Remove(other);
            if (player == null || !contactCounts.TryGetValue(player, out int count))
                return;

            count--;
            if (count > 0)
            {
                contactCounts[player] = count;
                return;
            }

            contactCounts.Remove(player);
            if (candidatePlayer == player)
                SelectNextCandidate();
        }

        private void RegisterContact(Collider2D other)
        {
            if (other == null || contactOwners.ContainsKey(other) ||
                !IsPlayerLayer(other.gameObject.layer))
                return;

            PlayerController player = other.GetComponent<PlayerController>() ??
                other.GetComponentInParent<PlayerController>();
            if (player == null)
                return;

            contactOwners.Add(other, player);
            if (contactCounts.TryGetValue(player, out int count))
            {
                contactCounts[player] = count + 1;
                return;
            }

            contactCounts.Add(player, 1);
            if (candidatePlayer == null)
                SetCandidate(player);
        }

        private bool TryGetCandidate(out PlayerController player, out InputReader playerInput)
        {
            if (candidatePlayer == null || !contactCounts.ContainsKey(candidatePlayer))
                SelectNextCandidate();

            player = candidatePlayer;
            playerInput = candidateInput;
            return player != null && playerInput != null;
        }

        private void SelectNextCandidate()
        {
            candidatePlayer = null;
            candidateInput = null;

            foreach (KeyValuePair<PlayerController, int> pair in contactCounts)
            {
                if (pair.Key == null || pair.Value <= 0)
                    continue;

                SetCandidate(pair.Key);
                return;
            }
        }

        private void SetCandidate(PlayerController player)
        {
            candidatePlayer = player;
            candidateInput = player != null ? player.GetComponentInParent<InputReader>() : null;
        }

        private bool HasCandidate(PlayerController player)
        {
            return player != null && contactCounts.TryGetValue(player, out int count) && count > 0;
        }

        private bool IsClosestEligibleRail(PlayerController player)
        {
            Vector2 handPosition = player.SwingHandPosition;
            OverheadHangRail bestRail = null;
            float bestSqrDistance = float.PositiveInfinity;
            int bestInstanceId = int.MaxValue;

            foreach (OverheadHangRail rail in ActiveRails)
            {
                if (rail == null || !rail.HasCandidate(player) ||
                    !player.CanEnterOverheadRailForFeedback(rail))
                    continue;

                float sqrDistance = (rail.GetClosestPoint(handPosition) - handPosition).sqrMagnitude;
                int instanceId = rail.GetInstanceID();
                if (sqrDistance < bestSqrDistance - 0.000001f ||
                    (Mathf.Abs(sqrDistance - bestSqrDistance) <= 0.000001f &&
                     instanceId < bestInstanceId))
                {
                    bestRail = rail;
                    bestSqrDistance = sqrDistance;
                    bestInstanceId = instanceId;
                }
            }

            return bestRail == this;
        }

        private bool IsPlayerLayer(int layer)
        {
            return playerLayers.value == 0 || (playerLayers.value & (1 << layer)) != 0;
        }

        private void ResolveReferences()
        {
            if (pointA == null)
                pointA = transform.Find("PointA");
            if (pointB == null)
                pointB = transform.Find("PointB");
            if (grabTrigger == null)
                grabTrigger = GetComponent<BoxCollider2D>();

            if (grabTrigger != null)
                grabTrigger.isTrigger = true;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void WarnIfTraversalAxisIsNearlyPerpendicular()
        {
            if (pointA == null || pointB == null)
                return;

            Vector2 delta = PointB - PointA;
            if (delta.sqrMagnitude <= 0.0025f)
                return;

            Vector2 tangent = delta.normalized;
            float selectedAxisComponent = traversalInputAxis == RailTraversalInputAxis.Vertical
                ? Mathf.Abs(tangent.y)
                : Mathf.Abs(tangent.x);
            if (selectedAxisComponent >= 0.1f)
                return;

            Debug.LogWarning(
                $"[OVERHEAD RAIL] {name}: Traversal Input Axis '{traversalInputAxis}' " +
                "is nearly perpendicular to PointA -> PointB. Choose the matching axis " +
                "to keep world-space controls unambiguous.",
                this);
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos)
                return;

            ResolveReferences();
            Vector2 a = PointA;
            Vector2 b = PointB;
            Vector2 tangent = GetTangent();

            Gizmos.color = railColor;
            Gizmos.DrawLine(a, b);
            Gizmos.DrawSphere(a, 0.09f);
            Gizmos.DrawWireSphere(a, 0.15f);
            Gizmos.DrawSphere(b, 0.09f);
            Gizmos.DrawWireSphere(b, 0.15f);

            Vector2 midpoint = (a + b) * 0.5f;
            Vector2 normal = new Vector2(-tangent.y, tangent.x);
            float arrowSize = Mathf.Min(0.3f, Mathf.Max(0.12f, Length * 0.08f));
            Vector2 arrowTip = midpoint + tangent * arrowSize;
            Gizmos.DrawLine(midpoint - tangent * arrowSize, arrowTip);
            Gizmos.DrawLine(arrowTip, arrowTip - tangent * arrowSize * 0.65f + normal * arrowSize * 0.45f);
            Gizmos.DrawLine(arrowTip, arrowTip - tangent * arrowSize * 0.65f - normal * arrowSize * 0.45f);

            if (grabTrigger == null)
                return;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = grabTrigger.transform.localToWorldMatrix;
            Gizmos.color = triggerColor;
            Gizmos.DrawCube(grabTrigger.offset, grabTrigger.size);
            Gizmos.color = new Color(triggerColor.r, triggerColor.g, triggerColor.b, 0.9f);
            Gizmos.DrawWireCube(grabTrigger.offset, grabTrigger.size);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}

using System.Collections.Generic;
using Game.Player;
using UnityEngine;
using UnityEngine.Serialization;

namespace Game.World.Traversal
{
    public enum AirCurrentDirection
    {
        Up,
        Left,
        Right
    }

    public enum AirCurrentEndBehavior
    {
        HoldInside,
        PassThrough
    }

    [DisallowMultipleComponent]
    public sealed class UpdraftZone : MonoBehaviour
    {
        [Header("Flow")]
        [SerializeField] private AirCurrentDirection flowDirection = AirCurrentDirection.Up;
        [Tooltip("How quickly velocity builds along the flow direction.")]
        [FormerlySerializedAs("liftAcceleration")]
        [SerializeField, Min(0f)] private float flowAcceleration = 30f;
        [Tooltip("The flow accelerates up to this speed; faster external momentum is preserved.")]
        [FormerlySerializedAs("maxRiseSpeed")]
        [SerializeField, Min(0f)] private float maxFlowSpeed = 14f;
        [SerializeField, Min(0f)] private float strengthMultiplier = 1f;

        [Header("Horizontal Flow")]
        [Tooltip("Fraction of airborne locomotion retained while pressing against the flow. Zero gives the current full authority without deleting existing momentum.")]
        [SerializeField, Range(0f, 1f)] private float againstFlowControl = 0f;
        [Tooltip("Multiplies flow acceleration while horizontal input points with the current.")]
        [SerializeField, Min(0f)] private float withFlowAccelerationMultiplier = 2.5f;
        [Tooltip("Multiplies the speed up to which horizontal flow accelerates while input points with the current.")]
        [SerializeField, Min(1f)] private float withFlowMaxSpeedMultiplier = 1.75f;

        [Header("End")]
        [SerializeField] private AirCurrentEndBehavior endBehavior =
            AirCurrentEndBehavior.PassThrough;
        [FormerlySerializedAs("topHoverInset")]
        [SerializeField, Min(0f)] private float endHoldInset = 0.35f;
        [SerializeField, Min(0.01f)] private float hoverTolerance = 0.08f;
        [SerializeField, Min(0.01f)] private float hoverCorrectionSpeed = 2f;
        [SerializeField, Min(0.01f)] private float hoverStrength = 30f;

        [Header("Scene View")]
        [SerializeField] private bool drawGizmo = true;
        [SerializeField] private Color gizmoColor = new Color(0.15f, 0.8f, 1f, 0.22f);

        [Header("Diagnostics")]
#if UNITY_EDITOR
        [SerializeField] private bool diagnosticLogs = true;
#else
        [SerializeField] private bool diagnosticLogs;
#endif

        private Collider2D zoneCollider;
        private readonly Dictionary<Collider2D, PlayerUpdraftCape> registeredContacts =
            new Dictionary<Collider2D, PlayerUpdraftCape>();
        private readonly HashSet<Collider2D> unresolvedPlayerContacts =
            new HashSet<Collider2D>();

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

        public Vector2 FlowDirection
        {
            get
            {
                switch (flowDirection)
                {
                    case AirCurrentDirection.Left:
                        return Vector2.left;
                    case AirCurrentDirection.Right:
                        return Vector2.right;
                    default:
                        return Vector2.up;
                }
            }
        }

        public AirCurrentDirection Direction => flowDirection;
        public AirCurrentEndBehavior EndBehavior => endBehavior;
        public float FlowAcceleration => Mathf.Max(0f, flowAcceleration);
        public float MaxFlowSpeed => Mathf.Max(0f, maxFlowSpeed);
        public float StrengthMultiplier => Mathf.Max(0f, strengthMultiplier);
        public float EffectiveFlowAcceleration => FlowAcceleration * StrengthMultiplier;
        public float AgainstFlowControl => Mathf.Clamp01(againstFlowControl);
        public float WithFlowAccelerationMultiplier =>
            Mathf.Max(0f, withFlowAccelerationMultiplier);
        public float WithFlowMaxSpeedMultiplier =>
            Mathf.Max(1f, withFlowMaxSpeedMultiplier);
        public float EndHoldInset => Mathf.Max(0f, endHoldInset);
        public float HoverTolerance => Mathf.Max(0.01f, hoverTolerance);
        public float HoverCorrectionSpeed => Mathf.Max(0.01f, hoverCorrectionSpeed);
        public float HoverStrength => Mathf.Max(0.01f, hoverStrength);

        private void Awake()
        {
            zoneCollider = GetComponent<Collider2D>();
            if (zoneCollider == null)
            {
                Debug.LogError("[UPDRAFT] A Collider2D is required.", this);
                enabled = false;
                return;
            }

            zoneCollider.isTrigger = true;
        }

        private void OnDisable()
        {
            foreach (KeyValuePair<Collider2D, PlayerUpdraftCape> contact in registeredContacts)
            {
                if (contact.Value != null)
                    contact.Value.UnregisterZoneContact(this, contact.Key);
            }

            registeredContacts.Clear();
            unresolvedPlayerContacts.Clear();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            RegisterContact(other, "trigger-enter");
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            RegisterContact(other, "trigger-stay-recovery");
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            unresolvedPlayerContacts.Remove(other);
            if (!registeredContacts.TryGetValue(other, out PlayerUpdraftCape cape))
                return;

            registeredContacts.Remove(other);
            if (cape != null)
                cape.UnregisterZoneContact(this, other);
        }

        private void RegisterContact(Collider2D other, string reason)
        {
            if (other == null)
                return;

            PlayerUpdraftCape cape = other.GetComponentInParent<PlayerUpdraftCape>();
            if (cape == null || !cape.isActiveAndEnabled)
            {
                if (registeredContacts.TryGetValue(other, out PlayerUpdraftCape previousCape))
                {
                    registeredContacts.Remove(other);
                    if (previousCape != null)
                        previousCape.UnregisterZoneContact(this, other);
                }

                if (IsPlayerCollider(other) && unresolvedPlayerContacts.Add(other) &&
                    DiagnosticsEnabled)
                {
                    Debug.Log(
                        $"[CAPE][ZONE ENTER] zone={name} " +
                        $"collider={other.name}:{other.GetType().Name} " +
                        "registered=False reason=player-cape-not-ready",
                        this);
                }

                return;
            }

            unresolvedPlayerContacts.Remove(other);
            if (registeredContacts.TryGetValue(other, out PlayerUpdraftCape registeredCape))
            {
                if (registeredCape == cape)
                {
                    // Idempotent repair path for a cape component that was disabled or
                    // rebound while the physical overlap remained active.
                    cape.RegisterZoneContact(this, other, reason);
                    return;
                }

                if (registeredCape != null)
                    registeredCape.UnregisterZoneContact(this, other);
            }

            registeredContacts[other] = cape;
            cape.RegisterZoneContact(this, other, reason);
        }

        private static bool IsPlayerCollider(Collider2D other)
        {
            Transform otherTransform = other.transform;
            return otherTransform.CompareTag("Player") ||
                otherTransform.root.CompareTag("Player");
        }

        public float GetProjectedFlowSpeed(Vector2 velocity, float fixedDeltaTime)
        {
            return GetProjectedFlowSpeed(
                Vector2.Dot(velocity, FlowDirection),
                fixedDeltaTime);
        }

        public float GetProjectedFlowSpeed(float currentFlowSpeed, float fixedDeltaTime)
        {
            return GetProjectedFlowSpeed(currentFlowSpeed, fixedDeltaTime, 1f);
        }

        public float GetProjectedFlowSpeed(
            float currentFlowSpeed,
            float fixedDeltaTime,
            float accelerationMultiplier)
        {
            return GetProjectedFlowSpeed(
                currentFlowSpeed,
                fixedDeltaTime,
                accelerationMultiplier,
                1f);
        }

        public float GetProjectedFlowSpeed(
            float currentFlowSpeed,
            float fixedDeltaTime,
            float accelerationMultiplier,
            float maxSpeedMultiplier)
        {
            // This is an acceleration field with a finite contribution cap. The cap only
            // stops further acceleration; it never removes faster external momentum.
            float flowSpeedLimit = MaxFlowSpeed * Mathf.Max(0f, maxSpeedMultiplier);
            if (currentFlowSpeed >= flowSpeedLimit)
                return currentFlowSpeed;

            return Mathf.MoveTowards(
                currentFlowSpeed,
                flowSpeedLimit,
                EffectiveFlowAcceleration *
                Mathf.Max(0f, accelerationMultiplier) *
                Mathf.Max(0f, fixedDeltaTime));
        }

        public bool TryGetEndHoldCoordinates(
            out float holdCoordinate,
            out float boundaryCoordinate)
        {
            Collider2D currentCollider = zoneCollider != null
                ? zoneCollider
                : GetComponent<Collider2D>();
            if (currentCollider == null)
            {
                boundaryCoordinate = Vector2.Dot(transform.position, FlowDirection);
                holdCoordinate = boundaryCoordinate;
                return false;
            }

            Bounds bounds = currentCollider.bounds;
            Vector2 direction = FlowDirection;
            float flowExtent = direction.x == 0f ? bounds.size.y : bounds.size.x;
            float inset = Mathf.Min(EndHoldInset, Mathf.Max(0f, flowExtent));
            boundaryCoordinate = GetLeadingEdgeCoordinate(bounds, direction);
            holdCoordinate = boundaryCoordinate - inset;
            return true;
        }

        public static float GetLeadingEdgeCoordinate(Bounds bounds, Vector2 direction)
        {
            if (direction.x > 0f)
                return bounds.max.x;
            if (direction.x < 0f)
                return -bounds.min.x;

            return bounds.max.y;
        }

#if UNITY_EDITOR
        private void Reset()
        {
            zoneCollider = GetComponent<Collider2D>();
            if (zoneCollider != null)
                zoneCollider.isTrigger = true;
        }

        private void OnValidate()
        {
            flowAcceleration = Mathf.Max(0f, flowAcceleration);
            maxFlowSpeed = Mathf.Max(0f, maxFlowSpeed);
            strengthMultiplier = Mathf.Max(0f, strengthMultiplier);
            againstFlowControl = Mathf.Clamp01(againstFlowControl);
            withFlowAccelerationMultiplier = Mathf.Max(0f, withFlowAccelerationMultiplier);
            withFlowMaxSpeedMultiplier = Mathf.Max(1f, withFlowMaxSpeedMultiplier);
            endHoldInset = Mathf.Max(0f, endHoldInset);
            hoverTolerance = Mathf.Max(0.01f, hoverTolerance);
            hoverCorrectionSpeed = Mathf.Max(0.01f, hoverCorrectionSpeed);
            hoverStrength = Mathf.Max(0.01f, hoverStrength);

            Collider2D currentCollider = GetComponent<Collider2D>();
            if (currentCollider != null)
                currentCollider.isTrigger = true;
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmo)
                return;

            Collider2D currentCollider = GetComponent<Collider2D>();
            Color fill = gizmoColor;
            Color outline = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.9f);

            if (currentCollider is BoxCollider2D box)
            {
                Matrix4x4 previousMatrix = Gizmos.matrix;
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.color = fill;
                Gizmos.DrawCube(box.offset, box.size);
                Gizmos.color = outline;
                Gizmos.DrawWireCube(box.offset, box.size);
                Gizmos.matrix = previousMatrix;
                DrawWorldArrows(currentCollider.bounds);
                DrawEndHoldLine(currentCollider);
                return;
            }

            if (currentCollider != null)
            {
                Gizmos.color = fill;
                Gizmos.DrawCube(currentCollider.bounds.center, currentCollider.bounds.size);
                Gizmos.color = outline;
                Gizmos.DrawWireCube(currentCollider.bounds.center, currentCollider.bounds.size);
                DrawWorldArrows(currentCollider.bounds);
                DrawEndHoldLine(currentCollider);
            }
        }

        private void DrawEndHoldLine(Collider2D currentCollider)
        {
            if (endBehavior != AirCurrentEndBehavior.HoldInside ||
                currentCollider == null ||
                !TryGetEndHoldCoordinates(out float holdCoordinate, out _))
            {
                return;
            }

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
            Bounds bounds = currentCollider.bounds;
            Vector2 direction = FlowDirection;
            if (direction.x == 0f)
            {
                Gizmos.DrawLine(
                    new Vector3(bounds.min.x, holdCoordinate, transform.position.z),
                    new Vector3(bounds.max.x, holdCoordinate, transform.position.z));
            }
            else
            {
                float holdX = direction.x > 0f ? holdCoordinate : -holdCoordinate;
                Gizmos.DrawLine(
                    new Vector3(holdX, bounds.min.y, transform.position.z),
                    new Vector3(holdX, bounds.max.y, transform.position.z));
            }
            Gizmos.matrix = previousMatrix;
        }

        private void DrawWorldArrows(Bounds bounds)
        {
            Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
            Vector2 direction = FlowDirection;
            Vector2 perpendicular = direction.x == 0f ? Vector2.right : Vector2.up;
            float flowSize = direction.x == 0f ? bounds.size.y : bounds.size.x;
            float perpendicularSize = direction.x == 0f ? bounds.size.x : bounds.size.y;
            Vector2 center = bounds.center;
            float halfArrowLength = flowSize * 0.32f;
            float headSize = Mathf.Min(bounds.size.x, bounds.size.y) * 0.08f;

            for (int i = -1; i <= 1; i++)
            {
                Vector2 laneOffset = perpendicular * (perpendicularSize * 0.25f * i);
                Vector2 arrowStart = center + laneOffset - direction * halfArrowLength;
                Vector2 arrowEnd = center + laneOffset + direction * halfArrowLength;
                Vector2 headBase = arrowEnd - direction * headSize;
                Vector2 headA = headBase + perpendicular * headSize;
                Vector2 headB = headBase - perpendicular * headSize;
                Gizmos.DrawLine(arrowStart, arrowEnd);
                Gizmos.DrawLine(arrowEnd, headA);
                Gizmos.DrawLine(arrowEnd, headB);
            }
        }
#endif
    }
}

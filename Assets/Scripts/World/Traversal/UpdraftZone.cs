using System.Collections.Generic;
using Game.Player;
using UnityEngine;

namespace Game.World.Traversal
{
    [DisallowMultipleComponent]
    public sealed class UpdraftZone : MonoBehaviour
    {
        [Header("Lift")]
        [SerializeField, Min(0f)] private float liftAcceleration = 18f;
        [SerializeField, Min(0f)] private float maxRiseSpeed = 7f;
        [SerializeField, Min(0f)] private float strengthMultiplier = 1f;

        [Header("Top Hover")]
        [SerializeField, Min(0f)] private float topHoverInset = 0.35f;
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

        public float LiftAcceleration => Mathf.Max(0f, liftAcceleration);
        public float MaxRiseSpeed => Mathf.Max(0f, maxRiseSpeed);
        public float StrengthMultiplier => Mathf.Max(0f, strengthMultiplier);
        public float EffectiveLiftAcceleration => LiftAcceleration * StrengthMultiplier;
        public float TopHoverInset => Mathf.Max(0f, topHoverInset);
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

        public float GetProjectedVerticalSpeed(float currentVerticalSpeed, float fixedDeltaTime)
        {
            if (currentVerticalSpeed >= MaxRiseSpeed)
                return currentVerticalSpeed;

            return Mathf.MoveTowards(
                currentVerticalSpeed,
                MaxRiseSpeed,
                EffectiveLiftAcceleration * Mathf.Max(0f, fixedDeltaTime));
        }

        public bool TryGetTopHoverY(out float hoverY)
        {
            return TryGetTopHoverY(out hoverY, out _);
        }

        public bool TryGetTopHoverY(out float hoverY, out float topBoundaryY)
        {
            Collider2D currentCollider = zoneCollider != null
                ? zoneCollider
                : GetComponent<Collider2D>();
            if (currentCollider == null)
            {
                hoverY = transform.position.y;
                topBoundaryY = hoverY;
                return false;
            }

            Bounds bounds = currentCollider.bounds;
            float inset = Mathf.Min(TopHoverInset, Mathf.Max(0f, bounds.size.y));
            topBoundaryY = bounds.max.y;
            hoverY = topBoundaryY - inset;
            return true;
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
            liftAcceleration = Mathf.Max(0f, liftAcceleration);
            maxRiseSpeed = Mathf.Max(0f, maxRiseSpeed);
            strengthMultiplier = Mathf.Max(0f, strengthMultiplier);
            topHoverInset = Mathf.Max(0f, topHoverInset);
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
                DrawLocalArrows(box.offset, box.size);
                Gizmos.matrix = previousMatrix;
                DrawHoverLine(currentCollider);
                return;
            }

            if (currentCollider != null)
            {
                Gizmos.color = fill;
                Gizmos.DrawCube(currentCollider.bounds.center, currentCollider.bounds.size);
                Gizmos.color = outline;
                Gizmos.DrawWireCube(currentCollider.bounds.center, currentCollider.bounds.size);
                DrawHoverLine(currentCollider);
            }
        }

        private void DrawHoverLine(Collider2D currentCollider)
        {
            if (currentCollider == null || !TryGetTopHoverY(out float hoverY))
                return;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
            Bounds bounds = currentCollider.bounds;
            Gizmos.DrawLine(
                new Vector3(bounds.min.x, hoverY, transform.position.z),
                new Vector3(bounds.max.x, hoverY, transform.position.z));
            Gizmos.matrix = previousMatrix;
        }

        private void DrawLocalArrows(Vector2 center, Vector2 size)
        {
            Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
            float bottom = center.y - size.y * 0.32f;
            float top = center.y + size.y * 0.32f;
            float headSize = Mathf.Min(size.x, size.y) * 0.08f;

            for (int i = -1; i <= 1; i++)
            {
                float x = center.x + size.x * 0.25f * i;
                Vector3 arrowTop = new Vector3(x, top, 0f);
                Gizmos.DrawLine(new Vector3(x, bottom, 0f), arrowTop);
                Gizmos.DrawLine(arrowTop, new Vector3(x - headSize, top - headSize, 0f));
                Gizmos.DrawLine(arrowTop, new Vector3(x + headSize, top - headSize, 0f));
            }
        }
#endif
    }
}

using System.Collections.Generic;
using UnityEngine;
using Game.World;

namespace Game.World.Factory.WoodProcessing
{
    /// <summary>
    /// Composes a ConveyorBelt surface velocity into a generic dynamic body only
    /// while a valid top-face contact supports it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    public sealed class ConveyorRider2D : MonoBehaviour
    {
        private struct SupportRecord
        {
            public ConveyorBelt Belt;
            public int SeenStep;

            public SupportRecord(ConveyorBelt belt, int seenStep)
            {
                Belt = belt;
                SeenStep = seenStep;
            }
        }

        [Header("Support detection")]
        [SerializeField, Range(0f, 1f)] private float minimumUpNormal = 0.55f;
        [SerializeField, Min(0f)] private float topContactTolerance = 0.15f;

        private Rigidbody2D body;
        private Collider2D riderCollider;
        private readonly Dictionary<Collider2D, SupportRecord> supports =
            new Dictionary<Collider2D, SupportRecord>();
        private readonly List<Collider2D> staleSupports = new List<Collider2D>(2);
        private ConveyorBelt supportedBelt;
        private int fixedStep;
        private bool transportEnabled = true;

        public bool TransportEnabled => transportEnabled;
        public ConveyorBelt SupportedBelt => supportedBelt;
        public bool HasTopSupport => supportedBelt != null;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            riderCollider = GetComponent<Collider2D>();
        }

        private void FixedUpdate()
        {
            fixedStep++;
            SelectSupportedBelt();

            if (transportEnabled && supportedBelt != null && supportedBelt.isActiveAndEnabled)
                ApplySurfaceVelocity(supportedBelt.SurfaceVelocity.x);
        }

        private void OnDisable()
        {
            ClearSupports();
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            EvaluateSupport(collision);
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            EvaluateSupport(collision);
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            if (collision != null && collision.collider != null)
                supports.Remove(collision.collider);
        }

        public void SetTransportEnabled(bool enabled)
        {
            if (transportEnabled == enabled)
                return;

            transportEnabled = enabled;
        }

        /// <summary>
        /// Clears all cached contacts before an external teleport and restores the
        /// default ability to receive conveyor transport.
        /// </summary>
        public void ResetForTeleport()
        {
            ClearSupports();
            transportEnabled = true;
        }

        private void EvaluateSupport(Collision2D collision)
        {
            if (!transportEnabled || collision == null || collision.collider == null)
                return;

            ConveyorBelt belt;
            if (supports.TryGetValue(collision.collider, out SupportRecord cachedSupport) &&
                cachedSupport.Belt != null)
            {
                belt = cachedSupport.Belt;
            }
            else
            {
                belt = collision.collider.GetComponentInParent<ConveyorBelt>();
            }

            if (belt == null || collision.collider.isTrigger || riderCollider == null)
                return;

            Bounds beltBounds = collision.collider.bounds;
            Bounds riderBounds = riderCollider.bounds;

            if (riderBounds.min.y < beltBounds.center.y)
                return;

            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint2D contact = collision.GetContact(i);
                if (contact.normal.y < minimumUpNormal)
                    continue;

                if (contact.point.y < beltBounds.max.y - topContactTolerance)
                    continue;

                supports[collision.collider] = new SupportRecord(belt, fixedStep);
                return;
            }
        }

        private void SelectSupportedBelt()
        {
            supportedBelt = null;
            int newestStep = -1;
            staleSupports.Clear();

            foreach (KeyValuePair<Collider2D, SupportRecord> pair in supports)
            {
                Collider2D supportCollider = pair.Key;
                SupportRecord record = pair.Value;
                bool stale = supportCollider == null || record.Belt == null ||
                             fixedStep - record.SeenStep > 1;

                if (stale)
                {
                    staleSupports.Add(supportCollider);
                    continue;
                }

                if (record.SeenStep > newestStep)
                {
                    newestStep = record.SeenStep;
                    supportedBelt = record.Belt;
                }
            }

            for (int i = 0; i < staleSupports.Count; i++)
                supports.Remove(staleSupports[i]);
        }

        private void ApplySurfaceVelocity(float surfaceVelocityX)
        {
            if (body == null || !body.simulated || body.bodyType != RigidbodyType2D.Dynamic)
                return;

            // The conveyor owns the supported body's horizontal transport velocity.
            // Assigning it prevents per-step accumulation while contact forces still
            // resolve normally during the following physics simulation.
            body.velocity = new Vector2(surfaceVelocityX, body.velocity.y);
        }

        private void ClearSupports()
        {
            supports.Clear();
            staleSupports.Clear();
            supportedBelt = null;
        }
    }
}

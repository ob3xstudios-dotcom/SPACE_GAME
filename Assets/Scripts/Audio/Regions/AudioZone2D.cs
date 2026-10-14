using UnityEngine;

namespace Game.Audio
{
    [DisallowMultipleComponent]
    public sealed class AudioZone2D : MonoBehaviour
    {
        [SerializeField] private AudioRegionProfile profile;
        [Tooltip("Higher-priority overlapping zones take precedence. Equal priorities prefer the smaller zone.")]
        [SerializeField] private int priority;
        [SerializeField] private Color gizmoColor = new Color(0.2f, 0.65f, 1f, 0.2f);

        private Collider2D[] zoneColliders;

        public AudioRegionProfile Profile => profile;
        public int Priority => priority;

        private void Awake()
        {
            CacheColliders();
        }

        private void OnEnable()
        {
            CacheColliders();
            if (Application.isPlaying)
                MusicDirector.RegisterZone(this);
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
                MusicDirector.UnregisterZone(this);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.GetComponentInParent<AudioRegionListener2D>() != null)
                MusicDirector.RequestRegionRefresh();
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (other.GetComponentInParent<AudioRegionListener2D>() != null)
                MusicDirector.RequestRegionRefresh();
        }

        public bool Contains(Vector2 worldPoint)
        {
            if (!isActiveAndEnabled || profile == null)
                return false;

            EnsureColliderCache();
            for (int i = 0; i < zoneColliders.Length; i++)
            {
                Collider2D zoneCollider = zoneColliders[i];
                if (zoneCollider != null &&
                    zoneCollider.enabled &&
                    zoneCollider.OverlapPoint(worldPoint))
                {
                    return true;
                }
            }

            return false;
        }

        public float GetWorldBoundsArea()
        {
            EnsureColliderCache();
            float area = 0f;
            bool foundCollider = false;
            for (int i = 0; i < zoneColliders.Length; i++)
            {
                Collider2D zoneCollider = zoneColliders[i];
                if (zoneCollider == null || !zoneCollider.enabled)
                    continue;

                Bounds bounds = zoneCollider.bounds;
                area += Mathf.Abs(bounds.size.x * bounds.size.y);
                foundCollider = true;
            }

            return foundCollider ? area : float.PositiveInfinity;
        }

        private void OnTransformChildrenChanged()
        {
            CacheColliders();
            if (Application.isPlaying)
                MusicDirector.RequestRegionRefresh();
        }

        private void Reset()
        {
            CacheColliders();
            EnsureTriggerColliders();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            CacheColliders();
            EnsureTriggerColliders();
            if (Application.isPlaying)
                MusicDirector.RequestRegionRefresh();
        }

        private void OnDrawGizmosSelected()
        {
            CacheColliders();
            for (int i = 0; i < zoneColliders.Length; i++)
            {
                Collider2D zoneCollider = zoneColliders[i];
                if (zoneCollider == null)
                    continue;

                Bounds bounds = zoneCollider.bounds;
                Gizmos.color = gizmoColor;
                Gizmos.DrawCube(bounds.center, bounds.size);
                Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.9f);
                Gizmos.DrawWireCube(bounds.center, bounds.size);
            }
        }
#endif

        private void EnsureColliderCache()
        {
            if (zoneColliders == null)
                CacheColliders();
        }

        private void CacheColliders()
        {
            zoneColliders = GetComponents<Collider2D>();
        }

        private void EnsureTriggerColliders()
        {
            if (zoneColliders == null)
                return;

            for (int i = 0; i < zoneColliders.Length; i++)
            {
                if (zoneColliders[i] != null)
                    zoneColliders[i].isTrigger = true;
            }
        }
    }
}

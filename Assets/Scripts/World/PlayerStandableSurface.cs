using UnityEngine;

namespace Game.World
{
    [DisallowMultipleComponent]
    public sealed class PlayerStandableSurface : MonoBehaviour
    {
        [SerializeField] private Collider2D surfaceCollider;

        public Vector2 LastDelta { get; private set; }
        public Collider2D SurfaceCollider => surfaceCollider;

        private void Awake()
        {
            if (surfaceCollider == null)
                surfaceCollider = GetComponent<Collider2D>();
        }

        public bool Accepts(Collider2D candidate)
        {
            return candidate != null &&
                   !candidate.isTrigger &&
                   candidate == surfaceCollider;
        }

        public void SetMovementDelta(Vector2 delta)
        {
            LastDelta = delta;
        }

        private void OnDisable()
        {
            LastDelta = Vector2.zero;
        }

        private void Reset()
        {
            surfaceCollider = GetComponent<Collider2D>();
        }
    }
}

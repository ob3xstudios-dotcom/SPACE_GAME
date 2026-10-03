using UnityEngine;

namespace Game.World
{
    public enum ConveyorDirection
    {
        Left,
        Right
    }

    /// <summary>
    /// Static physical surface that exposes a horizontal velocity to supported characters.
    /// Contact detection and velocity composition are owned by PlayerController.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class ConveyorBelt : MonoBehaviour
    {
        [Header("Surface")]
        [SerializeField] private ConveyorDirection direction = ConveyorDirection.Right;
        [SerializeField, Min(0f)] private float surfaceSpeed = 3f;

        [Header("Dimensions")]
        [SerializeField, Min(0.1f)] private float beltLength = 4f;
        [SerializeField, Min(0.1f)] private float beltHeight = 0.75f;

        [Header("Parts")]
        [SerializeField] private BoxCollider2D surfaceCollider;
        [SerializeField] private ConveyorBeltVisual visual;

        public Vector2 SurfaceVelocity => enabled
            ? Vector2.right * SignedSurfaceSpeed
            : Vector2.zero;

        public float SignedSurfaceSpeed =>
            (direction == ConveyorDirection.Right ? 1f : -1f) * Mathf.Max(0f, surfaceSpeed);

        private void Awake()
        {
            ResolveReferences();
            ApplyConfiguration();
        }

        private void OnValidate()
        {
            beltLength = Mathf.Max(0.1f, beltLength);
            beltHeight = Mathf.Max(0.1f, beltHeight);
            surfaceSpeed = Mathf.Max(0f, surfaceSpeed);

            ResolveReferences();
            ApplyConfiguration();
        }

        private void ResolveReferences()
        {
            if (surfaceCollider == null)
                surfaceCollider = GetComponent<BoxCollider2D>();

            if (visual == null)
                visual = GetComponentInChildren<ConveyorBeltVisual>(true);
        }

        private void ApplyConfiguration()
        {
            Rigidbody2D body = GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.bodyType = RigidbodyType2D.Static;
                body.gravityScale = 0f;
                body.simulated = true;
            }

            if (surfaceCollider != null)
            {
                surfaceCollider.isTrigger = false;
                surfaceCollider.offset = Vector2.zero;
                surfaceCollider.size = new Vector2(beltLength, beltHeight);
            }

            if (visual != null)
                visual.Configure(beltLength, beltHeight, SignedSurfaceSpeed);
        }

        private void OnDrawGizmosSelected()
        {
            float length = Mathf.Max(0.1f, beltLength);
            float height = Mathf.Max(0.1f, beltHeight);
            int sign = direction == ConveyorDirection.Right ? 1 : -1;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.1f, 0.85f, 1f, 0.9f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(length, height, 0.02f));

            float arrowLength = Mathf.Min(0.75f, length * 0.25f);
            float arrowHead = Mathf.Min(0.22f, height * 0.3f);
            Vector3 arrowStart = new Vector3(-sign * arrowLength * 0.5f, height * 0.65f, 0f);
            Vector3 arrowEnd = new Vector3(sign * arrowLength * 0.5f, height * 0.65f, 0f);
            Gizmos.DrawLine(arrowStart, arrowEnd);
            Gizmos.DrawLine(arrowEnd, arrowEnd + new Vector3(-sign * arrowHead, arrowHead, 0f));
            Gizmos.DrawLine(arrowEnd, arrowEnd + new Vector3(-sign * arrowHead, -arrowHead, 0f));

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}

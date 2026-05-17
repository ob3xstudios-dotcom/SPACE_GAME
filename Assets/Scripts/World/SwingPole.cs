using Game.Player;
using Game.Input;
using UnityEngine;

namespace Game.World
{
    [RequireComponent(typeof(Collider2D))]
    public class SwingPole : MonoBehaviour
    {
        [Header("Anchor")]
        [SerializeField] private Transform swingPoint;
        [SerializeField, Min(0.05f)] private float swingDistance = 1.15f;
        [SerializeField] private Vector2 hangOffset = new Vector2(0f, -1.15f);
        [SerializeField] private bool snapOnEnter = true;

        [Header("Catch")]
        [SerializeField] private bool requireAirborne = true;
        [SerializeField, Min(0f)] private float catchRadius = 1.15f;
        [SerializeField] private float maxUpwardVelocityToCatch = 12f;
        [SerializeField] private LayerMask playerLayer;

        [Header("Refs")]
        [SerializeField] private Rigidbody2D connectedBody;

        [Header("Gizmos")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private Color catchGizmoColor = new Color(0.2f, 0.8f, 1f, 0.35f);
        [SerializeField] private Color anchorGizmoColor = new Color(1f, 0.85f, 0.2f, 1f);

        public Vector2 AnchorPosition => swingPoint != null ? swingPoint.position : transform.position;
        public Rigidbody2D ConnectedBody => connectedBody;
        public bool SnapOnEnter => snapOnEnter;

        private Collider2D triggerCollider;
        private PlayerController playerInRange;
        private InputReader playerInput;

        private void Update()
        {
            if (playerInRange == null || playerInput == null) return;
            if (!playerInRange.CanEnterSwing(this)) return;
            if (!playerInput.ConsumeSwingGrabPressed()) return;

            playerInRange.TryEnterSwing(this);
        }

        private void Awake()
        {
            ResolveRefs();
        }

        private void Reset()
        {
            ResolveRefs();
        }

        private void OnValidate()
        {
            ResolveRefs();
        }

        public bool CanCatch(PlayerController player, Rigidbody2D playerBody)
        {
            if (!isActiveAndEnabled || player == null || playerBody == null) return false;
            if (player.IsSwinging || player.IsHanging) return false;
            if (requireAirborne && player.IsGrounded) return false;
            if (playerBody.velocity.y > maxUpwardVelocityToCatch) return false;

            float sqrDistance = ((Vector2)player.transform.position - AnchorPosition).sqrMagnitude;
            return sqrDistance <= catchRadius * catchRadius;
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

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;

            Vector2 anchor = swingPoint != null ? swingPoint.position : transform.position;

            Gizmos.color = catchGizmoColor;
            Gizmos.DrawSphere(anchor, catchRadius);

            Gizmos.color = anchorGizmoColor;
            Gizmos.DrawWireSphere(anchor, 0.08f);
            Gizmos.DrawLine(anchor, anchor + hangOffset);
            Gizmos.DrawWireSphere(anchor + hangOffset, 0.12f);
        }
    }
}

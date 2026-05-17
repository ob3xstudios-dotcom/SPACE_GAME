using Game.Input;
using Game.Player;
using UnityEngine;

namespace Game.World
{
    [RequireComponent(typeof(Collider2D))]
    public class LedgeGrabPoint : MonoBehaviour
    {
        public enum LedgeFacing
        {
            Left = -1,
            Right = 1
        }

        [Header("Hang")]
        [SerializeField] private Transform hangPoint;
        [SerializeField] private Transform standPoint;
        [SerializeField] private LedgeFacing facingWhenHanging = LedgeFacing.Right;

        [Header("Catch")]
        [SerializeField] private LayerMask playerLayer;
        [SerializeField] private bool autoGrabWhenAirborne = false;
        [SerializeField, Range(0.02f, 1f)] private float autoGrabStuckTime = 0.15f;
        [SerializeField, Range(0.01f, 2f)] private float autoGrabMaxAbsVerticalSpeed = 0.2f;
        [SerializeField, Range(0.01f, 3f)] private float autoGrabMaxAbsHorizontalSpeed = 0.5f;
        [SerializeField, Range(0f, 1f)] private float autoGrabBelowHangTolerance = 0.3f;

        [Header("Gizmos")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private bool drawSceneLabel = true;
        [SerializeField] private Color triggerGizmoColor = new Color(1f, 0.75f, 0.15f, 0.25f);
        [SerializeField] private Color hangGizmoColor = new Color(1f, 0.2f, 0.7f, 1f);
        [SerializeField] private Color standGizmoColor = new Color(0.2f, 1f, 0.45f, 1f);
        [SerializeField] private Vector3 labelOffset = new Vector3(0f, 0.35f, 0f);

        private Collider2D triggerCollider;
        private PlayerController playerInRange;
        private InputReader playerInput;
        private float playerInRangeTimer;

        public int FacingDirection => (int)facingWhenHanging;
        public Vector2 HangPosition => hangPoint != null ? hangPoint.position : transform.position;
        public bool HasStandPosition => standPoint != null;
        public Vector2 StandPosition => standPoint != null ? standPoint.position : HangPosition;
        public Vector2 CornerPosition => transform.position;

        private void Awake()
        {
            ResolveRefs(false);
        }

        private void Reset()
        {
            ResolveRefs(true);
        }

        private void OnValidate()
        {
            ResolveRefs(false);
        }

        private void Update()
        {
            if (playerInRange == null || playerInput == null) return;
            playerInRangeTimer += Time.deltaTime;
            if (TryAutoGrab()) return;

            if (!playerInRange.CanEnterLedgeGrabPoint(this)) return;
            if (!playerInput.ConsumeSwingGrabPressed()) return;

            playerInRange.TryEnterLedgeGrabPoint(this);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!CachePlayer(other)) return;

            // Avoid using a SwingGrab press that happened before entering this trigger.
            playerInput?.ConsumeSwingGrabPressed();
            TryAutoGrab();
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (playerInRange == null)
                CachePlayer(other);

            TryAutoGrab();
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null || player != playerInRange) return;

            playerInRange = null;
            playerInput = null;
            playerInRangeTimer = 0f;
        }

        private bool CachePlayer(Collider2D other)
        {
            if (!IsPlayerLayer(other.gameObject.layer)) return false;

            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null) return false;

            playerInRange = player;
            playerInput = player.GetComponentInParent<InputReader>();
            playerInRangeTimer = 0f;
            return playerInput != null;
        }

        private bool IsPlayerLayer(int layer)
        {
            return playerLayer.value == 0 || (playerLayer.value & (1 << layer)) != 0;
        }

        private bool TryAutoGrab()
        {
            if (!autoGrabWhenAirborne || playerInRange == null) return false;
            if (playerInput != null && playerInput.JumpHeld) return false;
            if (playerInRangeTimer < autoGrabStuckTime) return false;
            if (playerInRange.transform.position.y < HangPosition.y - autoGrabBelowHangTolerance) return false;
            if (!playerInRange.CanAutoRescueLedgeGrabPoint(this, autoGrabMaxAbsVerticalSpeed, autoGrabMaxAbsHorizontalSpeed)) return false;

            playerInRange.TryEnterLedgeGrabPoint(this);
            return true;
        }

        private void ResolveRefs(bool createHangPoint)
        {
            if (triggerCollider == null)
                triggerCollider = GetComponent<Collider2D>();

            if (triggerCollider != null)
                triggerCollider.isTrigger = true;

            if (hangPoint == null)
                hangPoint = transform.Find("HangPoint");

            if (standPoint == null)
                standPoint = transform.Find("StandPoint") ?? transform.Find("ClimbPoint");

            if (hangPoint == null && createHangPoint)
            {
                GameObject child = new GameObject("HangPoint");
                child.transform.SetParent(transform);
                child.transform.localPosition = Vector3.zero;
                hangPoint = child.transform;
            }

            if (standPoint == null && createHangPoint)
            {
                GameObject child = new GameObject("StandPoint");
                child.transform.SetParent(transform);
                child.transform.localPosition = new Vector3(FacingDirection * 0.75f, 0.75f, 0f);
                standPoint = child.transform;
            }
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos) return;

            ResolveRefs(false);

            if (triggerCollider != null)
            {
                Gizmos.color = triggerGizmoColor;
                Gizmos.DrawCube(triggerCollider.bounds.center, triggerCollider.bounds.size);
                Gizmos.color = new Color(triggerGizmoColor.r, triggerGizmoColor.g, triggerGizmoColor.b, 1f);
                Gizmos.DrawWireCube(triggerCollider.bounds.center, triggerCollider.bounds.size);
            }

            Vector2 hangPos = hangPoint != null ? hangPoint.position : transform.position;
            Vector2 facingEnd = hangPos + Vector2.right * FacingDirection * 0.35f;

            Gizmos.color = hangGizmoColor;
            Gizmos.DrawSphere(hangPos, 0.12f);
            Gizmos.DrawWireSphere(hangPos, 0.18f);
            Gizmos.DrawLine(hangPos, facingEnd);

            if (standPoint != null)
            {
                Vector2 standPos = standPoint.position;
                Gizmos.color = standGizmoColor;
                Gizmos.DrawCube(standPos, new Vector3(0.18f, 0.18f, 0.02f));
                Gizmos.DrawWireCube(standPos, new Vector3(0.32f, 0.32f, 0.02f));
                Gizmos.DrawLine(hangPos, standPos);
            }

#if UNITY_EDITOR
            if (drawSceneLabel)
            {
                UnityEditor.Handles.color = Color.white;
                UnityEditor.Handles.Label(transform.position + labelOffset, gameObject.name);
            }
#endif
        }
    }
}

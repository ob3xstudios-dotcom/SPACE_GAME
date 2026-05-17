using Game.Player;
using UnityEngine;

namespace Game.Systems
{
    [DisallowMultipleComponent]
    public class CameraLookAheadTarget2D : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform target;
        [SerializeField] private PlayerController player;

        [Header("Look Ahead")]
        [SerializeField] private bool enableLookAhead = true;
        [SerializeField, Range(0f, 4f)] private float horizontalLookAhead = 1.2f;
        [SerializeField, Range(0f, 2f)] private float verticalLookAhead = 0.35f;
        [SerializeField, Range(1f, 20f)] private float followSharpness = 8f;
        [SerializeField, Range(0f, 2f)] private float wallSlideLookAheadMultiplier = 0.45f;

        private Vector3 velocity;

        public Transform TargetProxy => transform;

        private void Reset()
        {
            target = transform.parent;
            if (target != null)
                player = target.GetComponent<PlayerController>();
        }

        private void Awake()
        {
            if (target == null && transform.parent != null)
                target = transform.parent;

            if (player == null && target != null)
                player = target.GetComponent<PlayerController>();
        }

        private void LateUpdate()
        {
            if (target == null) return;

            Vector3 desired = target.position;
            if (enableLookAhead)
                desired += CalculateLookAheadOffset();

            transform.position = Vector3.SmoothDamp(
                transform.position,
                desired,
                ref velocity,
                1f / Mathf.Max(0.01f, followSharpness)
            );
        }

        private Vector3 CalculateLookAheadOffset()
        {
            if (player == null)
                return Vector3.zero;

            Vector2 playerVelocity = player.Velocity;
            float horizontalSign = Mathf.Abs(playerVelocity.x) > 0.1f
                ? Mathf.Sign(playerVelocity.x)
                : (player.FacingLeft ? -1f : 1f);

            float multiplier = player.IsWallSliding ? wallSlideLookAheadMultiplier : 1f;
            float x = horizontalSign * horizontalLookAhead * multiplier;
            float y = Mathf.Clamp(playerVelocity.y / 12f, -1f, 1f) * verticalLookAhead;
            return new Vector3(x, y, 0f);
        }
    }
}

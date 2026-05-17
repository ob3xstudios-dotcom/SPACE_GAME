using UnityEngine;

namespace Game.Systems
{
    public enum SpawnFacingDirection
    {
        Keep = 0,
        Left = -1,
        Right = 1
    }

    public class SpawnPoint : MonoBehaviour
    {
        [SerializeField] private string spawnId;
        [SerializeField] private SpawnFacingDirection facingDirection = SpawnFacingDirection.Keep;
        [SerializeField] private Color gizmoColor = new Color(0.1f, 0.8f, 1f, 0.9f);
        [SerializeField, Min(0.05f)] private float gizmoRadius = 0.25f;
        [SerializeField, Min(0.1f)] private float arrowLength = 0.75f;

        public string SpawnId => spawnId;
        public SpawnFacingDirection FacingDirection => facingDirection;
        public bool HasFacingDirection => facingDirection != SpawnFacingDirection.Keep;
        public int FacingSign => facingDirection == SpawnFacingDirection.Left ? -1 : 1;

        private void OnDrawGizmos()
        {
            DrawGizmo(false);
        }

        private void OnDrawGizmosSelected()
        {
            DrawGizmo(true);
        }

        private void DrawGizmo(bool selected)
        {
            Color color = gizmoColor;
            color.a = selected ? 1f : gizmoColor.a;
            Gizmos.color = color;

            Vector3 pos = transform.position;
            float radius = selected ? gizmoRadius * 1.25f : gizmoRadius;
            Gizmos.DrawWireSphere(pos, radius);
            Gizmos.DrawLine(pos + Vector3.left * radius, pos + Vector3.right * radius);
            Gizmos.DrawLine(pos + Vector3.down * radius, pos + Vector3.up * radius);

#if UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(spawnId))
                UnityEditor.Handles.Label(pos + Vector3.up * (radius + 0.15f), spawnId);
#endif

            Vector3 dir = FacingDirectionToVector();
            if (dir.sqrMagnitude <= 0.001f) return;

            Vector3 end = pos + dir * arrowLength;
            Gizmos.DrawLine(pos, end);
            Gizmos.DrawLine(end, end - dir * 0.18f + Vector3.up * 0.12f);
            Gizmos.DrawLine(end, end - dir * 0.18f + Vector3.down * 0.12f);
        }

        private Vector3 FacingDirectionToVector()
        {
            if (facingDirection == SpawnFacingDirection.Left) return Vector3.left;
            if (facingDirection == SpawnFacingDirection.Right) return Vector3.right;
            return Vector3.zero;
        }
    }
}

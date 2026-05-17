using Game.Player;
using UnityEngine;

namespace Game.Systems
{
    public class Checkpoint : MonoBehaviour
    {
        [SerializeField] private string checkpointId;
        [SerializeField] private Transform respawnPoint;
        [SerializeField] private string playerTag = "Player";
        [SerializeField] private Color gizmoColor = new Color(0.2f, 1f, 0.35f, 0.9f);
        [SerializeField, Min(0.05f)] private float gizmoRadius = 0.3f;

        public string CheckpointId => checkpointId;
        public Transform RespawnTransform => respawnPoint != null ? respawnPoint : transform;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!IsPlayer(other)) return;

            GameManager manager = GameManager.Instance ?? FindObjectOfType<GameManager>();
            if (manager != null)
                manager.SetActiveCheckpoint(this);
        }

        private bool IsPlayer(Collider2D other)
        {
            if (other == null) return false;
            return other.CompareTag(playerTag) ||
                   other.GetComponentInParent<PlayerHealth>() != null ||
                   other.GetComponentInChildren<PlayerHealth>(true) != null;
        }

        private void OnDrawGizmos()
        {
            Transform target = RespawnTransform;
            if (target == null) return;

            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(target.position, gizmoRadius);
            Gizmos.DrawLine(target.position + Vector3.left * gizmoRadius, target.position + Vector3.right * gizmoRadius);
            Gizmos.DrawLine(target.position + Vector3.down * gizmoRadius, target.position + Vector3.up * gizmoRadius);

#if UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(checkpointId))
                UnityEditor.Handles.Label(target.position + Vector3.up * (gizmoRadius + 0.15f), checkpointId);
#endif
        }
    }
}

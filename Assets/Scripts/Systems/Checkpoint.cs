using UnityEngine;

namespace Game.Systems
{
    public class Checkpoint : MonoBehaviour
    {
        [SerializeField] private string checkpointId;
        [SerializeField] private Transform respawnPoint;
        [SerializeField] private string playerTag = "Player";
        [SerializeField] private SpriteRenderer visualRenderer;
        [SerializeField] private Color inactiveColor = new Color(1f, 1f, 1f, 0.45f);
        [SerializeField] private Color activeColor = new Color(0.35f, 1f, 0.4f, 1f);
        [SerializeField] private AudioSource activationAudio;
        [SerializeField] private ParticleSystem activationParticles;
        [SerializeField] private Color gizmoColor = new Color(0.2f, 1f, 0.35f, 0.9f);
        [SerializeField, Min(0.05f)] private float gizmoRadius = 0.3f;

        public string CheckpointId => checkpointId;
        public Transform RespawnTransform => respawnPoint != null ? respawnPoint : transform;

        private void Awake()
        {
            ApplyVisual(false);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!IsPlayer(other)) return;

            GameManager manager = GameManager.Instance ?? FindObjectOfType<GameManager>();
            if (manager != null)
                manager.SetActiveCheckpoint(this);

            ApplyVisual(true);
            PlayActivationFeedback();
        }

        private bool IsPlayer(Collider2D other)
        {
            if (other == null) return false;
            return other.CompareTag(playerTag);
        }

        private void ApplyVisual(bool active)
        {
            if (visualRenderer == null) return;
            visualRenderer.color = active ? activeColor : inactiveColor;
        }

        private void PlayActivationFeedback()
        {
            if (activationAudio != null)
                activationAudio.Play();

            if (activationParticles != null)
                activationParticles.Play();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying)
                ApplyVisual(false);
        }
#endif

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

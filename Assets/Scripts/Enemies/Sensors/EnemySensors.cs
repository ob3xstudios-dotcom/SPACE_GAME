using UnityEngine;

namespace Game.Enemies
{
    public class EnemySensors : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform player;
        [SerializeField] private string playerTag = "Player";

        [Header("Shared Perception")]
        [SerializeField] private EnemyPerception2D perception;
        [SerializeField, Range(1.0f, 3f)] private float loseDistanceMultiplier = 1.25f;

        [Header("Attack Range")]
        [SerializeField, Range(0.05f, 10f)] private float attackRangeX = 1.1f;
        [SerializeField, Range(0.05f, 10f)] private float attackRangeY = 0.6f;

        [Header("Debug")]
        [SerializeField] private bool debugGizmos = true;

        private EnemyBase enemy;

        private bool hasLineOfSight;
        private bool hasVisualLineOfSight;
        private float lastSeenTime = -999f;
        private Vector2 lastKnownPlayerPos;
        private int externalFacingDirection = 1;

        public Transform Player => player;
        public float ViewDistance => perception != null ? perception.VisionDistance : 0f;
        public float LoseDistance => ViewDistance * loseDistanceMultiplier;
        public bool HasLineOfSight => hasLineOfSight;
        public bool HasVisualLineOfSight => hasVisualLineOfSight;
        public Vector2 LastKnownPlayerPos => lastKnownPlayerPos;
        public float AttackRangeX => attackRangeX;
        public float AttackRangeY => attackRangeY;
        public EnemyPerception2D Perception => perception;

        private void Awake()
        {
            enemy = GetComponent<EnemyBase>();
            if (perception == null)
                perception = GetComponent<EnemyPerception2D>();
        }

        public void ResolvePlayer()
        {
            if (player == null)
            {
                var go = GameObject.FindGameObjectWithTag(playerTag);
                if (go != null) player = go.transform;
            }

            if (perception != null && perception.Target != null)
                player = perception.Target;
        }

        public void TickVision(Vector2 origin)
        {
            ResolvePlayer();

            if (player == null)
            {
                hasLineOfSight = false;
                hasVisualLineOfSight = false;
                return;
            }

            hasVisualLineOfSight = perception != null && perception.IsPlayerVisible;
            hasLineOfSight = hasVisualLineOfSight ||
                             (perception != null && perception.HasActiveNoiseStimulus);

            if (hasLineOfSight)
            {
                lastSeenTime = Time.time;
                lastKnownPlayerPos = hasVisualLineOfSight
                    ? perception.LastSeenPosition
                    : perception.LastHeardPosition;
            }
        }

        public void SetExternalFacingDirection(int direction)
        {
            if (enemy == null && direction != 0)
                externalFacingDirection = direction < 0 ? -1 : 1;
            if (perception != null && direction != 0)
                perception.SetFacingDirection(direction < 0 ? Vector2.left : Vector2.right);
        }

        public bool IsPlayerInAttackRange(Vector2 origin)
        {
            ResolvePlayer();
            if (player == null) return false;

            Vector2 enemyCenter = GetCenter(gameObject, origin);
            Vector2 playerCenter = GetCenter(player.gameObject, (Vector2)player.position);
            Vector2 d = playerCenter - enemyCenter;

            return Mathf.Abs(d.x) <= attackRangeX && Mathf.Abs(d.y) <= attackRangeY;
        }

        public float DistanceToPlayer(Vector2 origin)
        {
            ResolvePlayer();
            if (player == null) return float.PositiveInfinity;

            Vector2 enemyCenter = GetCenter(gameObject, origin);
            Vector2 playerCenter = GetCenter(player.gameObject, (Vector2)player.position);
            return Vector2.Distance(enemyCenter, playerCenter);
        }


        private static Vector2 GetCenter(GameObject go, Vector2 fallback)
        {
            var col = go.GetComponent<Collider2D>() ?? go.GetComponentInChildren<Collider2D>(true);
            return col != null ? (Vector2)col.bounds.center : fallback;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!debugGizmos) return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, ViewDistance);

            Gizmos.color = Color.gray;
            Gizmos.DrawWireSphere(transform.position, LoseDistance);

            Gizmos.color = new Color(1f, 0.5f, 0f, 1f);
            Gizmos.DrawWireCube(transform.position, new Vector3(attackRangeX * 2f, attackRangeY * 2f, 0f));

            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(lastKnownPlayerPos, 0.12f);

            var eb = GetComponent<EnemyBase>();
            if (eb != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(transform.position, transform.position + (Vector3)eb.Forward * 1.2f);
            }
        }
#endif
    }
}

using UnityEngine;

namespace Game.Enemies
{
    public class EnemySensors : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform player;
        [SerializeField] private string playerTag = "Player";

        [Header("Vision")]
        [SerializeField, Range(0.5f, 50f)] private float viewDistance = 6f;
        [SerializeField, Range(1.0f, 3f)] private float loseDistanceMultiplier = 1.25f;
        [SerializeField, Range(0f, 10f)] private float memoryTime = 2.0f;
        [SerializeField] private LayerMask playerLayer;
        [SerializeField] private LayerMask obstacleLayer;

        [Header("Stealth Vision")]
        [SerializeField] private bool requireFacingOnlyWhenPlayerStealthed = true;
        [SerializeField, Range(-1f, 1f)] private float frontDotThreshold = 0.25f;
        [SerializeField, Range(0.1f, 1f)] private float stealthViewDistanceMultiplier = 1.0f;

        [Header("Attack Range")]
        [SerializeField, Range(0.05f, 10f)] private float attackRangeX = 1.1f;
        [SerializeField, Range(0.05f, 10f)] private float attackRangeY = 0.6f;

        [Header("Debug")]
        [SerializeField] private bool debugGizmos = true;

        private EnemyBase enemy;
        private Game.Player.PlayerController cachedPlayerController;

        private bool hasLineOfSight;
        private float lastSeenTime = -999f;
        private Vector2 lastKnownPlayerPos;

        public Transform Player => player;
        public float ViewDistance => viewDistance;
        public float LoseDistance => viewDistance * loseDistanceMultiplier;
        public bool HasLineOfSight => hasLineOfSight;
        public bool HasTargetInMemory => hasLineOfSight || (Time.time - lastSeenTime) <= memoryTime;
        public Vector2 LastKnownPlayerPos => lastKnownPlayerPos;
        public float AttackRangeX => attackRangeX;
        public float AttackRangeY => attackRangeY;

        private void Awake()
        {
            enemy = GetComponent<EnemyBase>();
        }

        public void ResolvePlayer()
        {
            if (player == null)
            {
                var go = GameObject.FindGameObjectWithTag(playerTag);
                if (go != null) player = go.transform;
            }

            if (player != null && cachedPlayerController == null)
            {
                cachedPlayerController =
                    player.GetComponent<Game.Player.PlayerController>() ??
                    player.GetComponentInChildren<Game.Player.PlayerController>(true);
            }
        }

        public void TickVision(Vector2 origin)
        {
            ResolvePlayer();

            if (player == null)
            {
                hasLineOfSight = false;
                return;
            }

            Vector2 enemyCenter = GetCenter(gameObject, origin);
            Vector2 playerCenter = GetCenter(player.gameObject, (Vector2)player.position);
            Vector2 toPlayer = playerCenter - enemyCenter;
            float dist = toPlayer.magnitude;

            bool playerStealthed = IsPlayerStealthed();

            float effectiveViewDistance = viewDistance * (playerStealthed ? stealthViewDistanceMultiplier : 1f);
            float effectiveLoseDistance = effectiveViewDistance * loseDistanceMultiplier;

            if (dist > effectiveLoseDistance)
            {
                hasLineOfSight = false;
                return;
            }

            if (requireFacingOnlyWhenPlayerStealthed && playerStealthed)
            {
                Vector2 forward = enemy != null ? enemy.Forward : Vector2.right;
                Vector2 dirToPlayer = dist > 0.0001f ? toPlayer / dist : forward;

                float dot = Vector2.Dot(forward, dirToPlayer);
                if (dot < frontDotThreshold)
                {
                    hasLineOfSight = false;
                    return;
                }
            }

            if (playerLayer.value != 0)
            {
                var hit = Physics2D.OverlapCircle(enemyCenter, effectiveViewDistance, playerLayer);
                if (hit == null)
                {
                    hasLineOfSight = false;
                    return;
                }
            }

            if (obstacleLayer.value != 0 && dist > 0.001f)
            {
                var block = Physics2D.Raycast(enemyCenter, toPlayer.normalized, dist, obstacleLayer);
                if (block.collider != null)
                {
                    hasLineOfSight = false;
                    return;
                }
            }

            hasLineOfSight = dist <= effectiveViewDistance;

            if (hasLineOfSight)
            {
                lastSeenTime = Time.time;
                lastKnownPlayerPos = playerCenter;
            }
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

        private bool IsPlayerStealthed()
        {
            if (cachedPlayerController == null) return false;
            return cachedPlayerController.IsCrouching || cachedPlayerController.IsLayDown;
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
            Gizmos.DrawWireSphere(transform.position, viewDistance);

            Gizmos.color = Color.gray;
            Gizmos.DrawWireSphere(transform.position, viewDistance * loseDistanceMultiplier);

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
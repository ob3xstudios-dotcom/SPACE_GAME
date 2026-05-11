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
        [SerializeField] private LayerMask playerLayer;
        [SerializeField] private LayerMask obstacleLayer;

        [Header("Stealth Vision")]
        [SerializeField, Range(0.1f, 1f)] private float stealthViewDistanceMultiplier = 1.0f;

        [Header("Hearing")]
        [SerializeField, Range(0.5f, 30f)] private float hearingDistance = 5f;
        [SerializeField, Min(0f)] private float hearingSpeedThreshold = 6f;

        [Header("Attack Range")]
        [SerializeField, Range(0.05f, 10f)] private float attackRangeX = 1.1f;
        [SerializeField, Range(0.05f, 10f)] private float attackRangeY = 0.6f;

        [Header("Debug")]
        [SerializeField] private bool debugGizmos = true;

        private EnemyBase enemy;
        private Game.Player.PlayerController cachedPlayerController;
        private Rigidbody2D cachedPlayerRb;
        private const float VisionRayEpsilon = 0.02f;

        private bool hasLineOfSight;
        private float lastSeenTime = -999f;
        private Vector2 lastKnownPlayerPos;

        public Transform Player => player;
        public float ViewDistance => viewDistance;
        public float LoseDistance => viewDistance * loseDistanceMultiplier;
        public bool HasLineOfSight => hasLineOfSight;
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

            if (player != null && cachedPlayerRb == null)
            {
                cachedPlayerRb =
                    player.GetComponent<Rigidbody2D>() ??
                    player.GetComponentInChildren<Rigidbody2D>(true);
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

            if (!IsPlayerOnLayer())
            {
                hasLineOfSight = false;
                return;
            }

            Vector2 enemyCenter = GetCenter(gameObject, origin);
            Vector2 playerCenter = GetCenter(player.gameObject, (Vector2)player.position);
            Vector2 toPlayer = playerCenter - enemyCenter;
            float dist = toPlayer.magnitude;

            bool canSee = CanSeePlayerByVision(enemyCenter, toPlayer, dist);
            bool canHear = CanHearPlayer(enemyCenter, toPlayer, dist);

            hasLineOfSight = canSee || canHear;

            if (hasLineOfSight)
            {
                lastSeenTime = Time.time;
                lastKnownPlayerPos = playerCenter;
            }
        }

        private bool CanSeePlayerByVision(Vector2 enemyCenter, Vector2 toPlayer, float dist)
        {
            bool playerStealthed = IsPlayerStealthed();
            float effectiveViewDistance = viewDistance * (playerStealthed ? stealthViewDistanceMultiplier : 1f);

            if (dist > effectiveViewDistance)
                return false;

            if (dist <= 0.001f)
                return true;

            Vector2 forward = enemy != null ? enemy.Forward : Vector2.right;
            Vector2 dirToPlayer = toPlayer / dist;

            if (Vector2.Dot(forward, dirToPlayer) <= 0f)
                return false;

            return !IsBlockedByObstacle(enemyCenter, dirToPlayer, dist);
        }

        private bool CanHearPlayer(Vector2 enemyCenter, Vector2 toPlayer, float dist)
        {
            if (IsPlayerStealthed()) return false;
            if (cachedPlayerRb == null) return false;
            if (dist > hearingDistance) return false;
            if (cachedPlayerRb.velocity.magnitude < hearingSpeedThreshold) return false;
            if (dist <= 0.001f) return true;

            Vector2 dirToPlayer = toPlayer / dist;
            return !IsBlockedByObstacle(enemyCenter, dirToPlayer, dist);
        }

        private bool IsBlockedByObstacle(Vector2 origin, Vector2 dir, float dist)
        {
            int mask = ObstacleMask();
            if (mask == 0) return false;

            RaycastHit2D block = Physics2D.Raycast(origin, dir, dist - VisionRayEpsilon, mask);
            return block.collider != null && !IsPlayerCollider(block.collider);
        }

        private bool IsPlayerOnLayer()
        {
            int mask = PlayerMask();
            return mask == 0 || (mask & (1 << player.gameObject.layer)) != 0;
        }

        private int PlayerMask()
        {
            if (playerLayer.value != 0)
                return playerLayer.value;

            return LayerMask.GetMask("Player");
        }

        private int ObstacleMask()
        {
            if (obstacleLayer.value != 0)
                return obstacleLayer.value;

            return LayerMask.GetMask("Ground", "Wall");
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

        private bool IsPlayerCollider(Collider2D col)
        {
            if (col == null || player == null) return false;
            return col.transform == player || col.transform.IsChildOf(player);
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

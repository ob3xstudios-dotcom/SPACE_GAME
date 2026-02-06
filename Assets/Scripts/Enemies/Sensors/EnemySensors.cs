using UnityEngine;

namespace Game.Enemies
{
    /// <summary>
    /// Sensores 2D:
    /// - Visión por distancia + (opcional) precheck por layer + raycast de obstáculos
    /// - Memoria (last seen + lastKnownPos)
    /// - Rango de ataque por CAJA X/Y (usando centros de colliders para que "encima" funcione)
    /// </summary>
    public class EnemySensors : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform player;
        [SerializeField] private string playerTag = "Player";

        [Header("Vision")]
        [SerializeField, Range(0.5f, 50f)] private float viewDistance = 6f;
        [SerializeField, Range(1.0f, 3f)] private float loseDistanceMultiplier = 1.25f;
        [SerializeField, Range(0f, 10f)] private float memoryTime = 2.0f;

        [Tooltip("Opcional: precheck (OverlapCircle) para evitar raycasts si está vacío se ignora.")]
        [SerializeField] private LayerMask playerLayer;

        [Tooltip("Capas que bloquean visión (Walls, Ground, etc). Si está vacío, no bloquea visión.")]
        [SerializeField] private LayerMask obstacleLayer;

        [Header("Attack Range (X/Y box)")]
        [SerializeField, Range(0.05f, 10f)] private float attackRangeX = 1.1f;
        [SerializeField, Range(0.05f, 10f)] private float attackRangeY = 0.6f;

        [Header("Debug")]
        [SerializeField] private bool debugGizmos = true;

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

        public void ResolvePlayer()
        {
            if (player != null) return;
            var go = GameObject.FindGameObjectWithTag(playerTag);
            if (go != null) player = go.transform;
        }

        /// <summary>
        /// Llamad esto desde EnemyBase.Tick() pasando el origen (normalmente enemy.RB.position).
        /// </summary>
        public void TickVision(Vector2 origin)
        {
            ResolvePlayer();

            if (player == null)
            {
                hasLineOfSight = false;
                return;
            }

            // ✅ usa centros (evita pivots en pies)
            Vector2 enemyCenter = GetCenter(gameObject, origin);
            Vector2 playerCenter = GetCenter(player.gameObject, (Vector2)player.position);

            Vector2 toPlayer = playerCenter - enemyCenter;
            float dist = toPlayer.magnitude;

            if (dist > LoseDistance)
            {
                hasLineOfSight = false;
                return;
            }

            // (Opcional) precheck por layer para evitar raycasts
            if (playerLayer.value != 0)
            {
                var hit = Physics2D.OverlapCircle(enemyCenter, viewDistance, playerLayer);
                if (hit == null)
                {
                    hasLineOfSight = false;
                    return;
                }
            }

            // Raycast de obstáculos (si hay capas)
            if (obstacleLayer.value != 0 && dist > 0.001f)
            {
                var block = Physics2D.Raycast(enemyCenter, toPlayer.normalized, dist, obstacleLayer);
                if (block.collider != null)
                {
                    hasLineOfSight = false;
                    return;
                }
            }

            hasLineOfSight = dist <= viewDistance;

            if (hasLineOfSight)
            {
                lastSeenTime = Time.time;
                lastKnownPlayerPos = playerCenter;
            }
        }

        /// <summary>
        /// ✅ Rango de ataque por caja X/Y usando centros de colliders.
        /// Esto arregla el caso "player encima" (Y) aunque los pivots estén en los pies.
        /// </summary>
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
            return (col != null) ? (Vector2)col.bounds.center : fallback;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!debugGizmos) return;

            // Vision
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, viewDistance);

            Gizmos.color = Color.gray;
            Gizmos.DrawWireSphere(transform.position, viewDistance * loseDistanceMultiplier);

            // Attack box (en el centro del enemy)
            Gizmos.color = new Color(1f, 0.5f, 0f, 1f);
            Vector3 p = transform.position;
            Gizmos.DrawWireCube(p, new Vector3(attackRangeX * 2f, attackRangeY * 2f, 0f));

            // Last known
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(lastKnownPlayerPos, 0.12f);
        }
#endif
    }
}

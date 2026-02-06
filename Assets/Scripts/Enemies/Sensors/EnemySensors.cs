using UnityEngine;

namespace Game.Enemies
{
    /// <summary>
    /// Sensores 2D:
    /// - Visión por distancia + (opcional) precheck por layer + raycast de obstáculos
    /// - Memoria (last seen + lastKnownPos)
    /// - Rango de ataque por CAJA X/Y (usando centros de colliders para que "encima" funcione)
    ///
    /// ✅ Stealth:
    /// - Si el Player está en Crouch/LayDown, el enemigo SOLO ve si el Player está delante (según facing del enemigo)
    /// - Si el Player NO está en stealth, el enemigo ve normal (sin chequeo de facing)
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

        [Header("Stealth Vision")]
        [Tooltip("Si ON: cuando el player está en crouch/laydown, solo hay LOS si está delante del enemigo.")]
        [SerializeField] private bool requireFacingOnlyWhenPlayerStealthed = true;

        [Tooltip("Dot mínimo para considerar 'delante'. 0=180º, 0.5≈120º, 0.7≈90º.")]
        [SerializeField, Range(-1f, 1f)] private float frontDotThreshold = 0.25f;

        [Tooltip("Multiplica la distancia de visión cuando el player está en stealth. 1 = igual, 0.6 = ve menos.")]
        [SerializeField, Range(0.1f, 1f)] private float stealthViewDistanceMultiplier = 1.0f;

        [Header("Attack Range (X/Y box)")]
        [SerializeField, Range(0.05f, 10f)] private float attackRangeX = 1.1f;
        [SerializeField, Range(0.05f, 10f)] private float attackRangeY = 0.6f;

        [Header("Debug")]
        [SerializeField] private bool debugGizmos = true;

        private bool hasLineOfSight;
        private float lastSeenTime = -999f;
        private Vector2 lastKnownPlayerPos;

        // Cache player controller para saber si está en stealth
        private Game.Player.PlayerController cachedPlayerController;

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
            if (go != null)
            {
                player = go.transform;
                cachedPlayerController = player.GetComponent<Game.Player.PlayerController>()
                                      ?? player.GetComponentInChildren<Game.Player.PlayerController>(true);
            }
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

            // ---------
            // ✅ Stealth gating: SOLO aplica el "facing check" si el player está en stealth
            // ---------
            bool playerStealthed = IsPlayerStealthed();

            float effectiveViewDistance = viewDistance * (playerStealthed ? stealthViewDistanceMultiplier : 1f);
            float effectiveLoseDistance = (viewDistance * loseDistanceMultiplier) * (playerStealthed ? stealthViewDistanceMultiplier : 1f);

            if (dist > effectiveLoseDistance)
            {
                hasLineOfSight = false;
                return;
            }

            if (requireFacingOnlyWhenPlayerStealthed && playerStealthed)
            {
                // Facing del enemigo por scale.x
                int facing = (transform.localScale.x < 0f) ? -1 : 1;
                Vector2 enemyForward = (facing < 0) ? Vector2.left : Vector2.right;

                // Si toPlayer está "delante", dot será alto. Si está detrás, dot será negativo.
                Vector2 dirToPlayer = (dist > 0.0001f) ? (toPlayer / dist) : enemyForward;
                float dot = Vector2.Dot(enemyForward, dirToPlayer);

                if (dot < frontDotThreshold)
                {
                    hasLineOfSight = false;
                    return;
                }
            }

            // (Opcional) precheck por layer para evitar raycasts
            if (playerLayer.value != 0)
            {
                var hit = Physics2D.OverlapCircle(enemyCenter, effectiveViewDistance, playerLayer);
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
            // cache lazy si se ha asignado player por inspector
            if (player != null && cachedPlayerController == null)
            {
                cachedPlayerController = player.GetComponent<Game.Player.PlayerController>()
                                      ?? player.GetComponentInChildren<Game.Player.PlayerController>(true);
            }

            if (cachedPlayerController == null) return false;
            return cachedPlayerController.IsCrouching || cachedPlayerController.IsLayDown;
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

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, viewDistance);

            Gizmos.color = Color.gray;
            Gizmos.DrawWireSphere(transform.position, viewDistance * loseDistanceMultiplier);

            Gizmos.color = new Color(1f, 0.5f, 0f, 1f);
            Vector3 p = transform.position;
            Gizmos.DrawWireCube(p, new Vector3(attackRangeX * 2f, attackRangeY * 2f, 0f));

            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(lastKnownPlayerPos, 0.12f);
        }
#endif
    }
}

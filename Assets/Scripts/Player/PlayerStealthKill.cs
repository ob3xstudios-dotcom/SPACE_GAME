using UnityEngine;

namespace Game.Player
{
    public class PlayerStealthKill : MonoBehaviour
    {
        [Header("Tuning")]
        [SerializeField] private LayerMask enemyLayer;
        [SerializeField, Range(0.2f, 2f)] private float killRadius = 1.5f;
        [Tooltip("Cuanto más alto, más estricto para estar detrás.")]
        [SerializeField, Range(0f, 1f)] private float behindDotThreshold = 0.35f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private Collider2D ownCollider;

        private void Awake()
        {
            ownCollider = GetComponent<Collider2D>() ?? GetComponentInChildren<Collider2D>(true);
        }

        /// <summary>
        /// Intento de stealth kill usando el botón de ATTACK,
        /// si estás detrás y el enemigo no te está detectando.
        /// Devuelve true si mata a alguien.
        /// </summary>
        public bool TryStealthKill()
        {
            Vector2 origin = KillOrigin();

            // 1) Buscar enemigos cercanos
            var hits = Physics2D.OverlapCircleAll(origin, killRadius, enemyLayer);
            if (hits == null || hits.Length == 0)
            {
                LogNoEnemyFound(origin);
                return false;
            }

            foreach (var col in hits)
            {
                if (col == null)
                {
                    LogBlocked(origin, null, null, "hit collider is null", 0f);
                    continue;
                }

                var target = col.GetComponentInParent<Game.Enemies.EnemyBase>();
                if (target == null)
                {
                    LogBlocked(origin, col, null, "hit has no EnemyBase", 0f);
                    continue;
                }

                // 2) Si el enemigo te detecta por vista o sonido, no hay stealth
                if (target.CanSeePlayer())
                {
                    LogBlocked(origin, col, target, "target.CanSeePlayer() == true", BehindDot(origin, target));
                    continue;
                }

                // 3) “Detrás” del enemigo usando el facing real de EnemyBase.
                float dot = BehindDot(origin, target);
                bool isBehind = dot < -behindDotThreshold;
                if (!isBehind)
                {
                    LogBlocked(origin, col, target, "behindDot did not pass threshold", dot);
                    continue;
                }

                // 4) Ejecutar kill
                if (TryKillEnemy(target))
                {
                    LogSuccess(origin, col, target, dot);
                    return true;
                }

                LogBlocked(origin, col, target, "TryKillEnemy failed", dot);
            }

            return false;
        }

        private bool TryKillEnemy(Game.Enemies.EnemyBase enemy)
        {
            var health =
                enemy.GetComponent<Game.Combat.Health>() ??
                enemy.GetComponentInChildren<Game.Combat.Health>(true);

            if (health != null)
            {
                health.TakeDamage(9999, transform.position);
                return true;
            }

            enemy.gameObject.SetActive(false);
            return true;
        }

        private Vector2 KillOrigin()
        {
            if (ownCollider == null)
                ownCollider = GetComponent<Collider2D>() ?? GetComponentInChildren<Collider2D>(true);

            return ownCollider != null ? (Vector2)ownCollider.bounds.center : (Vector2)transform.position;
        }

        private float BehindDot(Vector2 origin, Game.Enemies.EnemyBase enemy)
        {
            if (enemy == null) return 0f;

            Vector2 enemyForward = enemy.Forward;
            Vector2 toPlayer = (origin - (Vector2)enemy.transform.position).normalized;
            return Vector2.Dot(enemyForward, toPlayer);
        }

        private void LogNoEnemyFound(Vector2 origin)
        {
            if (!debugLogs) return;

            Debug.Log(
                $"[STEALTH KILL] BLOCKED: no enemy found in enemyLayer | radius={killRadius:0.00} " +
                $"enemyLayer={enemyLayer.value} origin={origin} playerPos={transform.position}");
        }

        private void LogBlocked(Vector2 origin, Collider2D col, Game.Enemies.EnemyBase enemy, string reason, float behindDot)
        {
            if (!debugLogs) return;

            string enemyName = enemy != null ? enemy.name : "NULL";
            Vector3 enemyPos = enemy != null ? enemy.transform.position : Vector3.zero;
            Vector2 enemyForward = enemy != null ? enemy.Forward : Vector2.zero;
            bool canSee = enemy != null && enemy.CanSeePlayer();
            float distance = enemy != null ? Vector2.Distance(origin, enemy.transform.position) : -1f;
            int hitLayer = col != null ? col.gameObject.layer : -1;
            string hitLayerName = hitLayer >= 0 ? LayerMask.LayerToName(hitLayer) : "NULL";

            Debug.Log(
                $"[STEALTH KILL] BLOCKED: {reason} | enemy={enemyName} distance={distance:0.00} " +
                $"canSee={canSee} behindDot={behindDot:0.000} threshold=-{behindDotThreshold:0.000} " +
                $"enemyForward={enemyForward} origin={origin} playerPos={transform.position} enemyPos={enemyPos} " +
                $"hitLayer={hitLayer}({hitLayerName}) hit={GetHitName(col)} enemyLayerMask={enemyLayer.value}");
        }

        private void LogSuccess(Vector2 origin, Collider2D col, Game.Enemies.EnemyBase enemy, float behindDot)
        {
            if (!debugLogs) return;

            float distance = enemy != null ? Vector2.Distance(origin, enemy.transform.position) : -1f;
            int hitLayer = col != null ? col.gameObject.layer : -1;
            string hitLayerName = hitLayer >= 0 ? LayerMask.LayerToName(hitLayer) : "NULL";

            Debug.Log(
                $"[STEALTH KILL] SUCCESS | enemy={enemy.name} distance={distance:0.00} " +
                $"canSee={enemy.CanSeePlayer()} behindDot={behindDot:0.000} enemyForward={enemy.Forward} " +
                $"origin={origin} playerPos={transform.position} enemyPos={enemy.transform.position} " +
                $"hitLayer={hitLayer}({hitLayerName}) hit={GetHitName(col)}");
        }

        private static string GetHitName(Collider2D col)
        {
            return col != null ? col.name : "NULL";
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(KillOrigin(), killRadius);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(KillOrigin(), killRadius);
        }
#endif
    }
}

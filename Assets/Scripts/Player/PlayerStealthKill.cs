using UnityEngine;

namespace Game.Player
{
    public class PlayerStealthKill : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private Game.Input.InputReader input;
        [SerializeField] private PlayerMana mana;

        [Header("Tuning")]
        [SerializeField] private LayerMask enemyLayer;
        [SerializeField, Range(0.2f, 2f)] private float killRadius = 0.6f;
        [Tooltip("Cuanto más alto, más estricto para estar detrás.")]
        [SerializeField, Range(0f, 1f)] private float behindDotThreshold = 0.35f;

        [Header("Reward")]
        [SerializeField, Min(0)] private int manaReward = 1;

        private void Awake()
        {
            if (input == null) input = GetComponent<Game.Input.InputReader>();
            if (mana == null) mana = GetComponent<PlayerMana>();
        }

        /// <summary>
        /// Intento de stealth kill usando el botón de ATTACK,
        /// pero solo si estás en Crouch/LayDown y el enemigo NO te ve y estás detrás.
        /// Devuelve true si mata a alguien.
        /// </summary>
        public bool TryStealthKill()
        {
            if (input == null) return false;

            // 1) Debe estar en modo sigilo
            if (!input.IsCrouching && !input.IsLayDown)
                return false;

            // 2) Buscar enemigos cercanos
            var hits = Physics2D.OverlapCircleAll(transform.position, killRadius, enemyLayer);
            if (hits == null || hits.Length == 0) return false;

            foreach (var col in hits)
            {
                if (col == null) continue;

                var target = col.GetComponentInParent<Game.Enemies.EnemyBase>();
                if (target == null) continue;

                // 3) Si el enemigo te ve, no hay stealth
                if (target.CanSeePlayer())
                    continue;

                // 4) “Detrás” del enemigo (asumiendo facing por scale.x)
                int facing = (target.transform.localScale.x < 0f) ? -1 : 1;
                Vector2 enemyForward = (facing < 0) ? Vector2.left : Vector2.right;

                Vector2 toPlayer = ((Vector2)transform.position - (Vector2)target.transform.position).normalized;

                // Si estás detrás: dot con forward es negativo.
                float dot = Vector2.Dot(enemyForward, toPlayer);
                bool isBehind = dot < -behindDotThreshold;
                if (!isBehind) continue;

                // 5) Ejecutar kill
                if (TryKillEnemy(target))
                {
                    mana?.AddMana(manaReward);
                    return true;
                }
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

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position, killRadius);
        }
#endif
    }
}

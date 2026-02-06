using UnityEngine;

namespace Game.Enemies.Combat
{
    public class EnemyMeleeAttack : MonoBehaviour
    {
        [Header("Hitbox")]
        [SerializeField] private Transform attackPoint;
        [SerializeField, Range(0.05f, 2f)] private float attackRadius = 0.45f;
        [SerializeField] private LayerMask targetLayer;

        [Header("Directional Attack (X/Y)")]
        [SerializeField] private bool directional = true;

        [Tooltip("Si ON, fuerza el ataque a 4 direcciones (L/R/U/D).")]
        [SerializeField] private bool snapTo4Directions = true;

        [Tooltip("Cuanto MÁS BAJO, más fácil será elegir vertical (arriba/abajo) aunque haya algo de X. Ej: 0.6 = más vertical.")]
        [SerializeField, Range(0.2f, 1.0f)] private float verticalBias = 0.65f;

        [Tooltip("Si |y| >= este valor, prioriza vertical sí o sí (útil si pivots están raros).")]
        [SerializeField, Range(0f, 5f)] private float forceVerticalIfAbsYOver = 0.35f;

        [Tooltip("Distancia desde el centro del enemy hasta el punto de ataque.")]
        [SerializeField, Min(0.01f)] private float attackOffset = 0.6f;

        [Header("Damage")]
        [SerializeField, Min(1)] private int damage = 1;
        [SerializeField, Min(0.05f)] private float cooldown = 0.8f;

        [Header("Debug")]
        [SerializeField] private bool debugGizmos = true;
        [SerializeField] private bool debugLogs = true;

        private float cd;

        public bool CanAttack => cd <= 0f;
        public float Cooldown => cooldown;

        private void Update()
        {
            if (cd > 0f) cd -= Time.deltaTime;
        }

        public void ForceCooldown() => cd = cooldown;

        public bool TryAttack(Vector2 sourcePosition) => TryAttack(sourcePosition, Vector2.right);

        public bool TryAttack(Vector2 sourcePosition, Vector2 attackDir)
        {
            if (!CanAttack)
            {
                if (debugLogs) Debug.Log($"[ENEMY ATTACK] {name} blocked by cooldown ({cd:0.00}s)");
                return false;
            }

            if (targetLayer.value == 0)
            {
                Debug.LogWarning($"[ENEMY ATTACK] {name} targetLayer=NOTHING (mask=0). Marca la layer del Player en el inspector.");
                // seguimos, pero va a dar hits=0 siempre
            }

            cd = cooldown;

            Vector2 dir = (attackDir.sqrMagnitude < 0.0001f) ? Vector2.right : attackDir.normalized;

            if (directional && snapTo4Directions)
            {
                float ax = Mathf.Abs(attackDir.x);
                float ay = Mathf.Abs(attackDir.y);

                bool forceVertical = ay >= forceVerticalIfAbsYOver;
                bool chooseVertical = forceVertical || (ay >= ax * verticalBias);

                if (chooseVertical)
                    dir = new Vector2(0f, Mathf.Sign(attackDir.y == 0f ? 1f : attackDir.y));
                else
                    dir = new Vector2(Mathf.Sign(attackDir.x == 0f ? 1f : attackDir.x), 0f);
            }

            Vector2 hitPos = (Vector2)transform.position + (directional ? dir * attackOffset : Vector2.zero);

            if (attackPoint != null)
                attackPoint.position = hitPos;

            if (debugLogs)
            {
                string ap = (attackPoint != null) ? attackPoint.position.ToString() : "NULL";
                Debug.Log($"[ENEMY ATTACK] {name} rawDir={attackDir} dir={dir} hitPos={hitPos} ap={ap} r={attackRadius} mask={targetLayer.value} dmg={damage} cd={cooldown:0.00}");
            }

            var hits = Physics2D.OverlapCircleAll(hitPos, attackRadius, targetLayer);

            if (debugLogs)
                Debug.Log($"[ENEMY ATTACK] {name} hits={hits.Length}");

            bool didHit = false;

            for (int i = 0; i < hits.Length; i++)
            {
                var col = hits[i];
                if (col == null) continue;

                var dmgable =
                    col.GetComponent<Game.Combat.IDamageable>() ??
                    col.GetComponentInParent<Game.Combat.IDamageable>();

                if (dmgable != null)
                {
                    if (debugLogs) Debug.Log($"[ENEMY ATTACK] {name} -> HIT {col.name} for {damage}");
                    dmgable.TakeDamage(damage, sourcePosition);
                    didHit = true;
                }
            }

            if (debugLogs)
                Debug.Log($"[ENEMY ATTACK] {name} didHit={didHit}");

            return didHit;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!debugGizmos) return;
            Gizmos.color = Color.red;
            Vector3 p = (attackPoint != null) ? attackPoint.position : transform.position;
            Gizmos.DrawWireSphere(p, attackRadius);
        }
#endif
    }
}

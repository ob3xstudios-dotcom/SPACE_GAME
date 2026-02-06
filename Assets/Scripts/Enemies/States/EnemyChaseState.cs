using System.Reflection;
using UnityEngine;

namespace Game.Enemies.States
{
    public class EnemyChaseState : Game.Enemies.IEnemyState
    {
        // ⚠️ No es MonoBehaviour -> no SerializeField aquí.
        private const bool DebugLogs = true;

        // "Enganche" del ataque: rango mayor para iniciar windup aunque el player pase rápido
        private const float AttackStartRangeMultiplier = 1.35f;

        // Tiempo de compromiso: si entró en start-range hace poco, entramos a AttackState igualmente
        private const float AttackCommitTime = 0.35f;

        // Anti-spam: evita entrar en AttackState si estamos ya demasiado lejos
        private const float MaxStartRangeMultiplierClamp = 2.0f;

        private float lastTimeInAttackStartRange = -999f;

        // Reflection cache (para leer attackRangeX/Y aunque sean private en EnemySensors)
        private static FieldInfo _fiAttackRangeX;
        private static FieldInfo _fiAttackRangeY;
        private static bool _refCacheInit;

        public void Enter(EnemyBase enemy)
        {
            lastTimeInAttackStartRange = -999f;
        }

        public void Tick(EnemyBase enemy)
        {
            if (enemy.Player == null)
            {
                enemy.SetState(new EnemyReturnToPatrolState());
                return;
            }

            // ✅ 1) Si NO hay LOS, NO atacamos por proximidad.
            //    Pasamos a Search si hay memoria, o volvemos a Patrol si no la hay.
            if (!enemy.CanSeePlayer())
            {
                if (enemy.HasTargetInMemory)
                {
                    enemy.SetState(new EnemySearchState());
                    return;
                }

                enemy.SetState(new EnemyReturnToPatrolState());
                return;
            }

            // ✅ 2) A partir de aquí HAY LOS. Ahora sí: rango real de ataque.
            if (enemy.IsPlayerInAttackRange())
            {
                if (DebugLogs) Debug.Log($"[ENEMY CHASE] {enemy.name} -> Attack (HIT range + LOS)");
                enemy.SetState(new EnemyAttackState());
                return;
            }

            // ✅ 3) Start-range + commit time (enganche), pero SOLO si hay LOS.
            if (IsPlayerInAttackStartRange(enemy))
                lastTimeInAttackStartRange = Time.time;

            bool committed = (Time.time - lastTimeInAttackStartRange) <= AttackCommitTime;
            if (committed)
            {
                if (DebugLogs)
                    Debug.Log($"[ENEMY CHASE] {enemy.name} -> Attack (COMMIT + LOS t={(Time.time - lastTimeInAttackStartRange):0.00}s)");
                enemy.SetState(new EnemyAttackState());
                return;
            }
        }

        public void FixedTick(EnemyBase enemy)
        {
            if (enemy.Player == null)
            {
                enemy.StopSmooth(30f);
                return;
            }

            Vector2 target = enemy.Player.position;
            enemy.MoveTowards(target, enemy.ChaseSpeed, enemy.ChaseAcceleration);
        }

        public void Exit(EnemyBase enemy) { }

        // -------------------------
        // Helpers
        // -------------------------
        private static bool IsPlayerInAttackStartRange(EnemyBase enemy)
        {
            var sensors = enemy.Sensors;
            if (sensors == null || enemy.Player == null) return false;

            float baseX = GetSensorsAttackRangeX(sensors);
            float baseY = GetSensorsAttackRangeY(sensors);

            // Si no pudimos leer ranges (por lo que sea), no “enganchamos”
            if (baseX <= 0f || baseY <= 0f) return false;

            float mult = Mathf.Clamp(AttackStartRangeMultiplier, 1f, MaxStartRangeMultiplierClamp);
            float startX = baseX * mult;
            float startY = baseY * mult;

            // Usamos centros de colliders para que "encima" sea estable
            Vector2 enemyCenter = GetCenter(enemy.gameObject, enemy.RB.position);
            Vector2 playerCenter = GetCenter(enemy.Player.gameObject, (Vector2)enemy.Player.position);
            Vector2 d = playerCenter - enemyCenter;

            return Mathf.Abs(d.x) <= startX && Mathf.Abs(d.y) <= startY;
        }

        private static Vector2 GetCenter(GameObject go, Vector2 fallback)
        {
            var col = go.GetComponent<Collider2D>() ?? go.GetComponentInChildren<Collider2D>(true);
            return (col != null) ? (Vector2)col.bounds.center : fallback;
        }

        private static void InitRefCache()
        {
            if (_refCacheInit) return;
            _refCacheInit = true;

            var t = typeof(Game.Enemies.EnemySensors);
            _fiAttackRangeX = t.GetField("attackRangeX", BindingFlags.Instance | BindingFlags.NonPublic);
            _fiAttackRangeY = t.GetField("attackRangeY", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private static float GetSensorsAttackRangeX(Game.Enemies.EnemySensors sensors)
        {
            InitRefCache();
            if (_fiAttackRangeX == null) return 0f;
            object v = _fiAttackRangeX.GetValue(sensors);
            return v is float f ? f : 0f;
        }

        private static float GetSensorsAttackRangeY(Game.Enemies.EnemySensors sensors)
        {
            InitRefCache();
            if (_fiAttackRangeY == null) return 0f;
            object v = _fiAttackRangeY.GetValue(sensors);
            return v is float f ? f : 0f;
        }
    }
}

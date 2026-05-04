using UnityEngine;

namespace Game.Enemies.States
{
    public class EnemyChaseState : Game.Enemies.IEnemyState
    {
        private const bool DebugLogs = false;
        private const float AttackStartRangeMultiplier = 1.35f;
        private const float AttackCommitTime = 0.35f;
        private const float MaxStartRangeMultiplierClamp = 2.0f;

        private float lastTimeInAttackStartRange = -999f;

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

            if (!enemy.CanSeePlayer())
            {
                enemy.SetState(enemy.HasTargetInMemory ? new EnemySearchState() : new EnemyReturnToPatrolState());
                return;
            }

            enemy.SetFacingTowards(enemy.Player.position);

            if (enemy.IsPlayerInAttackRange())
            {
                if (DebugLogs) Debug.Log($"[ENEMY CHASE] {enemy.name} -> Attack");
                enemy.SetState(new EnemyAttackState());
                return;
            }

            if (IsPlayerInAttackStartRange(enemy))
                lastTimeInAttackStartRange = Time.time;

            if ((Time.time - lastTimeInAttackStartRange) <= AttackCommitTime)
            {
                enemy.SetState(new EnemyAttackState());
            }
        }

        public void FixedTick(EnemyBase enemy)
        {
            if (enemy.Player == null)
            {
                enemy.StopSmooth(30f);
                return;
            }

            enemy.MoveTowards(enemy.Player.position, enemy.ChaseSpeed, enemy.ChaseAcceleration);
        }

        public void Exit(EnemyBase enemy) { }

        private static bool IsPlayerInAttackStartRange(EnemyBase enemy)
        {
            if (enemy.Sensors == null || enemy.Player == null) return false;

            float mult = Mathf.Clamp(AttackStartRangeMultiplier, 1f, MaxStartRangeMultiplierClamp);
            float startX = enemy.Sensors.AttackRangeX * mult;
            float startY = enemy.Sensors.AttackRangeY * mult;

            Vector2 enemyCenter = GetCenter(enemy.gameObject, enemy.RB.position);
            Vector2 playerCenter = GetCenter(enemy.Player.gameObject, enemy.Player.position);
            Vector2 d = playerCenter - enemyCenter;

            return Mathf.Abs(d.x) <= startX && Mathf.Abs(d.y) <= startY;
        }

        private static Vector2 GetCenter(GameObject go, Vector2 fallback)
        {
            var col = go.GetComponent<Collider2D>() ?? go.GetComponentInChildren<Collider2D>(true);
            return col != null ? (Vector2)col.bounds.center : fallback;
        }
    }
}
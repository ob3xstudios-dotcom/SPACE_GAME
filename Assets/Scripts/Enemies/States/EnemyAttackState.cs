using UnityEngine;

namespace Game.Enemies.States
{
    public class EnemyAttackState : Game.Enemies.IEnemyState
    {
        private enum Phase { Windup, Recover }
        private static readonly bool DebugLogs = false;

        private const float WindupSeconds = 0.22f;
        private const float RecoverSeconds = 0.10f;

        private const float StopDecel = 45f;
        private const bool LockTargetDuringWindup = true;

        private const float VerticalBias = 0.55f;
        private const float ForceVerticalAbsY = 0.05f;

        private Phase phase;
        private float timer;

        public void Enter(EnemyBase enemy)
        {
            phase = Phase.Windup;
            timer = WindupSeconds;

            // ✅ Parryable durante windup
            enemy.SetParryable(true);

            enemy.StopSmooth(StopDecel);

            if (DebugLogs)
                Debug.Log($"[ENEMY ATTACK STATE] ENTER {enemy.name} windup={WindupSeconds:0.00}");
        }

        public void Tick(EnemyBase enemy)
        {
            if (enemy.Player == null)
            {
                enemy.SetParryable(false);
                enemy.SetState(new EnemyReturnToPatrolState());
                return;
            }

            if (!(LockTargetDuringWindup && phase == Phase.Windup))
            {
                if (!enemy.IsPlayerInAttackRange())
                {
                    enemy.SetParryable(false);

                    if (!enemy.CanSeePlayer() && enemy.HasTargetInMemory)
                        enemy.SetState(new EnemySearchState());
                    else
                        enemy.SetState(new EnemyChaseState());

                    return;
                }
            }

            timer -= Time.deltaTime;

            if (phase == Phase.Windup)
            {
                if (timer > 0f) return;

                // ✅ justo al golpear ya NO es parryable
                enemy.SetParryable(false);

                Vector2 enemyCenter = GetCenter(enemy.gameObject, enemy.RB.position);
                Vector2 playerCenter = GetCenter(enemy.Player.gameObject, (Vector2)enemy.Player.position);
                Vector2 raw = playerCenter - enemyCenter;

                if (enemy.MeleeDriver != null)
                {
                    enemy.MeleeDriver.BeginAttack(raw);
                }
                else if (enemy.Melee != null && enemy.Melee.CanAttack)
                {
                    enemy.Melee.TryAttack(enemy.RB.position, SnapDir(raw));
                }

                phase = Phase.Recover;
                timer = RecoverSeconds;

                if (DebugLogs)
                    Debug.Log($"[ENEMY ATTACK STATE] {enemy.name} -> Recover ({RecoverSeconds:0.00}s)");
            }
            else
            {
                if (timer > 0f) return;
                enemy.SetState(new EnemyChaseState());
            }
        }

        public void FixedTick(EnemyBase enemy)
        {
            if (phase == Phase.Windup)
                enemy.StopSmooth(StopDecel);
        }

        public void Exit(EnemyBase enemy)
        {
            enemy.SetParryable(false);

            if (DebugLogs)
                Debug.Log($"[ENEMY ATTACK STATE] EXIT {enemy.name}");
        }

        // ✅ Llamada por PlayerParry: cancela si está parryable
        public static bool TryParryEnemy(EnemyBase enemy)
        {
            if (enemy == null) return false;
            if (!enemy.IsParryable) return false;

            enemy.SetParryable(false);
            enemy.SetState(new EnemyChaseState());
            return true;
        }

        private static Vector2 GetCenter(GameObject go, Vector2 fallback)
        {
            var col = go.GetComponent<Collider2D>() ?? go.GetComponentInChildren<Collider2D>(true);
            return (col != null) ? (Vector2)col.bounds.center : fallback;
        }

        private static Vector2 SnapDir(Vector2 raw)
        {
            if (raw.sqrMagnitude < 0.0001f) return Vector2.right;

            float ax = Mathf.Abs(raw.x);
            float ay = Mathf.Abs(raw.y);

            bool forceVertical = ay >= ForceVerticalAbsY;
            bool chooseVertical = forceVertical || (ay >= ax * VerticalBias);

            if (chooseVertical)
                return new Vector2(0f, Mathf.Sign(raw.y == 0f ? 1f : raw.y));

            return new Vector2(Mathf.Sign(raw.x == 0f ? 1f : raw.x), 0f);
        }
    }
}

using UnityEngine;

namespace Game.Enemies.States
{
    public class EnemyAttackState : Game.Enemies.IEnemyState
    {
        private enum Phase { Windup, Recover }

        private const float WindupSeconds = 0.22f;
        private const float RecoverSeconds = 0.35f;
        private const float StopDecel = 45f;
        private const bool LockTargetDuringWindup = true;
        private const float VerticalBias = 0.55f;
        private const float ForceVerticalAbsY = 0.05f;

        private Phase phase;
        private float timer;
        private int attackDirX;
        private float advanceMoved;
        private Vector2 lastAdvancePos;
        private Vector2 lockedRawDir;

        public void Enter(EnemyBase enemy)
        {
            phase = Phase.Windup;
            timer = WindupSeconds;
            advanceMoved = 0f;
            lastAdvancePos = enemy.RB.position;
            enemy.SetParryable(true);
            enemy.StopSmooth(StopDecel);

            if (enemy.Player != null)
            {
                enemy.SetFacingTowards(enemy.Player.position);

                Vector2 enemyCenter = GetCenter(enemy.gameObject, enemy.RB.position);
                Vector2 playerCenter = GetCenter(enemy.Player.gameObject, enemy.Player.position);
                lockedRawDir = playerCenter - enemyCenter;
            }
            else
            {
                lockedRawDir = enemy.Forward;
            }

            Vector2 snapped = SnapDir(lockedRawDir);
            attackDirX = snapped.x < 0f ? -1 : 1;

            if (enemy.MeleeDriver != null)
                enemy.MeleeDriver.BeginAttack(lockedRawDir);
            else
                enemy.Melee?.BeginAttack(snapped);
        }

        public void Tick(EnemyBase enemy)
        {
            if (enemy.Player == null)
            {
                enemy.SetParryable(false);
                enemy.SetState(enemy.HasPatrolPoints ? new EnemyPatrolState() : new EnemyIdleState());
                return;
            }

            if (phase == Phase.Windup)
                enemy.SetFacingTowards(enemy.Player.position);

            if (!(LockTargetDuringWindup && phase == Phase.Windup))
            {
                if (!enemy.IsPlayerInAttackRange())
                {
                    enemy.SetParryable(false);
                    enemy.SetState(enemy.CanSeePlayer() || enemy.HasTargetInMemory ? new EnemyChaseState() : new EnemySearchState());
                    return;
                }
            }

            timer -= Time.deltaTime;

            if (phase == Phase.Windup)
            {
                if (timer > 0f) return;

                enemy.SetParryable(false);

                if (enemy.MeleeDriver == null && enemy.Melee != null && enemy.Melee.CanAttack)
                {
                    enemy.Melee.ActivateHitbox();
                }

                phase = Phase.Recover;
                timer = RecoverSeconds;
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
            {
                AdvanceDuringWindup(enemy);
                return;
            }

            enemy.StopSmooth(StopDecel);
        }

        public void Exit(EnemyBase enemy)
        {
            enemy.SetParryable(false);
            enemy.Melee?.DisableHitbox();
        }

        private void AdvanceDuringWindup(EnemyBase enemy)
        {
            if (enemy.AttackAdvanceDistance <= 0f)
            {
                enemy.StopSmooth(StopDecel);
                return;
            }

            if (advanceMoved >= enemy.AttackAdvanceDistance)
            {
                enemy.StopSmooth(StopDecel);
                return;
            }

            if (!enemy.CanMoveHorizontally(attackDirX))
            {
                enemy.StopSmooth(StopDecel);
                return;
            }

            bool didMove = enemy.MoveHorizontallyInDirection(attackDirX, enemy.AttackAdvanceSpeed, enemy.AttackAdvanceAcceleration);
            if (!didMove) return;

            Vector2 current = enemy.RB.position;
            advanceMoved += Mathf.Abs(current.x - lastAdvancePos.x);
            lastAdvancePos = current;
        }

        public static bool TryParryEnemy(EnemyBase enemy)
        {
            if (enemy == null || !enemy.IsParryable) return false;

            enemy.SetParryable(false);
            enemy.MeleeDriver?.TriggerParried();
            enemy.SetState(new EnemyChaseState());
            return true;
        }

        private static Vector2 GetCenter(GameObject go, Vector2 fallback)
        {
            var col = go.GetComponent<Collider2D>() ?? go.GetComponentInChildren<Collider2D>(true);
            return col != null ? (Vector2)col.bounds.center : fallback;
        }

        private static Vector2 SnapDir(Vector2 raw)
        {
            if (raw.sqrMagnitude < 0.0001f) return Vector2.right;

            float ax = Mathf.Abs(raw.x);
            float ay = Mathf.Abs(raw.y);

            bool chooseVertical = ay >= ForceVerticalAbsY || ay >= ax * VerticalBias;

            if (chooseVertical)
                return new Vector2(0f, Mathf.Sign(raw.y == 0f ? 1f : raw.y));

            return new Vector2(Mathf.Sign(raw.x == 0f ? 1f : raw.x), 0f);
        }
    }
}

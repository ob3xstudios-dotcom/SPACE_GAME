using UnityEngine;

namespace Game.Enemies.States
{
    public class EnemyAttackSpacingState : Game.Enemies.IEnemyState
    {
        private const float ArriveSlack = 0.03f;
        private const float MaxSpacingSeconds = 0.9f;

        private int backstepDir;
        private float moved;
        private float timer;
        private Vector2 lastPos;

        public void Enter(EnemyBase enemy)
        {
            enemy.SetParryable(false);
            enemy.Melee?.DisableHitbox();

            int toPlayer = enemy.DirectionToPlayerX();
            backstepDir = -toPlayer;
            moved = 0f;
            timer = 0f;
            lastPos = enemy.RB.position;

            if (enemy.Player != null)
                enemy.SetFacingTowards(enemy.Player.position);
            else
                enemy.SetFacingTowards(enemy.RB.position + enemy.Forward);
        }

        public void Tick(EnemyBase enemy)
        {
            timer += Time.deltaTime;

            if (enemy.Player == null)
            {
                enemy.SetState(new EnemySearchState());
                return;
            }

            enemy.SetFacingTowards(enemy.Player.position);

            if (!enemy.IsPlayerInAttackRange())
            {
                enemy.SetState(enemy.CanSeePlayer() || enemy.HasTargetInMemory ? new EnemyChaseState() : new EnemySearchState());
                return;
            }

            if (enemy.HasAttackSpacingToPlayer())
            {
                enemy.SetState(new EnemyAttackState());
                return;
            }

            bool timedOut = timer >= MaxSpacingSeconds;
            bool blocked = !enemy.CanMoveHorizontally(backstepDir);
            bool movedEnough = enemy.AttackBackstepDistance > 0f && moved >= enemy.AttackBackstepDistance - ArriveSlack;

            if (timedOut || blocked || movedEnough)
            {
                enemy.StopSmooth(35f);
                enemy.SetState(enemy.CanSeePlayer() || enemy.HasTargetInMemory ? new EnemyChaseState() : new EnemySearchState());
            }
        }

        public void FixedTick(EnemyBase enemy)
        {
            if (enemy.Player == null)
            {
                enemy.StopSmooth(30f);
                return;
            }

            if (enemy.HasAttackSpacingToPlayer())
            {
                enemy.StopSmooth(35f);
                return;
            }

            if (enemy.AttackBackstepDistance <= 0f || moved >= enemy.AttackBackstepDistance || !enemy.CanMoveHorizontally(backstepDir))
            {
                enemy.StopSmooth(35f);
                return;
            }

            bool didMove = enemy.MoveHorizontallyInDirection(backstepDir, enemy.AttackSpacingSpeed, enemy.AttackSpacingAcceleration);
            if (!didMove) return;

            Vector2 current = enemy.RB.position;
            moved += Mathf.Abs(current.x - lastPos.x);
            lastPos = current;
        }

        public void Exit(EnemyBase enemy)
        {
            enemy.StopSmooth(35f);
        }
    }
}

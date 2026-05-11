using UnityEngine;

namespace Game.Enemies.States
{
    public class EnemyPatrolState : Game.Enemies.IEnemyState
    {
        private Vector2 currentTarget;
        private bool goingToB;
        private float waitTimer;

        private const float ArriveDist = 0.8f;

        public void Enter(EnemyBase enemy)
        {
            if (!enemy.HasPatrolPoints)
            {
                enemy.SetState(new EnemyIdleState());
                return;
            }

            Vector2 a = enemy.PatrolA;
            Vector2 b = enemy.PatrolB;
            float distToA = Mathf.Abs(enemy.RB.position.x - a.x);
            float distToB = Mathf.Abs(enemy.RB.position.x - b.x);

            goingToB = distToA <= distToB;
            currentTarget = goingToB ? b : a;
            waitTimer = 0f;
        }

        public void Tick(EnemyBase enemy)
        {
            if (waitTimer > 0f)
            {
                waitTimer -= Time.deltaTime;
                return;
            }
        }

        public void FixedTick(EnemyBase enemy)
        {
            if (waitTimer > 0f)
            {
                enemy.StopSmooth(25f);
                return;
            }

            float dist = Vector2.Distance(enemy.RB.position, currentTarget);
            if (dist <= ArriveDist)
            {
                waitTimer = enemy.PatrolWaitSeconds;
                goingToB = !goingToB;
                currentTarget = goingToB ? enemy.PatrolB : enemy.PatrolA;
                enemy.StopSmooth(30f);
                return;
            }

            if (enemy.TryStartNavigationJump(currentTarget, this))
                return;

            if (enemy.RequiresNavigationJump(currentTarget))
            {
                waitTimer = enemy.PatrolWaitSeconds;
                goingToB = !goingToB;
                currentTarget = goingToB ? enemy.PatrolB : enemy.PatrolA;
                enemy.StopSmooth(30f);
                return;
            }

            enemy.MoveTowards(currentTarget, enemy.PatrolSpeed, enemy.PatrolAcceleration);
        }

        public void Exit(EnemyBase enemy) { }
    }
}

using UnityEngine;

namespace Game.Enemies.States
{
    public class EnemyPatrolState : Game.Enemies.IEnemyState
    {
        private Vector2 currentTarget;
        private bool goingToB;
        private float waitTimer;

        private const float ArriveDist = 0.2f;

        public void Enter(EnemyBase enemy)
        {
            Vector2 a = enemy.PatrolA;
            Vector2 b = enemy.PatrolB;

            float da = Vector2.Distance(enemy.RB.position, a);
            float db = Vector2.Distance(enemy.RB.position, b);

            currentTarget = (da <= db) ? a : b;
            goingToB = (currentTarget == b);

            waitTimer = 0f;
        }

        public void Tick(EnemyBase enemy)
        {
            if (enemy.CanSeePlayer())
            {
                enemy.SetState(new EnemyChaseState());
                return;
            }

            if (waitTimer > 0f)
                waitTimer -= Time.deltaTime;
        }

        public void FixedTick(EnemyBase enemy)
        {
            if (waitTimer > 0f)
            {
                enemy.StopSmooth(25f);
                return;
            }

            enemy.MoveTowards(currentTarget, enemy.PatrolSpeed, enemy.PatrolAcceleration);

            if (enemy.IsAt(currentTarget, ArriveDist))
            {
                enemy.StopSmooth(30f);
                waitTimer = enemy.PatrolWaitSeconds;

                Vector2 a = enemy.PatrolA;
                Vector2 b = enemy.PatrolB;

                if (enemy.PatrolLoopAtoB)
                {
                    currentTarget = (currentTarget == a) ? b : a;
                }
                else
                {
                    if (goingToB)
                    {
                        currentTarget = a;
                        goingToB = false;
                    }
                    else
                    {
                        currentTarget = b;
                        goingToB = true;
                    }
                }
            }
        }

        public void Exit(EnemyBase enemy) { }
    }
}
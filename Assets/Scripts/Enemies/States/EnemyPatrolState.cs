using UnityEngine;

namespace Game.Enemies.States
{
    public class EnemyPatrolState : Game.Enemies.IEnemyState
    {
        private Vector2 currentTarget;
        private bool goingToB;
        private float waitTimer;

        // Patrol solo X: umbral en X para “llegar”
        private const float ArriveX = 0.20f;

        public void Enter(EnemyBase enemy)
        {
            Vector2 a = enemy.PatrolA;
            Vector2 b = enemy.PatrolB;

            // Elegir el más cercano por X (porque patrol es X)
            float da = Mathf.Abs(enemy.RB.position.x - a.x);
            float db = Mathf.Abs(enemy.RB.position.x - b.x);

            currentTarget = (da <= db) ? a : b;
            goingToB = (currentTarget == b);

            waitTimer = 0f;
        }

        public void Tick(EnemyBase enemy)
        {
            // Si ve al player -> chase
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
                // Evitar drift en Y durante patrol
                enemy.RB.velocity = new Vector2(enemy.RB.velocity.x, 0f);
                return;
            }

            // ✅ Mover hacia el target pero SOLO en X (mantén Y)
            Vector2 target = new Vector2(currentTarget.x, enemy.RB.position.y);
            enemy.MoveTowards(target, enemy.PatrolSpeed, enemy.PatrolAcceleration);

            // ✅ Bloquea Y para que no derive
            enemy.RB.velocity = new Vector2(enemy.RB.velocity.x, 0f);

            // ✅ Llegada SOLO en X
            if (Mathf.Abs(enemy.RB.position.x - currentTarget.x) <= ArriveX)
            {
                enemy.StopSmooth(35f);
                waitTimer = enemy.PatrolWaitSeconds;

                Vector2 a = enemy.PatrolA;
                Vector2 b = enemy.PatrolB;

                if (enemy.PatrolLoopAtoB)
                {
                    // A->B->A->B
                    currentTarget = (currentTarget == a) ? b : a;
                    goingToB = (currentTarget == b);
                }
                else
                {
                    // Ping-pong
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

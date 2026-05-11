using UnityEngine;

namespace Game.Enemies.States
{
    public class EnemyReturnToPatrolState : Game.Enemies.IEnemyState
    {
        private Vector2 returnTarget;

        public void Enter(EnemyBase enemy)
        {
            if (!enemy.HasPatrolPoints)
                return;

            Vector2 a = enemy.PatrolA;
            Vector2 b = enemy.PatrolB;
            returnTarget = Vector2.Distance(enemy.RB.position, a) <= Vector2.Distance(enemy.RB.position, b) ? a : b;
        }

        public void Tick(EnemyBase enemy)
        {
            if (!enemy.HasPatrolPoints)
            {
                enemy.SetState(new EnemyIdleState());
                return;
            }
        }

        public void FixedTick(EnemyBase enemy)
        {
            if (!enemy.HasPatrolPoints)
            {
                enemy.StopSmooth(30f);
                return;
            }

            enemy.MoveTowards(returnTarget, enemy.ReturnSpeed, enemy.ReturnAcceleration);

            if (enemy.IsAt(returnTarget, enemy.ReturnArriveDistance))
            {
                enemy.SetState(new EnemyPatrolState());
                return;
            }
        }

        public void Exit(EnemyBase enemy) { }
    }
}

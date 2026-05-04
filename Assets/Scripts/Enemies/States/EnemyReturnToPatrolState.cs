using UnityEngine;

namespace Game.Enemies.States
{
    public class EnemyReturnToPatrolState : Game.Enemies.IEnemyState
    {
        public void Enter(EnemyBase enemy) { }

        public void Tick(EnemyBase enemy)
        {
            if (enemy.CanSeePlayer())
            {
                enemy.SetState(new EnemyChaseState());
            }
        }

        public void FixedTick(EnemyBase enemy)
        {
            Vector2 target = enemy.PatrolCenter;

            enemy.MoveTowards(target, enemy.ReturnSpeed, enemy.ReturnAcceleration);

            if (enemy.IsAt(target, enemy.ReturnArriveDistance))
            {
                enemy.SetState(new EnemyPatrolState());
            }
        }

        public void Exit(EnemyBase enemy) { }
    }
}
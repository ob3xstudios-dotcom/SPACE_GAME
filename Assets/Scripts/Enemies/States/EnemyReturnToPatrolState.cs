using UnityEngine;

namespace Game.Enemies.States
{
    public class EnemyReturnToPatrolState : Game.Enemies.IEnemyState
    {
        private const float ArriveX = 0.20f;

        public void Enter(EnemyBase enemy) { }

        public void Tick(EnemyBase enemy)
        {
            if (enemy.CanSeePlayer())
            {
                enemy.SetState(new EnemyChaseState());
                return;
            }
        }

        public void FixedTick(EnemyBase enemy)
        {
            Vector2 center = enemy.PatrolCenter;

            Vector2 target = new Vector2(center.x, enemy.RB.position.y);
            enemy.MoveTowards(target, enemy.ReturnSpeed, enemy.ReturnAcceleration);

            enemy.RB.velocity = new Vector2(enemy.RB.velocity.x, 0f);

            if (Mathf.Abs(enemy.RB.position.x - center.x) <= ArriveX)
            {
                enemy.StopSmooth(40f);
                enemy.SetState(new EnemyPatrolState());
            }
        }

        public void Exit(EnemyBase enemy) { }
    }
}

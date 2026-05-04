using UnityEngine;

namespace Game.Enemies.States
{
    public class EnemySearchState : Game.Enemies.IEnemyState
    {
        private float timer;
        private Vector2 searchTarget;

        public void Enter(EnemyBase enemy)
        {
            timer = enemy.SearchMaxSeconds;
            searchTarget = enemy.LastKnownPlayerPos;
        }

        public void Tick(EnemyBase enemy)
        {
            if (enemy.CanSeePlayer())
            {
                enemy.SetState(new EnemyChaseState());
                return;
            }

            searchTarget = enemy.LastKnownPlayerPos;

            timer -= Time.deltaTime;

            if (timer <= 0f)
            {
                enemy.SetState(new EnemyReturnToPatrolState());
                return;
            }

            if (enemy.IsPlayerInAttackRange() && enemy.HasTargetInMemory)
            {
                enemy.SetState(new EnemyAttackState());
            }
        }

        public void FixedTick(EnemyBase enemy)
        {
            enemy.MoveTowards(searchTarget, enemy.SearchSpeed, enemy.SearchAcceleration);

            if (enemy.IsAt(searchTarget, enemy.SearchArriveDistance))
            {
                enemy.StopSmooth(30f);
            }
        }

        public void Exit(EnemyBase enemy) { }
    }
}
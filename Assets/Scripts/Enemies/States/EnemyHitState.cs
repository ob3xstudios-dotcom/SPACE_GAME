using UnityEngine;

namespace Game.Enemies.States
{
    public class EnemyHitState : Game.Enemies.IEnemyState
    {
        private float timer = 0.2f;

        public void Enter(EnemyBase enemy)
        {
            enemy.StopSmooth(60f);
        }

        public void Tick(EnemyBase enemy)
        {
            timer -= Time.deltaTime;

            if (timer > 0f) return;

            if (enemy.CanSeePlayer() || enemy.HasTargetInMemory)
                enemy.SetState(new EnemyChaseState());
            else
                enemy.SetState(new EnemyPatrolState());
        }

        public void FixedTick(EnemyBase enemy)
        {
            enemy.StopSmooth(70f);
        }

        public void Exit(EnemyBase enemy) { }
    }
}
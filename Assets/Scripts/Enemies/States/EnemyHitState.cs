using UnityEngine;

namespace Game.Enemies.States
{
    public class EnemyHitState : Game.Enemies.IEnemyState
    {
        private float stunTime = 0.18f;
        private float t;

        public void Enter(EnemyBase enemy)
        {
            t = stunTime;
            enemy.StopSmooth(60f);
        }

        public void Tick(EnemyBase enemy)
        {
            t -= Time.deltaTime;
            if (t > 0f) return;

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

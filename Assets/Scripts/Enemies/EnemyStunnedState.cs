using UnityEngine;

namespace Game.Enemies.States
{
    public class EnemyStunnedState : Game.Enemies.IEnemyState
    {
        private readonly float duration;
        private readonly float decel;
        private float timer;

        public EnemyStunnedState(float seconds, float stopDecel = 70f)
        {
            duration = Mathf.Max(0f, seconds);
            decel = Mathf.Max(0f, stopDecel);
        }

        public void Enter(EnemyBase enemy)
        {
            timer = duration;
            enemy.SetParryable(false);
            enemy.StopSmooth(decel);
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
            enemy.StopSmooth(decel);
        }

        public void Exit(EnemyBase enemy) { }
    }
}
using UnityEngine;

namespace Game.Enemies.States
{
    public class EnemyJumpState : Game.Enemies.IEnemyState
    {
        private const float MinAirSeconds = 0.12f;
        private const float MaxJumpSeconds = 1.1f;

        private readonly IEnemyState returnState;
        private readonly int dir;
        private readonly float targetX;
        private float timer;
        private float lastX;
        private float blockedTimer;

        public EnemyJumpState(IEnemyState returnState, int dir, float targetX)
        {
            this.returnState = returnState;
            this.dir = dir < 0 ? -1 : 1;
            this.targetX = targetX;
        }

        public void Enter(EnemyBase enemy)
        {
            timer = 0f;
            blockedTimer = 0f;
            lastX = enemy.RB.position.x;
            enemy.SetParryable(false);
            enemy.RB.velocity = new Vector2(dir * enemy.PatrolSpeed, enemy.EnemyJumpForce);
        }

        public void Tick(EnemyBase enemy)
        {
            timer += Time.deltaTime;

            bool canLand = timer >= MinAirSeconds && enemy.RB.velocity.y <= 0f && enemy.IsGroundedForJump();
            bool timedOut = timer >= MaxJumpSeconds;

            if (canLand)
            {
                Return(enemy);
                return;
            }

            if (timedOut)
                Fail(enemy);
        }

        public void FixedTick(EnemyBase enemy)
        {
            enemy.MoveHorizontallyTo(targetX, enemy.PatrolSpeed, enemy.PatrolAcceleration);

            float movedX = Mathf.Abs(enemy.RB.position.x - lastX);
            if (timer >= MinAirSeconds && movedX < 0.003f)
                blockedTimer += Time.fixedDeltaTime;
            else
                blockedTimer = 0f;

            lastX = enemy.RB.position.x;

            if (blockedTimer >= 0.18f)
                Fail(enemy);
        }

        public void Exit(EnemyBase enemy) { }

        private void Return(EnemyBase enemy)
        {
            enemy.SetState(returnState ?? (enemy.HasPatrolPoints ? new EnemyPatrolState() : new EnemyIdleState()));
        }

        private void Fail(EnemyBase enemy)
        {
            enemy.NotifyNavigationJumpFailed();
            Return(enemy);
        }
    }
}

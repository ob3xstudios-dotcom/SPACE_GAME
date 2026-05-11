using UnityEngine;

namespace Game.Enemies.States
{
    public class EnemySearchState : Game.Enemies.IEnemyState
    {
        private float timer;
        private Vector2 searchTarget;
        private bool arrived;

        public void Enter(EnemyBase enemy)
        {
            timer = enemy.SearchSeconds;
            enemy.CaptureSearchTargetFromLastKnown();
            searchTarget = enemy.SearchTargetPos;
            arrived = false;
        }

        public void Tick(EnemyBase enemy)
        {
            if (!arrived) return;

            timer -= Time.deltaTime;

            if (timer <= 0f)
            {
                enemy.SetState(enemy.HasPatrolPoints ? new EnemyPatrolState() : new EnemyIdleState());
                return;
            }
        }

        public void FixedTick(EnemyBase enemy)
        {
            if (!arrived)
            {
                float distX = Mathf.Abs(enemy.RB.position.x - searchTarget.x);

                if (distX <= enemy.SearchArriveDistance)
                {
                    arrived = true;
                    enemy.StopSmooth(30f);
                    return;
                }

                if (enemy.TryStartNavigationJump(searchTarget, this))
                    return;

                if (enemy.RequiresNavigationJump(searchTarget))
                {
                    arrived = true;
                    enemy.StopSmooth(30f);
                    return;
                }

                enemy.MoveHorizontallyTo(searchTarget.x, enemy.SearchSpeed, enemy.SearchAcceleration);
            }
        }

        public void Exit(EnemyBase enemy) { }
    }
}

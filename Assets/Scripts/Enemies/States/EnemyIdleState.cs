namespace Game.Enemies.States
{
    public class EnemyIdleState : Game.Enemies.IEnemyState
    {
        public void Enter(EnemyBase enemy) => enemy.StopSmooth(30f);

        public void Tick(EnemyBase enemy)
        {
            if (enemy.CanSeePlayer() || enemy.HasTargetInMemory)
                enemy.SetState(new EnemyChaseState());
        }

        public void FixedTick(EnemyBase enemy) => enemy.StopSmooth(30f);

        public void Exit(EnemyBase enemy) { }
    }
}

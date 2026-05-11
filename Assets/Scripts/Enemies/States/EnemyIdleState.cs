namespace Game.Enemies.States
{
    public class EnemyIdleState : Game.Enemies.IEnemyState
    {
        public void Enter(EnemyBase enemy) => enemy.StopSmooth(30f);

        public void Tick(EnemyBase enemy) { }

        public void FixedTick(EnemyBase enemy) => enemy.StopSmooth(30f);

        public void Exit(EnemyBase enemy) { }
    }
}

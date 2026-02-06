namespace Game.Enemies
{
    /// <summary>
    /// Interfaz base de estados del enemigo.
    /// </summary>
    public interface IEnemyState
    {
        void Enter(EnemyBase enemy);
        void Tick(EnemyBase enemy);
        void FixedTick(EnemyBase enemy);
        void Exit(EnemyBase enemy);
    }
}

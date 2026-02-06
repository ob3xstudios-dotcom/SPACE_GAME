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
            // Si vuelve a ver al player -> chase
            if (enemy.CanSeePlayer())
            {
                enemy.SetState(new EnemyChaseState());
                return;
            }

            // Actualiza target “rastro” si el sistema de memoria sigue refrescando
            // (si no, se queda con el último)
            searchTarget = enemy.LastKnownPlayerPos;

            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                enemy.SetState(new EnemyReturnToPatrolState());
                return;
            }

            // Si está en rango de ataque durante search (por ejemplo lo perdió por 1 frame),
            // puedes permitir ataque si quieres:
            if (enemy.IsPlayerInAttackRange() && enemy.HasTargetInMemory)
            {
                enemy.SetState(new EnemyAttackState());
                return;
            }
        }

        public void FixedTick(EnemyBase enemy)
        {
            // Mover hacia última posición conocida
            enemy.MoveTowards(searchTarget, enemy.SearchSpeed, enemy.SearchAcceleration);

            // Si llega al punto (y aún no ve al player), se queda “buscando” hasta que timer acabe
            if (enemy.IsAt(searchTarget, enemy.SearchArriveDistance))
            {
                enemy.StopSmooth(30f);
            }
        }

        public void Exit(EnemyBase enemy) { }
    }
}

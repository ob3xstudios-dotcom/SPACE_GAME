using UnityEngine;

namespace Game.Enemies.States
{
    public class EnemyDieState : Game.Enemies.IEnemyState
    {
        private float despawnDelay = 1.25f;
        private float t;

        public void Enter(EnemyBase enemy)
        {
            t = despawnDelay;

            enemy.StopSmooth(999f);

            // Opcional: desactivar colisión
            var col = enemy.GetComponent<Collider2D>();
            if (col != null) col.enabled = false;
        }

        public void Tick(EnemyBase enemy)
        {
            t -= Time.deltaTime;
            if (t <= 0f)
                GameObject.Destroy(enemy.gameObject);
        }

        public void FixedTick(EnemyBase enemy)
        {
            enemy.StopSmooth(999f);
        }

        public void Exit(EnemyBase enemy) { }
    }
}

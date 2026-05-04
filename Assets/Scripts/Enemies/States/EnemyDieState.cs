using UnityEngine;

namespace Game.Enemies.States
{
    public class EnemyDieState : Game.Enemies.IEnemyState
    {
        private float timer = 1.2f;

        public void Enter(EnemyBase enemy)
        {
            enemy.StopSmooth(999f);

            var col = enemy.GetComponent<Collider2D>();
            if (col != null) col.enabled = false;
        }

        public void Tick(EnemyBase enemy)
        {
            timer -= Time.deltaTime;

            if (timer <= 0f)
                GameObject.Destroy(enemy.gameObject);
        }

        public void FixedTick(EnemyBase enemy)
        {
            enemy.StopSmooth(999f);
        }

        public void Exit(EnemyBase enemy) { }
    }
}
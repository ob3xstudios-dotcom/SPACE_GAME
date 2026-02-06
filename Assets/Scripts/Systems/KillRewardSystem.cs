using UnityEngine;
using Game.Events;
using Game.Player;

namespace Game.Systems
{
    public class KillRewardSystem : MonoBehaviour
    {
        [SerializeField] private EnemyKilledEventChannelSO enemyKilledChannel;
        [SerializeField] private PlayerResources playerResources;
        [SerializeField, Min(0)] private int manaPerKill = 1;

        private void OnEnable()
        {
            if (enemyKilledChannel != null)
                enemyKilledChannel.OnEnemyKilled += OnEnemyKilled;
        }

        private void OnDisable()
        {
            if (enemyKilledChannel != null)
                enemyKilledChannel.OnEnemyKilled -= OnEnemyKilled;
        }

        private void OnEnemyKilled(GameObject enemy)
        {
            if (playerResources == null) return;
            playerResources.GainMana(manaPerKill);
        }
    }
}

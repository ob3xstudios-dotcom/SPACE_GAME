using System;
using UnityEngine;

namespace Game.Events
{
    [CreateAssetMenu(menuName = "Game/Events/Enemy Killed Channel")]
    public class EnemyKilledEventChannelSO : ScriptableObject
    {
        public event Action<GameObject> OnEnemyKilled;

        public void Raise(GameObject enemy)
        {
            OnEnemyKilled?.Invoke(enemy);
        }
    }
}

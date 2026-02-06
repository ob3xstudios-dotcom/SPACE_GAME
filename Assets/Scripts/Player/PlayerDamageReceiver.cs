using UnityEngine;
using Game.Combat;

namespace Game.Player
{
    public class PlayerDamageReceiver : MonoBehaviour, IDamageable
    {
        [SerializeField] private PlayerResources playerResources;
        [SerializeField] private bool debugLogs = true;

        private void Awake()
        {
            if (playerResources == null)
                playerResources = GetComponent<PlayerResources>();

            if (debugLogs)
                Debug.Log($"[PDR] Awake. resources={(playerResources != null ? "OK" : "NULL")}");
        }

        public void TakeDamage(int dmg, Vector2 sourcePosition)
        {
            if (debugLogs)
                Debug.Log($"[PDR] TakeDamage dmg={dmg} from={sourcePosition}");

            if (playerResources == null) return;

            playerResources.TakeDamage(dmg);
        }
    }
}

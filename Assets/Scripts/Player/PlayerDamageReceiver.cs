using UnityEngine;
using Game.Combat;

namespace Game.Player
{
    /// <summary>
    /// Compatibility wrapper for old scene/prefab references.
    /// PlayerHealth owns the real damage flow: i-frames, knockback, death and respawn.
    /// </summary>
    public class PlayerDamageReceiver : MonoBehaviour, IDamageable
    {
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private bool debugLogs = false;

        private void Awake()
        {
            ResolvePlayerHealth();

            if (debugLogs)
                Debug.Log($"[PDR] Awake. playerHealth={(playerHealth != null ? "OK" : "NULL")}");
        }

        public void TakeDamage(int dmg, Vector2 sourcePosition)
        {
            if (debugLogs)
                Debug.Log($"[PDR] Forward damage dmg={dmg} from={sourcePosition}");

            if (playerHealth == null)
                ResolvePlayerHealth();

            if (playerHealth == null)
            {
                if (debugLogs)
                    Debug.LogWarning("[PDR] No PlayerHealth found. Damage ignored.");
                return;
            }

            playerHealth.TakeDamage(dmg, sourcePosition);
        }

        private void ResolvePlayerHealth()
        {
            if (playerHealth != null) return;

            playerHealth =
                GetComponent<PlayerHealth>() ??
                GetComponentInParent<PlayerHealth>() ??
                GetComponentInChildren<PlayerHealth>(true);
        }
    }
}

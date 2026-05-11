using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Compatibility bridge. PlayerResources.Stamina is the source of truth.
    /// </summary>
    public class PlayerStamina : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private PlayerResources playerResources;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        public int CurrentStamina => playerResources != null ? playerResources.CurrentStamina : 0;
        public int MaxStamina => playerResources != null ? playerResources.MaxStamina : 0;

        public event System.Action<int, int> OnStaminaChanged;

        private void Awake()
        {
            if (playerResources == null)
                playerResources = GetComponent<PlayerResources>();

            if (debugLogs)
                Debug.Log($"[STAMINA BRIDGE] Init {CurrentStamina}/{MaxStamina}");
        }

        private void OnEnable()
        {
            if (playerResources != null)
            {
                playerResources.OnStaminaChanged += HandleStaminaChanged;
                FireChanged();
            }
        }

        private void OnDisable()
        {
            if (playerResources != null)
                playerResources.OnStaminaChanged -= HandleStaminaChanged;
        }

        public bool HasStamina(int amount = 1) => playerResources != null && playerResources.HasStamina(amount);

        public bool SpendStamina(int amount)
        {
            if (amount <= 0) return true;

            if (playerResources == null || !playerResources.SpendStamina(amount))
            {
                if (debugLogs)
                    Debug.Log($"[STAMINA BRIDGE] Not enough ({CurrentStamina}/{amount})");
                return false;
            }

            if (debugLogs)
                Debug.Log($"[STAMINA BRIDGE] -{amount} -> {CurrentStamina}");

            return true;
        }

        public void RestoreStamina(int amount)
        {
            if (amount <= 0) return;

            int before = CurrentStamina;
            playerResources?.RestoreStamina(amount);

            if (debugLogs)
                Debug.Log($"[STAMINA BRIDGE] +{amount} -> {before} -> {CurrentStamina}");
        }

        public void SetStamina(int value)
        {
            int before = CurrentStamina;
            playerResources?.SetStamina(value);

            if (debugLogs)
                Debug.Log($"[STAMINA BRIDGE] Set {before} -> {CurrentStamina}");
        }

        public void Refill() => playerResources?.RefillStamina();

        public void SetMaxStamina(int newMax, bool refill = true)
        {
            playerResources?.SetMaxStamina(newMax, refill);

            if (debugLogs)
                Debug.Log($"[STAMINA BRIDGE] SetMax {MaxStamina} | now {CurrentStamina}");
        }

        private void HandleStaminaChanged(int current, int max)
        {
            FireChanged();
        }

        private void FireChanged()
        {
            OnStaminaChanged?.Invoke(CurrentStamina, MaxStamina);
        }
    }
}

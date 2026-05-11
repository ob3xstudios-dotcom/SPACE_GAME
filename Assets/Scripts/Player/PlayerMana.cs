using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// Compatibility bridge. PlayerResources.Mana is the source of truth.
    /// Keep this component only while old prefabs/scenes still reference PlayerMana.
    /// </summary>
    public class PlayerMana : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private PlayerResources playerResources;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        public int CurrentMana => playerResources != null ? playerResources.CurrentMana : 0;
        public int MaxMana => playerResources != null ? playerResources.MaxMana : 0;

        public event System.Action<int, int> OnManaChanged;

        private void Awake()
        {
            if (playerResources == null)
                playerResources = GetComponent<PlayerResources>();

            if (debugLogs)
                Debug.Log($"[MANA BRIDGE] Init {CurrentMana}/{MaxMana}");
        }

        private void OnEnable()
        {
            if (playerResources != null && playerResources.Mana != null)
            {
                playerResources.Mana.OnChanged += HandleManaChanged;
                FireChanged();
            }
        }

        private void OnDisable()
        {
            if (playerResources != null && playerResources.Mana != null)
                playerResources.Mana.OnChanged -= HandleManaChanged;
        }

        public bool HasMana(int amount = 1) => playerResources != null && playerResources.HasMana(amount);

        public void AddMana(int amount)
        {
            if (amount <= 0) return;

            int before = CurrentMana;
            playerResources?.GainMana(amount);

            if (debugLogs)
                Debug.Log($"[MANA BRIDGE] +{amount} -> {before} -> {CurrentMana}");
        }

        public bool ConsumeMana(int amount)
        {
            if (amount <= 0) return true;

            if (playerResources == null || !playerResources.TrySpendMana(amount))
            {
                if (debugLogs)
                    Debug.Log($"[MANA BRIDGE] Not enough ({CurrentMana}/{amount})");
                return false;
            }

            if (debugLogs)
                Debug.Log($"[MANA BRIDGE] -{amount} -> {CurrentMana}");

            return true;
        }

        public void SetMana(int value)
        {
            int before = CurrentMana;
            playerResources?.SetMana(value);

            if (debugLogs)
                Debug.Log($"[MANA BRIDGE] Set {before} -> {CurrentMana}");
        }

        public void Refill() => playerResources?.RefillMana();

        public void SetMaxMana(int newMax, bool refill = true)
        {
            playerResources?.SetMaxMana(newMax, refill);

            if (debugLogs)
                Debug.Log($"[MANA BRIDGE] SetMax {MaxMana} | now {CurrentMana}");
        }

        private void HandleManaChanged(int current, int max)
        {
            FireChanged();
        }

        private void FireChanged()
        {
            OnManaChanged?.Invoke(CurrentMana, MaxMana);
        }
    }
}

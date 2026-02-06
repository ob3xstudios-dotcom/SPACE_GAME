using UnityEngine;

namespace Game.Player
{
    public class PlayerMana : MonoBehaviour
    {
        [Header("Mana")]
        [SerializeField, Min(0)] private int maxMana = 5;
        [SerializeField] private int startMana = 0;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        public int CurrentMana { get; private set; }
        public int MaxMana => maxMana;

        public bool HasMana(int amount = 1)
        {
            return CurrentMana >= amount;
        }

        private void Awake()
        {
            CurrentMana = Mathf.Clamp(startMana, 0, maxMana);
            if (debugLogs)
                Debug.Log($"[MANA] Init {CurrentMana}/{maxMana}");
        }

        public void AddMana(int amount)
        {
            if (amount <= 0) return;

            int before = CurrentMana;
            CurrentMana = Mathf.Clamp(CurrentMana + amount, 0, maxMana);

            if (debugLogs)
                Debug.Log($"[MANA] +{amount} → {before} -> {CurrentMana}");
        }

        public bool ConsumeMana(int amount)
        {
            if (amount <= 0) return true;
            if (CurrentMana < amount)
            {
                if (debugLogs)
                    Debug.Log($"[MANA] Not enough ({CurrentMana}/{amount})");
                return false;
            }

            CurrentMana -= amount;

            if (debugLogs)
                Debug.Log($"[MANA] -{amount} → now {CurrentMana}");

            return true;
        }

        public void SetMaxMana(int newMax, bool refill = true)
        {
            maxMana = Mathf.Max(0, newMax);
            if (refill)
                CurrentMana = maxMana;
            else
                CurrentMana = Mathf.Min(CurrentMana, maxMana);
        }
    }
}

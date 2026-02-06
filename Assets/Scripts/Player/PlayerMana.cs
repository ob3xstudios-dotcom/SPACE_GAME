using System;
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

        public event Action<int, int> OnManaChanged; // (current, max)

        private void Awake()
        {
            CurrentMana = Mathf.Clamp(startMana, 0, maxMana);
            FireChanged();

            if (debugLogs)
                Debug.Log($"[MANA] Init {CurrentMana}/{maxMana}");
        }

        public bool HasMana(int amount = 1) => CurrentMana >= amount;

        public void AddMana(int amount)
        {
            if (amount <= 0) return;

            int before = CurrentMana;
            CurrentMana = Mathf.Clamp(CurrentMana + amount, 0, maxMana);

            if (debugLogs)
                Debug.Log($"[MANA] +{amount} → {before} -> {CurrentMana}");

            if (CurrentMana != before)
                FireChanged();
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

            int before = CurrentMana;
            CurrentMana -= amount;

            if (debugLogs)
                Debug.Log($"[MANA] -{amount} → {before} -> {CurrentMana}");

            FireChanged();
            return true;
        }

        public void SetMana(int value)
        {
            int before = CurrentMana;
            CurrentMana = Mathf.Clamp(value, 0, maxMana);

            if (debugLogs)
                Debug.Log($"[MANA] Set {before} -> {CurrentMana}");

            if (CurrentMana != before)
                FireChanged();
        }

        public void Refill() => SetMana(maxMana);

        public void SetMaxMana(int newMax, bool refill = true)
        {
            maxMana = Mathf.Max(0, newMax);

            if (refill)
                CurrentMana = maxMana;
            else
                CurrentMana = Mathf.Min(CurrentMana, maxMana);

            if (debugLogs)
                Debug.Log($"[MANA] SetMax {maxMana} | now {CurrentMana}");

            FireChanged();
        }

        private void FireChanged()
        {
            OnManaChanged?.Invoke(CurrentMana, maxMana);
        }
    }
}

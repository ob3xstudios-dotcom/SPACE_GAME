using System;
using Game.Core;
using UnityEngine;

namespace Game.Player
{
    [DefaultExecutionOrder(-1000)]
    public class PlayerResources : MonoBehaviour
    {
        [Header("Health (Masks)")]
        [SerializeField, Min(1)] private int maxHealth = 5;

        [Header("Mana (Movement Abilities)")]
        [SerializeField, Min(0)] private int maxMana = 3;
        [SerializeField, Min(0)] private int startMana = 0;

        [Header("Stamina")]
        [SerializeField, Min(0)] private int maxStamina = 3;
        [SerializeField, Min(0)] private int startStamina = 3;

        public IntResource Health { get; private set; }
        public IntResource Mana { get; private set; }
        public IntResource Stamina { get; private set; }

        public event Action<int, int> OnStaminaChanged;

        private void Awake()
        {
            Health = new IntResource(maxHealth);
            Mana = new IntResource(maxMana, startMana);
            Stamina = new IntResource(maxStamina, startStamina);
            Stamina.OnChanged += HandleStaminaChanged;
        }

        private void OnDestroy()
        {
            if (Stamina != null)
                Stamina.OnChanged -= HandleStaminaChanged;
        }

        // --- HEALTH ---
        public void TakeDamage(int dmg)
        {
            if (dmg <= 0) return;

            int before = Health.Current;
            Health.SetCurrent(Health.Current - dmg);

            Debug.Log($"[PR] TakeDamage {dmg} HP {before}->{Health.Current}/{Health.Max}");
        }

        public void Heal(int amount)
        {
            if (amount <= 0) return;
            Health.Restore(amount);
        }

        // --- MANA ---
        public int CurrentMana => Mana != null ? Mana.Current : 0;
        public int MaxMana => Mana != null ? Mana.Max : maxMana;

        public bool HasMana(int amount = 1) => Mana != null && Mana.Current >= amount;
        public bool TrySpendMana(int cost) => Mana != null && Mana.Spend(cost);
        public void GainMana(int amount) => Mana?.Restore(amount);

        public void SetMana(int value)
        {
            Mana?.SetCurrent(value);
        }

        public void RefillMana()
        {
            Mana?.Fill();
        }

        public void SetMaxMana(int newMax, bool refill = true)
        {
            maxMana = Mathf.Max(0, newMax);

            if (Mana == null)
            {
                Mana = new IntResource(maxMana, refill ? maxMana : 0);
                return;
            }

            Mana.SetMax(maxMana);

            if (refill)
                Mana.Fill();
        }

        // --- STAMINA ---
        public int CurrentStamina => Stamina != null ? Stamina.Current : 0;
        public int MaxStamina => Stamina != null ? Stamina.Max : maxStamina;

        public bool HasStamina(int amount = 1) => Stamina != null && Stamina.Current >= amount;
        public bool SpendStamina(int amount) => Stamina != null && Stamina.Spend(amount);
        public void RestoreStamina(int amount) => Stamina?.Restore(amount);

        public void SetStamina(int value)
        {
            Stamina?.SetCurrent(value);
        }

        public void RefillStamina()
        {
            Stamina?.Fill();
        }

        public void SetMaxStamina(int newMax, bool refill = true)
        {
            maxStamina = Mathf.Max(0, newMax);

            if (Stamina == null)
            {
                Stamina = new IntResource(maxStamina, refill ? maxStamina : 0);
                Stamina.OnChanged += HandleStaminaChanged;
                OnStaminaChanged?.Invoke(Stamina.Current, Stamina.Max);
                return;
            }

            Stamina.SetMax(maxStamina);

            if (refill)
                Stamina.Fill();
        }

        private void HandleStaminaChanged(int current, int max)
        {
            OnStaminaChanged?.Invoke(current, max);
        }
    }
}

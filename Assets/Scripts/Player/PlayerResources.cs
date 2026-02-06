using UnityEngine;
using Game.Core;

namespace Game.Player
{
    [DefaultExecutionOrder(-1000)]
    public class PlayerResources : MonoBehaviour
    {
        [Header("Health (Masks)")]
        [SerializeField, Min(1)] private int maxHealth = 5;

        [Header("Mana (Movement Abilities)")]
        [SerializeField, Min(0)] private int maxMana = 3;

        public IntResource Health { get; private set; }
        public IntResource Mana { get; private set; }

        private void Awake()
        {
            Health = new IntResource(maxHealth);
            Mana = new IntResource(maxMana);
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
        public bool TrySpendMana(int cost) => Mana.Spend(cost);
        public void GainMana(int amount) => Mana.Restore(amount);
    }
}

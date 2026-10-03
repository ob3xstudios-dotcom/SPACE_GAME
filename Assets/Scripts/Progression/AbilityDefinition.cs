using UnityEngine;

namespace Game.Progression
{
    public static class ProgressionAbilityIds
    {
        public const string DoubleJump = "ability_double_jump";
        public const string UpdraftCape = "ability_updraft_cape";
    }

    public enum AbilityType
    {
        General,
        Movement,
        Defense,
        Combat,
        Companion
    }

    [CreateAssetMenu(fileName = "AbilityDefinition", menuName = "SpaceWorld/Progression/Ability Definition")]
    public sealed class AbilityDefinition : ScriptableObject
    {
        [SerializeField] private string abilityId = string.Empty;
        [SerializeField] private string displayName = "Ability";
        [TextArea(2, 6)]
        [SerializeField] private string description = string.Empty;
        [SerializeField] private Sprite icon;
        [SerializeField] private AbilityType abilityType = AbilityType.General;
        [Tooltip("Effects applied while equipped in any slot.")]
        [SerializeField] private PlayerStatModifier[] equippedModifiers = new PlayerStatModifier[0];
        [Tooltip("Additional penalties applied only while this ability occupies slot 4.")]
        [SerializeField] private PlayerStatModifier[] overloadPenalties = new PlayerStatModifier[0];

        public string AbilityId => ProgressionSaveData.NormalizeId(abilityId);
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public AbilityType AbilityType => abilityType;
        public PlayerStatModifier[] EquippedModifiers => equippedModifiers;
        public PlayerStatModifier[] OverloadPenalties => overloadPenalties;

#if UNITY_EDITOR
        private void OnValidate()
        {
            abilityId = ProgressionSaveData.NormalizeId(abilityId);
            displayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();
            if (equippedModifiers == null)
                equippedModifiers = new PlayerStatModifier[0];
            if (overloadPenalties == null)
                overloadPenalties = new PlayerStatModifier[0];

            ClampPercentages(equippedModifiers);
            ClampPercentages(overloadPenalties);
        }

        private static void ClampPercentages(PlayerStatModifier[] modifiers)
        {
            for (int i = 0; i < modifiers.Length; i++)
                modifiers[i].percentAmount = Mathf.Max(-0.95f, modifiers[i].percentAmount);
        }
#endif
    }
}

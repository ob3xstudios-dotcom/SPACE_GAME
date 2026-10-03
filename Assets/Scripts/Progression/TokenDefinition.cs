using UnityEngine;

namespace Game.Progression
{
    [CreateAssetMenu(fileName = "TokenDefinition", menuName = "SpaceWorld/Progression/Token Definition")]
    public sealed class TokenDefinition : ScriptableObject
    {
        [SerializeField] private string tokenTypeId = string.Empty;
        [SerializeField] private string displayName = "Token";
        [TextArea(2, 5)]
        [SerializeField] private string description = string.Empty;
        [SerializeField] private Sprite icon;
        [SerializeField] private PlayerStatModifier permanentModifier;

        public string TokenTypeId => ProgressionSaveData.NormalizeId(tokenTypeId);
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public PlayerStatModifier PermanentModifier => permanentModifier;

#if UNITY_EDITOR
        private void OnValidate()
        {
            tokenTypeId = ProgressionSaveData.NormalizeId(tokenTypeId);
            displayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();
            permanentModifier.percentAmount = Mathf.Max(-0.95f, permanentModifier.percentAmount);
        }
#endif
    }
}

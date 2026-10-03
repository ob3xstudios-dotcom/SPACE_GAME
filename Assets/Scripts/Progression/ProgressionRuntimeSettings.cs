using UnityEngine;

namespace Game.Progression
{
    [CreateAssetMenu(fileName = "ProgressionRuntimeSettings", menuName = "SpaceWorld/Progression/Runtime Settings")]
    public sealed class ProgressionRuntimeSettings : ScriptableObject
    {
        private const string ResourcePath = "ProgressionRuntimeSettings";

        [SerializeField] private TokenDefinition[] tokenDefinitions = new TokenDefinition[0];
        [SerializeField] private AbilityDefinition[] abilityDefinitions = new AbilityDefinition[0];

        public TokenDefinition[] TokenDefinitions => tokenDefinitions;
        public AbilityDefinition[] AbilityDefinitions => abilityDefinitions;

        public static ProgressionRuntimeSettings Load()
        {
            return Resources.Load<ProgressionRuntimeSettings>(ResourcePath);
        }
    }
}

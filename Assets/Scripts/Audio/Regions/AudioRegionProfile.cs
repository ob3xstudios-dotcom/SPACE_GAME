using UnityEngine;

namespace Game.Audio
{
    public enum MusicSituation
    {
        Exploration,
        Tension,
        Combat,
        Event
    }

    [CreateAssetMenu(
        fileName = "AudioRegion_New",
        menuName = "Space World/Audio/Audio Region Profile")]
    public sealed class AudioRegionProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string regionId;
        [SerializeField] private string displayName;

        [Header("Music")]
        [SerializeField] private AudioEventDefinition explorationMusic;
        [SerializeField] private AudioEventDefinition tensionMusic;
        [SerializeField] private AudioEventDefinition combatMusic;
        [SerializeField] private AudioEventDefinition eventMusic;
        [SerializeField] private bool fallbackToExploration = true;

        [Header("Transitions")]
        [SerializeField, Min(0f)] private float transitionSeconds = 2f;

        public string RegionId => regionId;
        public string DisplayName => displayName;
        public float TransitionSeconds => transitionSeconds;

        public AudioEventDefinition GetMusic(MusicSituation situation)
        {
            AudioEventDefinition selected;
            switch (situation)
            {
                case MusicSituation.Tension:
                    selected = tensionMusic;
                    break;
                case MusicSituation.Combat:
                    selected = combatMusic;
                    break;
                case MusicSituation.Event:
                    selected = eventMusic;
                    break;
                default:
                    selected = explorationMusic;
                    break;
            }

            if (selected != null || situation == MusicSituation.Exploration)
                return selected;

            return fallbackToExploration ? explorationMusic : null;
        }

        private void OnValidate()
        {
            regionId = regionId != null ? regionId.Trim() : string.Empty;
            displayName = displayName != null ? displayName.Trim() : string.Empty;
            transitionSeconds = Mathf.Max(0f, transitionSeconds);
        }
    }
}

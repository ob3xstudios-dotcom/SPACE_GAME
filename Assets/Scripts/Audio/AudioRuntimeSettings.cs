using UnityEngine;
using UnityEngine.Audio;

namespace Game.Audio
{
    [CreateAssetMenu(
        fileName = "AudioRuntimeSettings",
        menuName = "Space World/Audio/Runtime Settings")]
    public sealed class AudioRuntimeSettings : ScriptableObject
    {
        private const string ResourcePath = "Audio/AudioRuntimeSettings";
        private static AudioRuntimeSettings cached;

        [Header("Library")]
        [SerializeField] private AudioEventCatalog catalog;

        [Header("Mixer")]
        [SerializeField] private AudioMixer mixer;
        [SerializeField] private AudioMixerGroup masterGroup;
        [SerializeField] private AudioMixerGroup musicGroup;
        [SerializeField] private AudioMixerGroup ambienceGroup;
        [SerializeField] private AudioMixerGroup sfxGroup;
        [SerializeField] private AudioMixerGroup uiGroup;
        [SerializeField] private AudioMixerGroup voiceGroup;

        [Header("Voice pool")]
        [SerializeField, Min(0)] private int initialPoolSize = 12;
        [SerializeField, Min(1)] private int maximumVoiceCount = 24;

        [Header("Diagnostics")]
        [Tooltip("Logs each missing event or empty definition at most once per session.")]
        [SerializeField] private bool logMissingAudioOnce = true;

        public AudioEventCatalog Catalog => catalog;
        public AudioMixer Mixer => mixer;
        public int InitialPoolSize => initialPoolSize;
        public int MaximumVoiceCount => maximumVoiceCount;
        public bool LogMissingAudioOnce => logMissingAudioOnce;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            cached = null;
        }

        public static AudioRuntimeSettings Load()
        {
            if (cached == null)
                cached = Resources.Load<AudioRuntimeSettings>(ResourcePath);
            return cached;
        }

        public AudioMixerGroup GetGroup(AudioBus bus)
        {
            switch (bus)
            {
                case AudioBus.Music:
                    return musicGroup != null ? musicGroup : masterGroup;
                case AudioBus.Ambience:
                    return ambienceGroup != null ? ambienceGroup : masterGroup;
                case AudioBus.SFX:
                    return sfxGroup != null ? sfxGroup : masterGroup;
                case AudioBus.UI:
                    return uiGroup != null ? uiGroup : masterGroup;
                case AudioBus.Voice:
                    return voiceGroup != null ? voiceGroup : masterGroup;
                default:
                    return masterGroup;
            }
        }

        private void OnValidate()
        {
            maximumVoiceCount = Mathf.Max(1, maximumVoiceCount);
            initialPoolSize = Mathf.Clamp(initialPoolSize, 0, maximumVoiceCount);
        }
    }
}


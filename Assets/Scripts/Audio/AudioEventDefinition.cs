using UnityEngine;

namespace Game.Audio
{
    public enum AudioClipSelectionMode
    {
        RandomNoImmediateRepeat,
        Random,
        Sequential
    }

    public enum AudioConcurrencyMode
    {
        RejectNew,
        StopOldest
    }

    [CreateAssetMenu(
        fileName = "AudioEvent_New",
        menuName = "Space World/Audio/Audio Event Definition")]
    public sealed class AudioEventDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string eventId;
        [SerializeField] private AudioBus bus = AudioBus.SFX;

        [Header("Clips")]
        [SerializeField] private AudioClip[] clips = new AudioClip[0];
        [SerializeField] private AudioClipSelectionMode selectionMode =
            AudioClipSelectionMode.RandomNoImmediateRepeat;
        [SerializeField] private bool loop;

        [Header("Variation")]
        [SerializeField] private Vector2 volumeRange = Vector2.one;
        [SerializeField] private Vector2 pitchRange = Vector2.one;

        [Header("Voice control")]
        [SerializeField, Range(0, 256)] private int priority = 128;
        [SerializeField, Min(1)] private int maxConcurrentInstances = 4;
        [SerializeField, Min(0f)] private float cooldownSeconds;
        [Tooltip("Events with the same non-empty key share their concurrency limit.")]
        [SerializeField] private string concurrencyGroup;
        [SerializeField] private AudioConcurrencyMode concurrencyMode =
            AudioConcurrencyMode.RejectNew;

        [Header("Playback")]
        [SerializeField, Min(0f)] private float fadeInSeconds;
        [SerializeField] private bool ignoreListenerPause;

        [Header("2D spatialization")]
        [SerializeField, Range(0f, 1f)] private float spatialBlend;
        [SerializeField] private AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;
        [SerializeField, Min(0f)] private float minDistance = 1f;
        [SerializeField, Min(0.01f)] private float maxDistance = 25f;

        public string EventId => eventId;
        public AudioBus Bus => bus;
        public AudioClipSelectionMode SelectionMode => selectionMode;
        public bool Loop => loop;
        public Vector2 VolumeRange => volumeRange;
        public Vector2 PitchRange => pitchRange;
        public int Priority => priority;
        public int MaxConcurrentInstances => maxConcurrentInstances;
        public float CooldownSeconds => cooldownSeconds;
        public string ConcurrencyGroup => concurrencyGroup;
        public AudioConcurrencyMode ConcurrencyMode => concurrencyMode;
        public float FadeInSeconds => fadeInSeconds;
        public bool IgnoreListenerPause => ignoreListenerPause;
        public float SpatialBlend => spatialBlend;
        public AudioRolloffMode RolloffMode => rolloffMode;
        public float MinDistance => minDistance;
        public float MaxDistance => maxDistance;
        public int ClipCount => clips != null ? clips.Length : 0;

        public AudioClip GetClipAt(int index)
        {
            return clips != null && index >= 0 && index < clips.Length
                ? clips[index]
                : null;
        }

        public int GetValidClipCount()
        {
            if (clips == null)
                return 0;

            int count = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null)
                    count++;
            }

            return count;
        }

        public int GetValidClipIndex(int validOrdinal)
        {
            if (clips == null || validOrdinal < 0)
                return -1;

            int ordinal = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] == null)
                    continue;
                if (ordinal == validOrdinal)
                    return i;
                ordinal++;
            }

            return -1;
        }

        private void OnValidate()
        {
            eventId = eventId != null ? eventId.Trim() : string.Empty;
            concurrencyGroup = concurrencyGroup != null
                ? concurrencyGroup.Trim()
                : string.Empty;
            volumeRange = OrderedNonNegative(volumeRange);
            pitchRange = OrderedNonNegative(pitchRange);
            priority = Mathf.Clamp(priority, 0, 256);
            maxConcurrentInstances = Mathf.Max(1, maxConcurrentInstances);
            cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
            fadeInSeconds = Mathf.Max(0f, fadeInSeconds);
            minDistance = Mathf.Max(0f, minDistance);
            maxDistance = Mathf.Max(minDistance + 0.01f, maxDistance);
        }

        private static Vector2 OrderedNonNegative(Vector2 value)
        {
            float minimum = Mathf.Max(0f, Mathf.Min(value.x, value.y));
            float maximum = Mathf.Max(minimum, Mathf.Max(value.x, value.y));
            return new Vector2(minimum, maximum);
        }
    }
}


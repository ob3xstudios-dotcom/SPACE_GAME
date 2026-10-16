using System;
using System.Collections.Generic;
using Game.Map;
using UnityEngine;
using UnityEngine.Audio;

namespace Game.Audio
{
    [DefaultExecutionOrder(-11000)]
    public sealed class AudioService : MonoBehaviour
    {
        private const string ServiceObjectName = "[AudioService]";
        private const int DefaultInitialPoolSize = 12;
        private const int DefaultMaximumVoiceCount = 24;
        private const float MinimumDecibels = -80f;
        private const float AcousticTransitionSeconds = 0.12f;
        private const float AcousticSilenceThreshold = 0.001f;

        private static AudioService instance;
        private static bool isQuitting;

        private readonly List<Voice> activeVoices = new List<Voice>();
        private readonly Stack<AudioSource> availableSources = new Stack<AudioSource>();
        private readonly Dictionary<int, Voice> voicesByHandle = new Dictionary<int, Voice>();
        private readonly Dictionary<int, EventRuntimeState> eventStates =
            new Dictionary<int, EventRuntimeState>();
        private readonly HashSet<int> warnedEmptyDefinitions = new HashSet<int>();
        private readonly HashSet<string> warnedUnknownIds =
            new HashSet<string>(StringComparer.Ordinal);

        private AudioRuntimeSettings settings;
        private int sourceCount;
        private int nextHandleId;
        private bool warnedVoiceLimit;

        public static AudioService Instance
        {
            get
            {
                EnsureInstance();
                return instance;
            }
        }

        public static bool HasInstance => instance != null;
        public AudioEventCatalog Catalog => settings != null ? settings.Catalog : null;
        public AudioMixer Mixer => settings != null ? settings.Mixer : null;
        public int ActiveVoiceCount => activeVoices.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            isQuitting = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            EnsureInstance();
        }

        public static AudioPlaybackHandle Play(AudioEventDefinition definition)
        {
            AudioService service = Instance;
            return service != null
                ? service.PlayInternal(definition, Vector3.zero, null, false, -1f, false)
                : default;
        }

        public static AudioPlaybackHandle Play(string eventId)
        {
            AudioService service = Instance;
            return service != null && service.TryResolve(eventId, out AudioEventDefinition definition)
                ? service.PlayInternal(definition, Vector3.zero, null, false, -1f, false)
                : default;
        }

        public static AudioPlaybackHandle PlayAt(
            AudioEventDefinition definition,
            Vector3 position)
        {
            AudioService service = Instance;
            return service != null
                ? service.PlayInternal(definition, position, null, false, -1f, false)
                : default;
        }

        public static AudioPlaybackHandle PlayAt(string eventId, Vector3 position)
        {
            AudioService service = Instance;
            return service != null && service.TryResolve(eventId, out AudioEventDefinition definition)
                ? service.PlayInternal(definition, position, null, false, -1f, false)
                : default;
        }

        public static AudioPlaybackHandle PlayAtAcoustic(
            AudioEventDefinition definition,
            Vector3 position)
        {
            AudioService service = Instance;
            return service != null
                ? service.PlayInternal(definition, position, null, false, -1f, true)
                : default;
        }

        public static AudioPlaybackHandle PlayAtAcoustic(
            string eventId,
            Vector3 position)
        {
            AudioService service = Instance;
            return service != null && service.TryResolve(eventId, out AudioEventDefinition definition)
                ? service.PlayInternal(definition, position, null, false, -1f, true)
                : default;
        }

        public static AudioPlaybackHandle PlayAttached(
            AudioEventDefinition definition,
            Transform target)
        {
            AudioService service = Instance;
            Vector3 position = target != null ? target.position : Vector3.zero;
            return service != null
                ? service.PlayInternal(definition, position, target, false, -1f, false)
                : default;
        }

        public static AudioPlaybackHandle StartLoop(
            AudioEventDefinition definition,
            Transform target = null,
            float fadeInSeconds = -1f)
        {
            AudioService service = Instance;
            Vector3 position = target != null ? target.position : Vector3.zero;
            return service != null
                ? service.PlayInternal(definition, position, target, true, fadeInSeconds, false)
                : default;
        }

        public static AudioPlaybackHandle StartAcousticLoop(
            AudioEventDefinition definition,
            Transform target = null,
            float fadeInSeconds = -1f)
        {
            AudioService service = Instance;
            Vector3 position = target != null ? target.position : Vector3.zero;
            return service != null
                ? service.PlayInternal(definition, position, target, true, fadeInSeconds, true)
                : default;
        }

        public static AudioPlaybackHandle StartLoopAt(
            AudioEventDefinition definition,
            Vector3 position,
            float fadeInSeconds = -1f)
        {
            AudioService service = Instance;
            return service != null
                ? service.PlayInternal(definition, position, null, true, fadeInSeconds, false)
                : default;
        }

        public static bool IsPlaying(AudioPlaybackHandle handle)
        {
            return instance != null &&
                   handle.Id != 0 &&
                   instance.voicesByHandle.ContainsKey(handle.Id);
        }

        public static bool Stop(AudioPlaybackHandle handle, float fadeOutSeconds = 0f)
        {
            if (instance == null ||
                handle.Id == 0 ||
                !instance.voicesByHandle.TryGetValue(handle.Id, out Voice voice))
            {
                return false;
            }

            instance.StopVoice(voice, fadeOutSeconds);
            return true;
        }

        public static bool SetPosition(AudioPlaybackHandle handle, Vector3 position)
        {
            if (instance == null ||
                handle.Id == 0 ||
                !instance.voicesByHandle.TryGetValue(handle.Id, out Voice voice))
            {
                return false;
            }

            voice.Position = position;
            if (voice.Source != null)
                voice.Source.transform.position = position;
            return true;
        }

        public static bool SetVolume(AudioPlaybackHandle handle, float volumeMultiplier)
        {
            if (instance == null ||
                handle.Id == 0 ||
                !instance.voicesByHandle.TryGetValue(handle.Id, out Voice voice))
            {
                return false;
            }

            voice.VolumeMultiplier = Mathf.Max(0f, volumeMultiplier);
            instance.ApplyVoiceVolume(voice);
            return true;
        }

        public static void StopAll(float fadeOutSeconds = 0f)
        {
            if (instance != null)
                instance.StopAllInternal(null, fadeOutSeconds);
        }

        public static void StopAll(AudioBus bus, float fadeOutSeconds = 0f)
        {
            if (instance != null)
                instance.StopAllInternal(bus, fadeOutSeconds);
        }

        public static bool SetBusVolume(AudioBus bus, float normalizedVolume)
        {
            float clamped = Mathf.Clamp01(normalizedVolume);
            float decibels = clamped <= 0.0001f
                ? MinimumDecibels
                : Mathf.Log10(clamped) * 20f;
            return SetBusVolumeDecibels(bus, decibels);
        }

        public static bool SetBusVolumeDecibels(AudioBus bus, float decibels)
        {
            AudioService service = Instance;
            return service != null &&
                   service.Mixer != null &&
                   service.Mixer.SetFloat(
                       AudioMixerParameters.GetVolumeParameter(bus),
                       Mathf.Clamp(decibels, MinimumDecibels, 20f));
        }

        private static void EnsureInstance()
        {
            if (instance != null || isQuitting)
                return;

            instance = FindObjectOfType<AudioService>(true);
            if (instance != null)
                return;

            GameObject serviceObject = new GameObject(ServiceObjectName);
            instance = serviceObject.AddComponent<AudioService>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            settings = AudioRuntimeSettings.Load();

            int initialPoolSize = settings != null
                ? settings.InitialPoolSize
                : DefaultInitialPoolSize;
            int maximumVoiceCount = GetMaximumVoiceCount();
            Prewarm(Mathf.Clamp(initialPoolSize, 0, maximumVoiceCount));
        }

        private void Update()
        {
            float unscaledDeltaTime = Time.unscaledDeltaTime;
            for (int i = activeVoices.Count - 1; i >= 0; i--)
            {
                Voice voice = activeVoices[i];
                if (voice.WasFollowing)
                {
                    if (voice.FollowTarget != null)
                    {
                        voice.Position = voice.FollowTarget.position;
                        if (voice.Source != null)
                            voice.Source.transform.position = voice.Position;
                    }
                    else if (voice.Source != null && voice.Source.loop)
                    {
                        voice.WasFollowing = false;
                        StopVoice(voice, 0.1f);
                    }
                    else
                    {
                        voice.WasFollowing = false;
                    }
                }

                if (!voicesByHandle.ContainsKey(voice.HandleId))
                    continue;

                if (voice.UseAcousticRooms)
                {
                    float targetGain = AcousticRoomSystem2D.GetTransmission(
                        voice.Position,
                        ref voice.AcousticRoom);
                    voice.AcousticGain = Mathf.MoveTowards(
                        voice.AcousticGain,
                        targetGain,
                        unscaledDeltaTime / AcousticTransitionSeconds);
                    ApplyVoiceVolume(voice);

                    if (targetGain <= AcousticSilenceThreshold &&
                        voice.AcousticGain <= AcousticSilenceThreshold)
                    {
                        ReleaseVoice(voice);
                        continue;
                    }
                }

                if (voice.FadeSpeed > 0f)
                {
                    float target = voice.FadingOut ? 0f : 1f;
                    voice.Fade = Mathf.MoveTowards(
                        voice.Fade,
                        target,
                        voice.FadeSpeed * unscaledDeltaTime);
                    ApplyVoiceVolume(voice);

                    if (voice.FadingOut && voice.Fade <= 0f)
                    {
                        ReleaseVoice(voice);
                        continue;
                    }

                    if (!voice.FadingOut && voice.Fade >= 1f)
                        voice.FadeSpeed = 0f;
                }

                if (voice.Source != null &&
                    !voice.Source.isPlaying &&
                    !voice.FadingOut)
                {
                    ReleaseVoice(voice);
                }
            }
        }

        private void OnApplicationQuit()
        {
            isQuitting = true;
        }

        private void OnDestroy()
        {
            if (instance != this)
                return;

            for (int i = activeVoices.Count - 1; i >= 0; i--)
            {
                if (activeVoices[i].Source != null)
                    activeVoices[i].Source.Stop();
            }

            activeVoices.Clear();
            voicesByHandle.Clear();
            availableSources.Clear();
            instance = null;
        }

        private AudioPlaybackHandle PlayInternal(
            AudioEventDefinition definition,
            Vector3 position,
            Transform followTarget,
            bool forceLoop,
            float requestedFadeInSeconds,
            bool useAcousticRooms)
        {
            if (definition == null)
                return default;

            MapRoomQuery2D sourceRoom = default;
            float acousticGain = useAcousticRooms
                ? AcousticRoomSystem2D.GetTransmission(position, ref sourceRoom)
                : 1f;
            if (acousticGain <= AcousticSilenceThreshold)
                return default;

            EventRuntimeState state = GetEventState(definition);
            double now = UnityEngine.AudioSettings.dspTime;
            if (definition.CooldownSeconds > 0f &&
                now - state.LastPlayTime < definition.CooldownSeconds)
            {
                return default;
            }

            AudioClip clip = SelectClip(definition, state, out int selectedIndex);
            if (clip == null)
            {
                WarnMissingClipsOnce(definition);
                return default;
            }

            string concurrencyKey = GetConcurrencyKey(definition);
            if (!ApplyConcurrencyLimit(definition, concurrencyKey))
                return default;

            AudioSource source = AcquireSource(definition.Priority);
            if (source == null)
            {
                WarnVoiceLimitOnce();
                return default;
            }

            bool shouldLoop = forceLoop || definition.Loop;
            float fadeInSeconds = requestedFadeInSeconds >= 0f
                ? requestedFadeInSeconds
                : definition.FadeInSeconds;
            float baseVolume = UnityEngine.Random.Range(
                definition.VolumeRange.x,
                definition.VolumeRange.y);

            ConfigureSource(source, definition, clip, position, shouldLoop);

            Voice voice = new Voice
            {
                Source = source,
                Definition = definition,
                HandleId = GetNextHandleId(),
                ConcurrencyKey = concurrencyKey,
                StartedAt = now,
                FollowTarget = followTarget,
                WasFollowing = followTarget != null,
                Position = position,
                BaseVolume = baseVolume,
                VolumeMultiplier = 1f,
                Fade = fadeInSeconds > 0f ? 0f : 1f,
                FadeSpeed = fadeInSeconds > 0f ? 1f / fadeInSeconds : 0f,
                UseAcousticRooms = useAcousticRooms,
                AcousticRoom = sourceRoom,
                AcousticGain = acousticGain
            };

            ApplyVoiceVolume(voice);
            activeVoices.Add(voice);
            voicesByHandle.Add(voice.HandleId, voice);
            source.Play();

            state.LastPlayTime = now;
            state.LastClipIndex = selectedIndex;
            return new AudioPlaybackHandle(voice.HandleId);
        }

        private void ConfigureSource(
            AudioSource source,
            AudioEventDefinition definition,
            AudioClip clip,
            Vector3 position,
            bool loop)
        {
            source.transform.position = position;
            source.clip = clip;
            source.outputAudioMixerGroup = settings != null
                ? settings.GetGroup(definition.Bus)
                : null;
            source.loop = loop;
            source.playOnAwake = false;
            source.priority = Mathf.Clamp(definition.Priority, 0, 256);
            source.pitch = UnityEngine.Random.Range(
                definition.PitchRange.x,
                definition.PitchRange.y);
            source.spatialBlend = definition.SpatialBlend;
            source.rolloffMode = definition.RolloffMode;
            source.minDistance = definition.MinDistance;
            source.maxDistance = definition.MaxDistance;
            source.dopplerLevel = 0f;
            source.spread = 0f;
            source.spatialize = false;
            source.ignoreListenerPause = definition.IgnoreListenerPause;
        }

        private bool TryResolve(string eventId, out AudioEventDefinition definition)
        {
            definition = null;
            string normalizedId = eventId != null ? eventId.Trim() : string.Empty;
            AudioEventCatalog catalog = Catalog;
            if (catalog != null && catalog.TryGet(normalizedId, out definition))
                return true;

            WarnUnknownIdOnce(normalizedId);
            return false;
        }

        private EventRuntimeState GetEventState(AudioEventDefinition definition)
        {
            int key = definition.GetInstanceID();
            if (!eventStates.TryGetValue(key, out EventRuntimeState state))
            {
                state = new EventRuntimeState();
                eventStates.Add(key, state);
            }

            return state;
        }

        private static AudioClip SelectClip(
            AudioEventDefinition definition,
            EventRuntimeState state,
            out int selectedIndex)
        {
            selectedIndex = -1;
            int validCount = definition.GetValidClipCount();
            if (validCount <= 0)
                return null;

            int validOrdinal;
            switch (definition.SelectionMode)
            {
                case AudioClipSelectionMode.Sequential:
                    validOrdinal = state.NextSequentialOrdinal % validCount;
                    state.NextSequentialOrdinal = (validOrdinal + 1) % validCount;
                    break;

                case AudioClipSelectionMode.Random:
                    validOrdinal = UnityEngine.Random.Range(0, validCount);
                    break;

                default:
                    validOrdinal = UnityEngine.Random.Range(0, validCount);
                    selectedIndex = definition.GetValidClipIndex(validOrdinal);
                    if (validCount > 1 && selectedIndex == state.LastClipIndex)
                    {
                        int offset = UnityEngine.Random.Range(1, validCount);
                        validOrdinal = (validOrdinal + offset) % validCount;
                    }
                    break;
            }

            selectedIndex = definition.GetValidClipIndex(validOrdinal);
            return definition.GetClipAt(selectedIndex);
        }

        private bool ApplyConcurrencyLimit(
            AudioEventDefinition definition,
            string concurrencyKey)
        {
            int matchingCount = 0;
            Voice oldest = null;
            for (int i = 0; i < activeVoices.Count; i++)
            {
                Voice voice = activeVoices[i];
                if (!string.Equals(
                        voice.ConcurrencyKey,
                        concurrencyKey,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                matchingCount++;
                if (oldest == null || voice.StartedAt < oldest.StartedAt)
                    oldest = voice;
            }

            if (matchingCount < definition.MaxConcurrentInstances)
                return true;

            if (definition.ConcurrencyMode == AudioConcurrencyMode.StopOldest &&
                oldest != null)
            {
                ReleaseVoice(oldest);
                return true;
            }

            return false;
        }

        private AudioSource AcquireSource(int requestedPriority)
        {
            if (availableSources.Count > 0)
                return availableSources.Pop();

            if (sourceCount < GetMaximumVoiceCount())
                return CreateSource();

            Voice candidate = null;
            for (int i = 0; i < activeVoices.Count; i++)
            {
                Voice voice = activeVoices[i];
                if (voice.Source.priority < requestedPriority)
                    continue;

                if (candidate == null ||
                    voice.Source.priority > candidate.Source.priority ||
                    (voice.Source.priority == candidate.Source.priority &&
                     voice.StartedAt < candidate.StartedAt))
                {
                    candidate = voice;
                }
            }

            if (candidate == null)
                return null;

            AudioSource source = candidate.Source;
            RemoveVoice(candidate, false);
            return source;
        }

        private void StopVoice(Voice voice, float fadeOutSeconds)
        {
            if (voice == null || !voicesByHandle.ContainsKey(voice.HandleId))
                return;

            if (fadeOutSeconds <= 0f)
            {
                ReleaseVoice(voice);
                return;
            }

            voice.FadingOut = true;
            voice.FadeSpeed = 1f / fadeOutSeconds;
        }

        private void StopAllInternal(AudioBus? bus, float fadeOutSeconds)
        {
            Voice[] snapshot = activeVoices.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                Voice voice = snapshot[i];
                if (!bus.HasValue || voice.Definition.Bus == bus.Value)
                    StopVoice(voice, fadeOutSeconds);
            }
        }

        private void ReleaseVoice(Voice voice)
        {
            RemoveVoice(voice, true);
        }

        private void RemoveVoice(Voice voice, bool returnSourceToPool)
        {
            if (voice == null)
                return;

            voicesByHandle.Remove(voice.HandleId);
            activeVoices.Remove(voice);

            AudioSource source = voice.Source;
            if (source == null)
                return;

            ResetSource(source);
            if (returnSourceToPool)
                availableSources.Push(source);
        }

        private static void ResetSource(AudioSource source)
        {
            source.Stop();
            source.clip = null;
            source.outputAudioMixerGroup = null;
            source.loop = false;
            source.volume = 1f;
            source.pitch = 1f;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = false;
        }

        private void ApplyVoiceVolume(Voice voice)
        {
            if (voice.Source != null)
            {
                voice.Source.volume = Mathf.Clamp01(
                    voice.BaseVolume *
                    voice.VolumeMultiplier *
                    voice.Fade *
                    voice.AcousticGain);
            }
        }

        private void Prewarm(int count)
        {
            for (int i = 0; i < count; i++)
                availableSources.Push(CreateSource());
        }

        private AudioSource CreateSource()
        {
            sourceCount++;
            GameObject voiceObject = new GameObject(
                "AudioVoice_" + sourceCount.ToString("00"));
            voiceObject.transform.SetParent(transform, false);
            AudioSource source = voiceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.dopplerLevel = 0f;
            return source;
        }

        private int GetMaximumVoiceCount()
        {
            return settings != null
                ? Mathf.Max(1, settings.MaximumVoiceCount)
                : DefaultMaximumVoiceCount;
        }

        private int GetNextHandleId()
        {
            do
            {
                nextHandleId = nextHandleId == int.MaxValue ? 1 : nextHandleId + 1;
            }
            while (nextHandleId == 0 || voicesByHandle.ContainsKey(nextHandleId));

            return nextHandleId;
        }

        private static string GetConcurrencyKey(AudioEventDefinition definition)
        {
            return !string.IsNullOrWhiteSpace(definition.ConcurrencyGroup)
                ? definition.ConcurrencyGroup.Trim()
                : "definition:" + definition.GetInstanceID();
        }

        private void WarnMissingClipsOnce(AudioEventDefinition definition)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (ShouldLogMissingAudio() &&
                warnedEmptyDefinitions.Add(definition.GetInstanceID()))
            {
                Debug.LogWarning(
                    "[AUDIO] Event '" + definition.EventId +
                    "' has no assigned AudioClip. Playback was skipped.",
                    definition);
            }
#endif
        }

        private void WarnUnknownIdOnce(string eventId)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            string key = string.IsNullOrEmpty(eventId) ? "<empty>" : eventId;
            if (ShouldLogMissingAudio() && warnedUnknownIds.Add(key))
                Debug.LogWarning("[AUDIO] Unknown event id '" + key + "'. Playback was skipped.");
#endif
        }

        private void WarnVoiceLimitOnce()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!warnedVoiceLimit)
            {
                warnedVoiceLimit = true;
                Debug.LogWarning(
                    "[AUDIO] Voice limit reached. A lower-priority playback was skipped.",
                    this);
            }
#endif
        }

        private bool ShouldLogMissingAudio()
        {
            return settings != null && settings.LogMissingAudioOnce;
        }

        private sealed class Voice
        {
            public AudioSource Source;
            public AudioEventDefinition Definition;
            public int HandleId;
            public string ConcurrencyKey;
            public double StartedAt;
            public Transform FollowTarget;
            public bool WasFollowing;
            public Vector3 Position;
            public float BaseVolume;
            public float VolumeMultiplier;
            public float Fade;
            public float FadeSpeed;
            public bool FadingOut;
            public bool UseAcousticRooms;
            public MapRoomQuery2D AcousticRoom;
            public float AcousticGain = 1f;
        }

        private sealed class EventRuntimeState
        {
            public int LastClipIndex = -1;
            public int NextSequentialOrdinal;
            public double LastPlayTime = double.NegativeInfinity;
        }
    }
}


using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Audio
{
    [DefaultExecutionOrder(-10900)]
    public sealed class MusicDirector : MonoBehaviour
    {
        private const string DirectorObjectName = "[MusicDirector]";
        private const float ZonePollIntervalSeconds = 0.1f;
        private const float RegionChangeConfirmationSeconds = 0.15f;
        private const float DefaultTransitionSeconds = 1.5f;

        private static MusicDirector instance;
        private static bool isQuitting;

        private readonly List<AudioZone2D> zones = new List<AudioZone2D>();
        private readonly List<AudioRegionListener2D> listeners =
            new List<AudioRegionListener2D>();

        private AudioZone2D currentZone;
        private AudioZone2D pendingZone;
        private AudioRegionProfile currentProfile;
        private AudioEventDefinition currentTrack;
        private AudioPlaybackHandle currentPlayback;
        private MusicSituation currentSituation = MusicSituation.Exploration;
        private double pendingZoneSince;
        private double nextZonePollTime;
        private bool refreshRequested = true;

        public static MusicDirector Instance
        {
            get
            {
                EnsureInstance();
                return instance;
            }
        }

        public static bool HasInstance => instance != null;
        public AudioRegionProfile CurrentProfile => currentProfile;
        public MusicSituation CurrentSituation => currentSituation;

        public static event Action<AudioRegionProfile> ActiveRegionChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            isQuitting = false;
            ActiveRegionChanged = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            EnsureInstance();
        }

        public static void SetSituation(MusicSituation situation)
        {
            MusicDirector director = Instance;
            if (director != null)
                director.SetSituationInternal(situation);
        }

        public static void RefreshRegionNow()
        {
            MusicDirector director = Instance;
            if (director != null)
                director.EvaluateRegionSelection(true);
        }

        internal static void RegisterZone(AudioZone2D zone)
        {
            MusicDirector director = Instance;
            if (director == null || zone == null || director.zones.Contains(zone))
                return;

            director.zones.Add(zone);
            director.EvaluateRegionSelection(true);
        }

        internal static void UnregisterZone(AudioZone2D zone)
        {
            if (instance == null || zone == null)
                return;

            instance.zones.Remove(zone);
            instance.EvaluateRegionSelection(true);
        }

        internal static void RegisterListener(AudioRegionListener2D listener)
        {
            MusicDirector director = Instance;
            if (director == null || listener == null || director.listeners.Contains(listener))
                return;

            director.listeners.Add(listener);
            director.EvaluateRegionSelection(true);
        }

        internal static void UnregisterListener(AudioRegionListener2D listener)
        {
            if (instance == null || listener == null)
                return;

            instance.listeners.Remove(listener);
            instance.EvaluateRegionSelection(true);
        }

        internal static void RequestRegionRefresh()
        {
            if (instance != null)
                instance.refreshRequested = true;
        }

        private static void EnsureInstance()
        {
            if (instance != null || isQuitting || !Application.isPlaying)
                return;

            MusicDirector existing = FindObjectOfType<MusicDirector>();
            if (existing != null)
            {
                instance = existing;
                return;
            }

            GameObject directorObject = new GameObject(DirectorObjectName);
            instance = directorObject.AddComponent<MusicDirector>();
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
        }

        private void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (!refreshRequested && now < nextZonePollTime)
                return;

            refreshRequested = false;
            nextZonePollTime = now + ZonePollIntervalSeconds;
            EvaluateRegionSelection(false);
        }

        private void OnApplicationQuit()
        {
            isQuitting = true;
        }

        private void OnDestroy()
        {
            if (instance != this)
                return;

            currentPlayback.Stop();
            zones.Clear();
            listeners.Clear();
            instance = null;
        }

        private void EvaluateRegionSelection(bool immediate)
        {
            AudioRegionListener2D listener = GetPrimaryListener();
            AudioZone2D bestZone = listener != null
                ? GetBestZone(listener.Position)
                : null;

            if (bestZone == currentZone)
            {
                pendingZone = null;
                return;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            if (immediate || currentZone == null || RegionChangeConfirmationSeconds <= 0f)
            {
                ApplyZone(bestZone);
                return;
            }

            if (bestZone != pendingZone)
            {
                pendingZone = bestZone;
                pendingZoneSince = now;
                return;
            }

            if (now - pendingZoneSince >= RegionChangeConfirmationSeconds)
                ApplyZone(bestZone);
        }

        private AudioRegionListener2D GetPrimaryListener()
        {
            AudioRegionListener2D best = null;
            for (int i = listeners.Count - 1; i >= 0; i--)
            {
                AudioRegionListener2D listener = listeners[i];
                if (listener == null)
                {
                    listeners.RemoveAt(i);
                    continue;
                }

                if (!listener.isActiveAndEnabled)
                    continue;

                if (best == null ||
                    listener.Priority > best.Priority ||
                    (listener.Priority == best.Priority &&
                     listener.GetInstanceID() < best.GetInstanceID()))
                {
                    best = listener;
                }
            }

            return best;
        }

        private AudioZone2D GetBestZone(Vector2 position)
        {
            AudioZone2D best = null;
            float bestArea = float.PositiveInfinity;

            for (int i = zones.Count - 1; i >= 0; i--)
            {
                AudioZone2D zone = zones[i];
                if (zone == null)
                {
                    zones.RemoveAt(i);
                    continue;
                }

                if (!zone.Contains(position))
                    continue;

                float area = zone.GetWorldBoundsArea();
                if (best == null ||
                    zone.Priority > best.Priority ||
                    (zone.Priority == best.Priority && area < bestArea) ||
                    (zone.Priority == best.Priority &&
                     Mathf.Approximately(area, bestArea) &&
                     zone.GetInstanceID() < best.GetInstanceID()))
                {
                    best = zone;
                    bestArea = area;
                }
            }

            return best;
        }

        private void ApplyZone(AudioZone2D nextZone)
        {
            AudioRegionProfile previousProfile = currentProfile;
            currentZone = nextZone;
            pendingZone = null;
            currentProfile = nextZone != null ? nextZone.Profile : null;

            AudioEventDefinition nextTrack = currentProfile != null
                ? currentProfile.GetMusic(currentSituation)
                : null;
            float transitionSeconds = GetTransitionSeconds(previousProfile, currentProfile);
            SetTrack(nextTrack, transitionSeconds);
            ActiveRegionChanged?.Invoke(currentProfile);
        }

        private void SetSituationInternal(MusicSituation situation)
        {
            if (currentSituation == situation)
                return;

            currentSituation = situation;
            AudioEventDefinition nextTrack = currentProfile != null
                ? currentProfile.GetMusic(currentSituation)
                : null;
            float transitionSeconds = currentProfile != null
                ? currentProfile.TransitionSeconds
                : DefaultTransitionSeconds;
            SetTrack(nextTrack, transitionSeconds);
        }

        private void SetTrack(AudioEventDefinition nextTrack, float transitionSeconds)
        {
            if (nextTrack == currentTrack && currentPlayback.IsValid)
                return;

            if (currentPlayback.IsValid)
                currentPlayback.Stop(transitionSeconds);

            currentPlayback = default;
            currentTrack = nextTrack;
            if (nextTrack != null)
            {
                currentPlayback = AudioService.StartLoop(
                    nextTrack,
                    null,
                    transitionSeconds);
            }
        }

        private static float GetTransitionSeconds(
            AudioRegionProfile previousProfile,
            AudioRegionProfile nextProfile)
        {
            if (nextProfile != null)
                return nextProfile.TransitionSeconds;
            if (previousProfile != null)
                return previousProfile.TransitionSeconds;
            return DefaultTransitionSeconds;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Progress
{
    [DefaultExecutionOrder(-10000)]
    public sealed class GameProgressService : MonoBehaviour
    {
        private const string ServiceObjectName = "[GameProgressService]";

#if UNITY_EDITOR
        private const string FreshExplorationPreferenceKey =
            "SpaceWorld.Map.FreshExplorationEveryPlay";
        private const string FreshExplorationMenuPath =
            "SpaceWorld/Map/Fresh Exploration Every Play";
#endif

        private static GameProgressService instance;

        private readonly HashSet<string> confirmedRegionIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> pendingRegionIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> confirmedMapCellIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> pendingMapCellIds = new HashSet<string>(StringComparer.Ordinal);
        private GameProgressData data;
        private bool usesTemporaryProgress;

        [Header("Diagnostics")]
        [SerializeField] private bool logMapDiagnostics;

        public static GameProgressService Instance
        {
            get
            {
                EnsureInstance();
                return instance;
            }
        }

        public bool MapOwned => data != null && data.mapOwned;
        public string LastRestBenchId => data != null ? data.lastRestBenchId : string.Empty;
        public string LastRestScene => data != null ? data.lastRestScene : string.Empty;
        public int PendingRegionCount => pendingRegionIds.Count;
        public int PendingMapCellCount => pendingMapCellIds.Count;
        public IReadOnlyCollection<string> PendingMapCellIds => pendingMapCellIds;
        public IReadOnlyCollection<string> ConfirmedMapCellIds => confirmedMapCellIds;
        public bool LogMapDiagnostics => logMapDiagnostics;
        public bool UsesTemporaryProgress => usesTemporaryProgress;

        public event Action<bool> OnMapOwnershipChanged;
        public event Action<string> OnRegionPending;
        public event Action<string> OnRegionConfirmed;
        public event Action<string> OnMapCellPending;
        public event Action<string> OnMapCellConfirmed;
        public event Action OnProgressLoaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            EnsureInstance();
        }

        private static void EnsureInstance()
        {
            if (instance != null)
                return;

            instance = FindObjectOfType<GameProgressService>(true);
            if (instance != null)
                return;

            GameObject serviceObject = new GameObject(ServiceObjectName);
            instance = serviceObject.AddComponent<GameProgressService>();
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
            LoadProgressForCurrentMode();
        }

        public bool IsRegionConfirmed(string regionId)
        {
            string normalizedId = GameProgressData.NormalizeId(regionId);
            return !string.IsNullOrEmpty(normalizedId) && confirmedRegionIds.Contains(normalizedId);
        }

        public bool IsRegionPending(string regionId)
        {
            string normalizedId = GameProgressData.NormalizeId(regionId);
            return !string.IsNullOrEmpty(normalizedId) && pendingRegionIds.Contains(normalizedId);
        }

        public bool MarkRegionExploredPending(string regionId)
        {
            string normalizedId = GameProgressData.NormalizeId(regionId);
            if (string.IsNullOrEmpty(normalizedId) || confirmedRegionIds.Contains(normalizedId))
                return false;

            if (!pendingRegionIds.Add(normalizedId))
                return false;

            LogMapDiagnostic($"[MAP][2 PENDING] region='{normalizedId}' pendingCount={pendingRegionIds.Count}");
            OnRegionPending?.Invoke(normalizedId);
            return true;
        }

        public bool IsMapCellConfirmed(string cellId)
        {
            string normalizedId = GameProgressData.NormalizeId(cellId);
            return !string.IsNullOrEmpty(normalizedId) && confirmedMapCellIds.Contains(normalizedId);
        }

        public bool IsMapCellPending(string cellId)
        {
            string normalizedId = GameProgressData.NormalizeId(cellId);
            return !string.IsNullOrEmpty(normalizedId) && pendingMapCellIds.Contains(normalizedId);
        }

        public bool MarkMapCellExploredPending(string cellId)
        {
            string normalizedId = GameProgressData.NormalizeId(cellId);
            if (string.IsNullOrEmpty(normalizedId) || confirmedMapCellIds.Contains(normalizedId))
                return false;

            if (!pendingMapCellIds.Add(normalizedId))
                return false;

            LogMapDiagnostic(
                $"[PENDING CELL] cell='{normalizedId}' pendingCellCount={pendingMapCellIds.Count}");
            OnMapCellPending?.Invoke(normalizedId);
            return true;
        }

        public void DiscardPendingExploration()
        {
            int discardedRegionCount = pendingRegionIds.Count;
            int discardedCellCount = pendingMapCellIds.Count;
            pendingRegionIds.Clear();
            pendingMapCellIds.Clear();
            LogMapDiagnostic(
                $"[MAP][DISCARD] pendingRegions={discardedRegionCount} " +
                $"pendingCells={discardedCellCount}");
        }

        public bool GrantMap(bool saveImmediately = true)
        {
            EnsureData();
            if (data.mapOwned)
                return false;

            data.mapOwned = true;
            OnMapOwnershipChanged?.Invoke(true);

            if (saveImmediately)
                SaveProgress();

            return true;
        }

        public bool RestAtBench(string benchId, string sceneName)
        {
            EnsureData();

            string normalizedBenchId = GameProgressData.NormalizeId(benchId);
            string normalizedSceneName = sceneName != null ? sceneName.Trim() : string.Empty;
            LogMapDiagnostic(
                $"[MAP][3 REST] bench='{normalizedBenchId}' scene='{normalizedSceneName}' " +
                $"pendingRegionsBeforeConfirm={pendingRegionIds.Count} " +
                $"pendingCellsBeforeConfirm={pendingMapCellIds.Count}");
            ConfirmPendingExploration();

            if (!string.IsNullOrEmpty(normalizedBenchId))
                data.lastRestBenchId = normalizedBenchId;
            else
                Debug.LogWarning("[PROGRESS] RestBench has no stable benchId. Exploration will save, but this bench cannot become a persistent respawn identity.");

            data.lastRestScene = normalizedSceneName;
            bool saved = SaveProgress();
            LogMapDiagnostic(
                $"[MAP][SAVE] success={saved} confirmedRegions={confirmedRegionIds.Count} " +
                $"pendingRegions={pendingRegionIds.Count} " +
                $"confirmedCells={confirmedMapCellIds.Count} " +
                $"pendingCells={pendingMapCellIds.Count}");
            return saved;
        }

        public bool SaveProgress()
        {
            EnsureData();
            SyncConfirmedExplorationToData();

            if (usesTemporaryProgress)
            {
                LogMapDiagnostic(
                    "[MAP][SAVE] persistentWrite=skipped temporarySession=true");
                return true;
            }

            return GameSaveSystem.Save(data);
        }

        public void ReloadProgressFromDisk()
        {
            LoadProgressForCurrentMode();
        }

        private void LoadProgressForCurrentMode()
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorPrefs.GetBool(FreshExplorationPreferenceKey, true))
            {
                usesTemporaryProgress = true;
                LoadFreshEditorProgress();
                return;
            }
#endif

            usesTemporaryProgress = false;
            LoadFromDisk();
        }

        private void LoadFromDisk()
        {
            data = GameSaveSystem.LoadOrCreate();
            pendingRegionIds.Clear();
            confirmedRegionIds.Clear();
            pendingMapCellIds.Clear();
            confirmedMapCellIds.Clear();

            for (int i = 0; i < data.confirmedExploredRegionIds.Count; i++)
                confirmedRegionIds.Add(data.confirmedExploredRegionIds[i]);
            for (int i = 0; i < data.confirmedMapCellIds.Count; i++)
                confirmedMapCellIds.Add(data.confirmedMapCellIds[i]);

            OnProgressLoaded?.Invoke();
        }

        private void ConfirmPendingExploration()
        {
            if (pendingRegionIds.Count == 0 && pendingMapCellIds.Count == 0)
                return;

            foreach (string regionId in pendingRegionIds)
            {
                if (confirmedRegionIds.Add(regionId))
                {
                    LogMapDiagnostic(
                        $"[MAP][4 CONFIRMED] region='{regionId}' confirmedCount={confirmedRegionIds.Count}");
                    OnRegionConfirmed?.Invoke(regionId);
                }
            }

            pendingRegionIds.Clear();

            foreach (string cellId in pendingMapCellIds)
            {
                if (confirmedMapCellIds.Add(cellId))
                {
                    LogMapDiagnostic(
                        $"[CONFIRMED CELL] cell='{cellId}' " +
                        $"confirmedCellCount={confirmedMapCellIds.Count}");
                    OnMapCellConfirmed?.Invoke(cellId);
                }
            }

            pendingMapCellIds.Clear();
        }

        private void SyncConfirmedExplorationToData()
        {
            data.confirmedExploredRegionIds.Clear();
            foreach (string regionId in confirmedRegionIds)
                data.confirmedExploredRegionIds.Add(regionId);

            data.confirmedExploredRegionIds.Sort(StringComparer.Ordinal);

            data.confirmedMapCellIds.Clear();
            foreach (string cellId in confirmedMapCellIds)
                data.confirmedMapCellIds.Add(cellId);

            data.confirmedMapCellIds.Sort(StringComparer.Ordinal);
            data.saveVersion = GameProgressData.CurrentSaveVersion;
        }

        private void EnsureData()
        {
            if (data == null)
                LoadProgressForCurrentMode();
        }

        [ContextMenu("Log Map Diagnostics Snapshot")]
        private void LogMapDiagnosticsSnapshot()
        {
            Debug.Log(
                $"[MAP][SNAPSHOT] temporarySession={usesTemporaryProgress} mapOwned={MapOwned} " +
                $"legacyPending=[{FormatIds(pendingRegionIds)}] " +
                $"legacyConfirmed=[{FormatIds(confirmedRegionIds)}] " +
                $"pendingCells=[{FormatIds(pendingMapCellIds)}] " +
                $"confirmedCells=[{FormatIds(confirmedMapCellIds)}] " +
                $"lastBench='{LastRestBenchId}' lastScene='{LastRestScene}'",
                this);
        }

#if UNITY_EDITOR
        private void LoadFreshEditorProgress()
        {
            data = new GameProgressData
            {
                mapOwned = true
            };
            data.Normalize();

            pendingRegionIds.Clear();
            confirmedRegionIds.Clear();
            pendingMapCellIds.Clear();
            confirmedMapCellIds.Clear();

            Debug.Log(
                "[MAP][TEST MODE] Fresh in-memory exploration active. " +
                "The persistent save was not loaded and will not be written.",
                this);
            OnProgressLoaded?.Invoke();
        }

        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeFreshExplorationMenu()
        {
            UnityEditor.EditorApplication.delayCall -= RefreshFreshExplorationMenu;
            UnityEditor.EditorApplication.delayCall += RefreshFreshExplorationMenu;
        }

        [UnityEditor.MenuItem(FreshExplorationMenuPath, false, 2000)]
        private static void ToggleFreshExplorationEveryPlay()
        {
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            bool enabled = !IsFreshExplorationEveryPlayEnabled();
            UnityEditor.EditorPrefs.SetBool(FreshExplorationPreferenceKey, enabled);
            RefreshFreshExplorationMenu();

            Debug.Log(
                $"[MAP][TEST MODE] Fresh Exploration Every Play " +
                $"{(enabled ? "enabled" : "disabled")}. " +
                "The change applies on the next Play Mode entry.");
        }

        [UnityEditor.MenuItem(FreshExplorationMenuPath, true)]
        private static bool ValidateFreshExplorationEveryPlay()
        {
            RefreshFreshExplorationMenu();
            return !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static bool IsFreshExplorationEveryPlayEnabled()
        {
            return UnityEditor.EditorPrefs.GetBool(FreshExplorationPreferenceKey, true);
        }

        private static void RefreshFreshExplorationMenu()
        {
            UnityEditor.Menu.SetChecked(
                FreshExplorationMenuPath,
                IsFreshExplorationEveryPlayEnabled());
        }
#endif

        private void LogMapDiagnostic(string message)
        {
            if (logMapDiagnostics)
                Debug.Log(message, this);
        }

        private static string FormatIds(HashSet<string> ids)
        {
            if (ids == null || ids.Count == 0)
                return "<none>";

            List<string> sortedIds = new List<string>(ids);
            sortedIds.Sort(StringComparer.Ordinal);
            return string.Join(", ", sortedIds);
        }
    }
}

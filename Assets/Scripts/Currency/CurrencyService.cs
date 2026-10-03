using System;
using System.Collections.Generic;
using Game.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Currency
{
    [DefaultExecutionOrder(-10000)]
    public sealed class CurrencyService : MonoBehaviour
    {
        private const string ServiceObjectName = "[CurrencyService]";

#if UNITY_EDITOR
        private const string FreshEconomyPreferenceKey =
            "SpaceWorld.Money.FreshEconomyEveryPlay";
        private const string FreshEconomyMenuPath =
            "SpaceWorld/Money/Fresh Economy Every Play";
#endif

        private static CurrencyService instance;

        private readonly HashSet<string> collectedPickupKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, DroppedCoinBagSaveData> bagRecords =
            new Dictionary<string, DroppedCoinBagSaveData>(StringComparer.Ordinal);
        private readonly Dictionary<string, DroppedCoinBag> liveBags =
            new Dictionary<string, DroppedCoinBag>(StringComparer.Ordinal);

        private CurrencySaveData data;
        private bool usesTemporaryEconomy;

#if UNITY_EDITOR
        [Header("Diagnostics")]
        [SerializeField] private bool logDiagnostics = true;
#else
        [SerializeField] private bool logDiagnostics;
#endif

        public static CurrencyService Instance
        {
            get
            {
                EnsureInstance();
                return instance;
            }
        }

        public int CarriedCoins => data != null ? data.carriedCoins : 0;
        public int BankedCoins => data != null ? data.bankedCoins : 0;
        public long TotalCoins => (long)CarriedCoins + BankedCoins;
        public int PendingBagCount => bagRecords.Count;
        public bool UsesTemporaryEconomy => usesTemporaryEconomy;

        public event Action<int, int> OnCurrencyChanged;
        public event Action OnDroppedBagsChanged;
        public event Action OnEconomyLoaded;

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

            instance = FindObjectOfType<CurrencyService>(true);
            if (instance != null)
                return;

            GameObject serviceObject = new GameObject(ServiceObjectName);
            instance = serviceObject.AddComponent<CurrencyService>();
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
            LoadForCurrentMode();

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void Start()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.isLoaded)
                HandleSceneLoaded(activeScene, LoadSceneMode.Single);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (instance == this)
                instance = null;
        }

        public bool IsPlacedCoinCollected(string sceneName, string pickupId)
        {
            EnsureData();
            string key = CurrencySaveData.BuildPickupKey(sceneName, pickupId);
            return !string.IsNullOrEmpty(key) && collectedPickupKeys.Contains(key);
        }

        public bool TryCollectPlacedCoin(string sceneName, string pickupId, int amount)
        {
            EnsureData();
            string key = CurrencySaveData.BuildPickupKey(sceneName, pickupId);
            if (string.IsNullOrEmpty(key) || amount <= 0 || collectedPickupKeys.Contains(key))
                return false;

            if (!TryAdd(data.carriedCoins, amount, out int newCarriedCoins))
            {
                Debug.LogWarning("[CURRENCY] Coin pickup rejected because CarriedCoins would overflow.");
                return false;
            }

            int previousCarriedCoins = data.carriedCoins;
            data.carriedCoins = newCarriedCoins;
            collectedPickupKeys.Add(key);

            if (!CommitCurrentState())
            {
                data.carriedCoins = previousCarriedCoins;
                collectedPickupKeys.Remove(key);
                SyncRuntimeCollectionsToData();
                return false;
            }

            LogDiagnostic(
                $"[CURRENCY][PICKUP] scene='{sceneName}' pickupId='{pickupId}' " +
                $"amount={amount} carried={data.carriedCoins} result=success");
            RaiseCurrencyChanged();
            return true;
        }

        public bool TryAddCarriedCoins(int amount)
        {
            EnsureData();
            if (amount <= 0 || !TryAdd(data.carriedCoins, amount, out int newCarriedCoins))
                return false;

            int previousCarriedCoins = data.carriedCoins;
            data.carriedCoins = newCarriedCoins;
            if (!CommitCurrentState())
            {
                data.carriedCoins = previousCarriedCoins;
                SyncRuntimeCollectionsToData();
                return false;
            }

            RaiseCurrencyChanged();
            return true;
        }

        public bool DepositAllCarriedCoins()
        {
            EnsureData();
            if (data.carriedCoins <= 0)
                return true;

            if (!TryAdd(data.bankedCoins, data.carriedCoins, out int newBankedCoins))
            {
                Debug.LogWarning("[CURRENCY] Deposit rejected because BankedCoins would overflow.");
                return false;
            }

            int previousCarriedCoins = data.carriedCoins;
            int previousBankedCoins = data.bankedCoins;
            data.carriedCoins = 0;
            data.bankedCoins = newBankedCoins;

            if (!CommitCurrentState())
            {
                data.carriedCoins = previousCarriedCoins;
                data.bankedCoins = previousBankedCoins;
                SyncRuntimeCollectionsToData();
                return false;
            }

            RaiseCurrencyChanged();
            return true;
        }

        public bool CanAfford(int amount)
        {
            return amount >= 0 && TotalCoins >= amount;
        }

        public bool TrySpendCoins(int amount)
        {
            EnsureData();
            if (amount < 0 || !CanAfford(amount))
                return false;
            if (amount == 0)
                return true;

            int previousCarriedCoins = data.carriedCoins;
            int previousBankedCoins = data.bankedCoins;
            int spentFromCarried = Mathf.Min(data.carriedCoins, amount);
            data.carriedCoins -= spentFromCarried;
            data.bankedCoins -= amount - spentFromCarried;

            if (!CommitCurrentState())
            {
                data.carriedCoins = previousCarriedCoins;
                data.bankedCoins = previousBankedCoins;
                SyncRuntimeCollectionsToData();
                return false;
            }

            RaiseCurrencyChanged();
            return true;
        }

        public bool TryDropCarriedCoins(
            string sceneName,
            Vector3 position,
            out string createdBagId)
        {
            return TryDropCarriedCoins("unspecified", sceneName, position, out createdBagId);
        }

        internal bool TryDropCarriedCoins(
            string deathType,
            string sceneName,
            Vector3 position,
            out string createdBagId)
        {
            EnsureData();
            createdBagId = string.Empty;

            string normalizedSceneName = sceneName != null ? sceneName.Trim() : string.Empty;
            int carriedCoinsAtDeath = data.carriedCoins;
            string normalizedDeathType = string.IsNullOrWhiteSpace(deathType)
                ? "unspecified"
                : deathType.Trim();
            if (carriedCoinsAtDeath <= 0)
            {
                LogDropResult(
                    normalizedDeathType,
                    carriedCoinsAtDeath,
                    string.Empty,
                    normalizedSceneName,
                    position,
                    false,
                    "empty-wallet");
                return false;
            }

            if (string.IsNullOrEmpty(normalizedSceneName))
            {
                LogDropResult(
                    normalizedDeathType,
                    carriedCoinsAtDeath,
                    string.Empty,
                    normalizedSceneName,
                    position,
                    false,
                    "invalid-scene");
                return false;
            }

            if (!CurrencySaveData.IsFinite(position))
            {
                LogDropResult(
                    normalizedDeathType,
                    carriedCoinsAtDeath,
                    string.Empty,
                    normalizedSceneName,
                    position,
                    false,
                    "invalid-position");
                return false;
            }

            string bagId;
            do
            {
                bagId = Guid.NewGuid().ToString("N");
            }
            while (bagRecords.ContainsKey(bagId));

            DroppedCoinBagSaveData bag = new DroppedCoinBagSaveData
            {
                bagId = bagId,
                coinAmount = carriedCoinsAtDeath,
                sceneName = normalizedSceneName,
                position = position
            };

            int previousCarriedCoins = carriedCoinsAtDeath;
            data.carriedCoins = 0;
            bagRecords.Add(bagId, bag);

            if (!CommitCurrentState())
            {
                data.carriedCoins = previousCarriedCoins;
                bagRecords.Remove(bagId);
                SyncRuntimeCollectionsToData();
                LogDropResult(
                    normalizedDeathType,
                    carriedCoinsAtDeath,
                    bagId,
                    normalizedSceneName,
                    position,
                    false,
                    "save-failed-carried-preserved");
                return false;
            }

            createdBagId = bagId;
            LogDropResult(
                normalizedDeathType,
                carriedCoinsAtDeath,
                bagId,
                normalizedSceneName,
                position,
                true,
                "record-persisted");
            RaiseCurrencyChanged();
            OnDroppedBagsChanged?.Invoke();
            TrySpawnBagInLoadedScene(bag);
            return true;
        }

        public bool TryRecoverDroppedBag(string bagId)
        {
            EnsureData();
            string normalizedBagId = CurrencySaveData.NormalizeId(bagId);
            if (string.IsNullOrEmpty(normalizedBagId) ||
                !bagRecords.TryGetValue(normalizedBagId, out DroppedCoinBagSaveData bag) ||
                bag.coinAmount <= 0)
            {
                LogDiagnostic(
                    $"[CURRENCY][BAG RECOVER] bagId='{normalizedBagId}' " +
                    "result=failed reason=record-not-found-or-empty");
                return false;
            }

            if (!TryAdd(data.carriedCoins, bag.coinAmount, out int newCarriedCoins))
            {
                Debug.LogWarning("[CURRENCY] Bag recovery rejected because CarriedCoins would overflow.");
                LogDiagnostic(
                    $"[CURRENCY][BAG RECOVER] bagId='{normalizedBagId}' " +
                    $"amount={bag.coinAmount} result=failed reason=carried-overflow");
                return false;
            }

            int previousCarriedCoins = data.carriedCoins;
            data.carriedCoins = newCarriedCoins;
            bagRecords.Remove(normalizedBagId);

            if (!CommitCurrentState())
            {
                data.carriedCoins = previousCarriedCoins;
                bagRecords.Add(normalizedBagId, bag);
                SyncRuntimeCollectionsToData();
                LogDiagnostic(
                    $"[CURRENCY][BAG RECOVER] bagId='{normalizedBagId}' " +
                    $"amount={bag.coinAmount} result=failed reason=save-failed-record-preserved");
                return false;
            }

            liveBags.Remove(normalizedBagId);
            LogDiagnostic(
                $"[CURRENCY][BAG RECOVER] bagId='{normalizedBagId}' " +
                $"amount={bag.coinAmount} carried={data.carriedCoins} result=success");
            RaiseCurrencyChanged();
            OnDroppedBagsChanged?.Invoke();
            return true;
        }

        public void HandleFatalFall(Transform playerTransform)
        {
            if (playerTransform == null)
                return;

            CurrencyPlayerAgent agent =
                playerTransform.GetComponent<CurrencyPlayerAgent>() ??
                playerTransform.GetComponentInChildren<CurrencyPlayerAgent>(true);

            if (agent == null)
            {
                PlayerHealth health =
                    playerTransform.GetComponent<PlayerHealth>() ??
                    playerTransform.GetComponentInChildren<PlayerHealth>(true);
                if (health != null)
                {
                    agent = health.gameObject.AddComponent<CurrencyPlayerAgent>();
                    agent.Bind(health);
                    LogDiagnostic(
                        $"[CURRENCY][AGENT] player='{health.gameObject.name}' " +
                        $"healthInstance={health.GetInstanceID()} agentInstance={agent.GetInstanceID()} " +
                        $"bound={agent.IsBoundTo(health)} source=fatal-fall",
                        agent);
                }
            }

            agent?.HandleFatalFall();
        }

        public void ReloadEconomy()
        {
            LoadForCurrentMode();
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.isLoaded)
                SpawnPendingBagsForScene(activeScene);
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            AttachAgentToPlayer(scene);
            SpawnPendingBagsForScene(scene);
        }

        private void AttachAgentToPlayer(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                PlayerHealth health = roots[i].GetComponentInChildren<PlayerHealth>(true);
                if (health == null)
                    continue;

                CurrencyPlayerAgent agent = health.GetComponent<CurrencyPlayerAgent>();
                bool wasAdded = agent == null;
                if (wasAdded)
                    agent = health.gameObject.AddComponent<CurrencyPlayerAgent>();

                bool wasBound = agent.IsBoundTo(health);
                agent.Bind(health);
                if (wasAdded || !wasBound)
                {
                    LogDiagnostic(
                        $"[CURRENCY][AGENT] player='{health.gameObject.name}' " +
                        $"healthInstance={health.GetInstanceID()} agentInstance={agent.GetInstanceID()} " +
                        $"bound={agent.IsBoundTo(health)} scene='{scene.name}'",
                        agent);
                }
                return;
            }
        }

        private void SpawnPendingBagsForScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            RemoveDestroyedLiveBagReferences();
            foreach (DroppedCoinBagSaveData bag in bagRecords.Values)
            {
                if (string.Equals(bag.sceneName, scene.name, StringComparison.Ordinal))
                    TrySpawnBag(bag, scene);
            }
        }

        private void TrySpawnBagInLoadedScene(DroppedCoinBagSaveData bag)
        {
            Scene scene = SceneManager.GetSceneByName(bag.sceneName);
            if (scene.IsValid() && scene.isLoaded)
            {
                TrySpawnBag(bag, scene);
                return;
            }

            LogDiagnostic(
                $"[CURRENCY][BAG SPAWN] bagId='{bag.bagId}' amount={bag.coinAmount} " +
                $"scene='{bag.sceneName}' position={FormatPosition(bag.position)} " +
                "result=deferred reason=scene-not-loaded");
        }

        private void TrySpawnBag(DroppedCoinBagSaveData bag, Scene scene)
        {
            if (bag == null || liveBags.ContainsKey(bag.bagId))
                return;

            CurrencyRuntimeSettings settings = CurrencyRuntimeSettings.Load();
            DroppedCoinBag prefab = settings != null ? settings.DroppedCoinBagPrefab : null;
            DroppedCoinBag spawnedBag;
            if (prefab != null)
            {
                spawnedBag = Instantiate(prefab, bag.position, Quaternion.identity);
                spawnedBag.name = "DroppedCoinBag_" + ShortId(bag.bagId);
                spawnedBag.Initialize(bag);
            }
            else
            {
                Debug.LogWarning(
                    "[CURRENCY] CurrencyRuntimeSettings or its bag prefab is missing. " +
                    "Using the procedural runtime fallback.",
                    this);
                spawnedBag = DroppedCoinBag.CreateRuntime(bag);
            }

            if (spawnedBag == null)
            {
                LogDiagnostic(
                    $"[CURRENCY][BAG SPAWN] bagId='{bag.bagId}' amount={bag.coinAmount} " +
                    $"scene='{bag.sceneName}' position={FormatPosition(bag.position)} " +
                    "result=failed reason=prefab-and-fallback-failed");
                return;
            }

            SceneManager.MoveGameObjectToScene(spawnedBag.gameObject, scene);
            liveBags.Add(bag.bagId, spawnedBag);
            LogDiagnostic(
                $"[CURRENCY][BAG SPAWN] bagId='{bag.bagId}' amount={bag.coinAmount} " +
                $"scene='{bag.sceneName}' position={FormatPosition(bag.position)} " +
                $"result=success source={(prefab != null ? "prefab" : "runtime-fallback")} " +
                $"liveBags={liveBags.Count} pickupArmed={spawnedBag.PickupArmed}",
                spawnedBag);
        }

        private static string ShortId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= 8)
                return value;
            return value.Substring(0, 8);
        }

        private void RemoveDestroyedLiveBagReferences()
        {
            if (liveBags.Count == 0)
                return;

            List<string> destroyedIds = null;
            foreach (KeyValuePair<string, DroppedCoinBag> pair in liveBags)
            {
                if (pair.Value != null)
                    continue;

                if (destroyedIds == null)
                    destroyedIds = new List<string>();
                destroyedIds.Add(pair.Key);
            }

            if (destroyedIds == null)
                return;

            for (int i = 0; i < destroyedIds.Count; i++)
                liveBags.Remove(destroyedIds[i]);
        }

        private void LoadForCurrentMode()
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorPrefs.GetBool(FreshEconomyPreferenceKey, true))
            {
                usesTemporaryEconomy = true;
                LoadData(new CurrencySaveData());
                Debug.Log(
                    "[CURRENCY][TEST MODE] Fresh in-memory economy active. " +
                    "The persistent economy was not loaded and will not be written.",
                    this);
                return;
            }
#endif

            usesTemporaryEconomy = false;
            LoadData(CurrencySaveSystem.LoadOrCreate());
        }

        private void LoadData(CurrencySaveData loadedData)
        {
            foreach (DroppedCoinBag liveBag in liveBags.Values)
            {
                if (liveBag == null)
                    continue;

                liveBag.gameObject.SetActive(false);
                Destroy(liveBag.gameObject);
            }
            liveBags.Clear();

            data = loadedData ?? new CurrencySaveData();
            data.Normalize();

            collectedPickupKeys.Clear();
            for (int i = 0; i < data.collectedPickupKeys.Count; i++)
                collectedPickupKeys.Add(data.collectedPickupKeys[i]);

            bagRecords.Clear();
            for (int i = 0; i < data.droppedCoinBags.Count; i++)
            {
                DroppedCoinBagSaveData bag = data.droppedCoinBags[i].Clone();
                bagRecords.Add(bag.bagId, bag);
            }

            OnEconomyLoaded?.Invoke();
            RaiseCurrencyChanged();
            OnDroppedBagsChanged?.Invoke();
        }

        private bool CommitCurrentState()
        {
            SyncRuntimeCollectionsToData();
            return usesTemporaryEconomy || CurrencySaveSystem.Save(data);
        }

        private void SyncRuntimeCollectionsToData()
        {
            data.saveVersion = CurrencySaveData.CurrentSaveVersion;

            data.collectedPickupKeys.Clear();
            foreach (string key in collectedPickupKeys)
                data.collectedPickupKeys.Add(key);
            data.collectedPickupKeys.Sort(StringComparer.Ordinal);

            data.droppedCoinBags.Clear();
            foreach (DroppedCoinBagSaveData bag in bagRecords.Values)
                data.droppedCoinBags.Add(bag.Clone());
            data.droppedCoinBags.Sort((left, right) =>
                string.CompareOrdinal(left.bagId, right.bagId));
        }

        private void EnsureData()
        {
            if (data == null)
                LoadForCurrentMode();
        }

        private void RaiseCurrencyChanged()
        {
            OnCurrencyChanged?.Invoke(CarriedCoins, BankedCoins);
        }

        internal void LogDiagnostic(string message, UnityEngine.Object context = null)
        {
            if (!logDiagnostics)
                return;

            Debug.Log(message, context != null ? context : this);
        }

        private void LogDropResult(
            string deathType,
            int carriedCoins,
            string bagId,
            string sceneName,
            Vector3 position,
            bool succeeded,
            string reason)
        {
            LogDiagnostic(
                $"[CURRENCY][DROP] type={deathType} carried={carriedCoins} " +
                $"bagId='{(string.IsNullOrEmpty(bagId) ? "<none>" : bagId)}' " +
                $"scene='{(string.IsNullOrEmpty(sceneName) ? "<none>" : sceneName)}' " +
                $"position={FormatPosition(position)} result={(succeeded ? "success" : "failed")} " +
                $"reason={reason}");
        }

        private static string FormatPosition(Vector3 position)
        {
            return $"({position.x:0.###}, {position.y:0.###}, {position.z:0.###})";
        }

        [ContextMenu("Diagnostics/Log Currency Snapshot")]
        private void LogCurrencySnapshot()
        {
            if (data == null)
            {
                Debug.Log("[CURRENCY][SNAPSHOT] Economy has not been loaded.", this);
                return;
            }

            RemoveDestroyedLiveBagReferences();
            List<string> bagIds = new List<string>(bagRecords.Keys);
            bagIds.Sort(StringComparer.Ordinal);
            List<string> lines = new List<string>
            {
                $"[CURRENCY][SNAPSHOT] temporary={usesTemporaryEconomy} " +
                $"carried={data.carriedCoins} banked={data.bankedCoins} " +
                $"registeredBags={bagRecords.Count} liveBags={liveBags.Count}"
            };

            for (int i = 0; i < bagIds.Count; i++)
            {
                string bagId = bagIds[i];
                DroppedCoinBagSaveData bag = bagRecords[bagId];
                liveBags.TryGetValue(bagId, out DroppedCoinBag liveBag);
                lines.Add(
                    $"  bagId='{bagId}' amount={bag.coinAmount} scene='{bag.sceneName}' " +
                    $"position={FormatPosition(bag.position)} live={liveBag != null} " +
                    $"pickupArmed={(liveBag != null && liveBag.PickupArmed)}");
            }

            Debug.Log(string.Join("\n", lines), this);
        }

        private static bool TryAdd(int current, int amount, out int result)
        {
            result = current;
            if (current < 0 || amount <= 0 || current > int.MaxValue - amount)
                return false;

            result = current + amount;
            return true;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeFreshEconomyMenu()
        {
            UnityEditor.EditorApplication.delayCall -= RefreshFreshEconomyMenu;
            UnityEditor.EditorApplication.delayCall += RefreshFreshEconomyMenu;
        }

        [UnityEditor.MenuItem(FreshEconomyMenuPath, false, 2010)]
        private static void ToggleFreshEconomyEveryPlay()
        {
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            bool enabled = !IsFreshEconomyEveryPlayEnabled();
            UnityEditor.EditorPrefs.SetBool(FreshEconomyPreferenceKey, enabled);
            RefreshFreshEconomyMenu();

            Debug.Log(
                $"[CURRENCY][TEST MODE] Fresh Economy Every Play " +
                $"{(enabled ? "enabled" : "disabled")}. " +
                "The change applies on the next Play Mode entry.");
        }

        [UnityEditor.MenuItem(FreshEconomyMenuPath, true)]
        private static bool ValidateFreshEconomyEveryPlay()
        {
            RefreshFreshEconomyMenu();
            return !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static bool IsFreshEconomyEveryPlayEnabled()
        {
            return UnityEditor.EditorPrefs.GetBool(FreshEconomyPreferenceKey, true);
        }

        private static void RefreshFreshEconomyMenu()
        {
            UnityEditor.Menu.SetChecked(
                FreshEconomyMenuPath,
                IsFreshEconomyEveryPlayEnabled());
        }
#endif
    }
}

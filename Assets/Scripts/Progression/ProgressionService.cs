using System;
using System.Collections.Generic;
using Game.Interaction;
using Game.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Progression
{
    public sealed class AbilityLoadoutDraft
    {
        internal readonly ProgressionService Owner;
        internal readonly string[] Slots;

        internal AbilityLoadoutDraft(ProgressionService owner, string[] slots)
        {
            Owner = owner;
            Slots = slots;
        }

        public string GetAbilityId(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < Slots.Length
                ? Slots[slotIndex]
                : string.Empty;
        }

        public string[] CopyAbilityIds()
        {
            return (string[])Slots.Clone();
        }
    }

    [DefaultExecutionOrder(-9500)]
    public sealed class ProgressionService : MonoBehaviour
    {
        private const string ServiceObjectName = "[ProgressionService]";

#if UNITY_EDITOR
        private const string FreshProgressionPreferenceKey =
            "SpaceWorld.Progression.FreshProgressionEveryPlay";
        private const string FreshProgressionMenuPath =
            "SpaceWorld/Progression/Fresh Progression Every Play";
#endif

        private static ProgressionService instance;

        private readonly Dictionary<string, TokenDefinition> tokenDefinitions =
            new Dictionary<string, TokenDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, AbilityDefinition> abilityDefinitions =
            new Dictionary<string, AbilityDefinition>(StringComparer.Ordinal);
        private readonly List<AbilityDefinition> abilityDefinitionList =
            new List<AbilityDefinition>();
        private readonly Dictionary<string, CollectedTokenSaveData> collectedTokens =
            new Dictionary<string, CollectedTokenSaveData>(StringComparer.Ordinal);
        private readonly HashSet<string> unlockedAbilityIds =
            new HashSet<string>(StringComparer.Ordinal);

        private ProgressionSaveData data;
        private bool usesTemporaryProgression;

        public static ProgressionService Instance
        {
            get
            {
                EnsureInstance();
                return instance;
            }
        }

        public bool UsesTemporaryProgression => usesTemporaryProgression;
        public int CollectedTokenCount => collectedTokens.Count;
        public int UnlockedAbilityCount => unlockedAbilityIds.Count;
        public IReadOnlyList<AbilityDefinition> AllAbilityDefinitions => abilityDefinitionList;

        public event Action OnProgressionChanged;
        public event Action OnProgressionLoaded;
        public event Action<TokenDefinition> OnTokenCollected;
        public event Action<AbilityDefinition> OnAbilityUnlocked;

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

            instance = FindObjectOfType<ProgressionService>(true);
            if (instance != null)
                return;

            GameObject serviceObject = new GameObject(ServiceObjectName);
            instance = serviceObject.AddComponent<ProgressionService>();
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
            BuildDefinitionRegistries();
            LoadForCurrentMode();
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void Start()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && scene.isLoaded)
                HandleSceneLoaded(scene, LoadSceneMode.Single);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (instance == this)
                instance = null;
        }

        public bool IsTokenCollected(string sceneName, string pickupId)
        {
            EnsureData();
            string key = ProgressionSaveData.BuildPickupKey(sceneName, pickupId);
            return !string.IsNullOrEmpty(key) && collectedTokens.ContainsKey(key);
        }

        public bool TryCollectToken(
            string sceneName,
            string pickupId,
            TokenDefinition definition)
        {
            EnsureData();
            string key = ProgressionSaveData.BuildPickupKey(sceneName, pickupId);
            string definitionId = definition != null ? definition.TokenTypeId : string.Empty;
            if (string.IsNullOrEmpty(key) ||
                string.IsNullOrEmpty(definitionId) ||
                collectedTokens.ContainsKey(key) ||
                !tokenDefinitions.ContainsKey(definitionId))
            {
                return false;
            }

            CollectedTokenSaveData record = new CollectedTokenSaveData
            {
                pickupKey = key,
                tokenDefinitionId = definitionId
            };
            collectedTokens.Add(key, record);
            if (!CommitCurrentState())
            {
                collectedTokens.Remove(key);
                SyncRuntimeCollectionsToData();
                return false;
            }

            OnTokenCollected?.Invoke(definition);
            OnProgressionChanged?.Invoke();
            return true;
        }

        public bool IsAbilityUnlocked(string abilityId)
        {
            string normalizedId = ProgressionSaveData.NormalizeId(abilityId);
            return !string.IsNullOrEmpty(normalizedId) && unlockedAbilityIds.Contains(normalizedId);
        }

        public bool IsAbilityEquipped(string abilityId)
        {
            EnsureData();
            string normalizedId = ProgressionSaveData.NormalizeId(abilityId);
            if (string.IsNullOrEmpty(normalizedId))
                return false;

            for (int i = 0; i < data.equippedAbilityIds.Length; i++)
            {
                if (string.Equals(
                    data.equippedAbilityIds[i],
                    normalizedId,
                    StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public bool UnlockAbility(string abilityId)
        {
            EnsureData();
            string normalizedId = ProgressionSaveData.NormalizeId(abilityId);
            if (string.IsNullOrEmpty(normalizedId) ||
                !abilityDefinitions.TryGetValue(normalizedId, out AbilityDefinition definition) ||
                !unlockedAbilityIds.Add(normalizedId))
            {
                return false;
            }

            if (!CommitCurrentState())
            {
                unlockedAbilityIds.Remove(normalizedId);
                SyncRuntimeCollectionsToData();
                return false;
            }

            OnAbilityUnlocked?.Invoke(definition);
            OnProgressionChanged?.Invoke();
            return true;
        }

        public List<AbilityDefinition> GetUnlockedAbilityDefinitions()
        {
            List<AbilityDefinition> definitions = new List<AbilityDefinition>();
            for (int i = 0; i < abilityDefinitionList.Count; i++)
            {
                AbilityDefinition definition = abilityDefinitionList[i];
                if (definition != null && unlockedAbilityIds.Contains(definition.AbilityId))
                    definitions.Add(definition);
            }

            return definitions;
        }

        public AbilityDefinition GetAbilityDefinition(string abilityId)
        {
            string normalizedId = ProgressionSaveData.NormalizeId(abilityId);
            return abilityDefinitions.TryGetValue(normalizedId, out AbilityDefinition definition)
                ? definition
                : null;
        }

        public string GetEquippedAbilityId(int slotIndex)
        {
            EnsureData();
            return slotIndex >= 0 && slotIndex < ProgressionSaveData.LoadoutSlotCount
                ? data.equippedAbilityIds[slotIndex]
                : string.Empty;
        }

        public bool IsOverloadSlotAvailable(AbilityLoadoutDraft draft)
        {
            return OwnsDraft(draft) && AreNormalSlotsOccupied(draft.Slots);
        }

        public AbilityLoadoutDraft BeginLoadoutEdit(RestBench bench, PlayerController player)
        {
            EnsureData();
            return CanEditAtBench(bench, player)
                ? new AbilityLoadoutDraft(this, (string[])data.equippedAbilityIds.Clone())
                : null;
        }

        internal AbilityLoadoutDraft CreateReadOnlyLoadoutSnapshot()
        {
            EnsureData();
            return new AbilityLoadoutDraft(this, (string[])data.equippedAbilityIds.Clone());
        }

        public bool TryAssignAbility(
            AbilityLoadoutDraft draft,
            int slotIndex,
            string abilityId,
            out string error)
        {
            error = string.Empty;
            if (!OwnsDraft(draft) || slotIndex < 0 || slotIndex >= ProgressionSaveData.LoadoutSlotCount)
            {
                error = "Borrador o espacio no válido.";
                return false;
            }

            string normalizedId = ProgressionSaveData.NormalizeId(abilityId);
            if (!unlockedAbilityIds.Contains(normalizedId) || !abilityDefinitions.ContainsKey(normalizedId))
            {
                error = "La habilidad no está desbloqueada.";
                return false;
            }

            if (slotIndex == 3 && !AreNormalSlotsOccupied(draft.Slots))
            {
                error = "La sobrecarga requiere los tres espacios normales ocupados.";
                return false;
            }

            string[] candidate = (string[])draft.Slots.Clone();
            int sourceSlot = Array.IndexOf(candidate, normalizedId);
            if (sourceSlot == slotIndex)
                return true;

            string replacedId = candidate[slotIndex];
            candidate[slotIndex] = normalizedId;
            if (sourceSlot >= 0)
                candidate[sourceSlot] = replacedId;

            if (!ValidateLoadout(candidate, out error))
                return false;

            Array.Copy(candidate, draft.Slots, candidate.Length);
            return true;
        }

        public bool TryClearSlot(
            AbilityLoadoutDraft draft,
            int slotIndex,
            out string error)
        {
            error = string.Empty;
            if (!OwnsDraft(draft) || slotIndex < 0 || slotIndex >= ProgressionSaveData.LoadoutSlotCount)
            {
                error = "Borrador o espacio no válido.";
                return false;
            }

            string[] candidate = (string[])draft.Slots.Clone();
            candidate[slotIndex] = string.Empty;
            candidate = ProgressionSaveData.NormalizeAbilityLoadout(candidate, slotIndex);
            if (!ValidateLoadout(candidate, out error))
                return false;

            Array.Copy(candidate, draft.Slots, candidate.Length);
            return true;
        }

        public bool TryConfirmLoadout(
            RestBench bench,
            PlayerController player,
            AbilityLoadoutDraft draft,
            out string error)
        {
            error = string.Empty;
            if (!CanEditAtBench(bench, player))
            {
                error = "El equipamiento solo puede confirmarse mientras descansas en este banco.";
                return false;
            }

            if (!OwnsDraft(draft))
                return false;

            string[] normalized = ProgressionSaveData.NormalizeAbilityLoadout(draft.Slots);
            if (!ValidateLoadout(normalized, out error))
                return false;

            Array.Copy(normalized, draft.Slots, normalized.Length);

            string[] previous = (string[])data.equippedAbilityIds.Clone();
            data.equippedAbilityIds = normalized;
            if (!CommitCurrentState())
            {
                data.equippedAbilityIds = previous;
                SyncRuntimeCollectionsToData();
                error = "No se pudo guardar el equipamiento.";
                return false;
            }

            OnProgressionChanged?.Invoke();
            return true;
        }

        private static bool AreNormalSlotsOccupied(string[] abilityIds)
        {
            return abilityIds != null &&
                   abilityIds.Length == ProgressionSaveData.LoadoutSlotCount &&
                   !string.IsNullOrEmpty(ProgressionSaveData.NormalizeId(abilityIds[0])) &&
                   !string.IsNullOrEmpty(ProgressionSaveData.NormalizeId(abilityIds[1])) &&
                   !string.IsNullOrEmpty(ProgressionSaveData.NormalizeId(abilityIds[2]));
        }

        public bool ValidateLoadout(string[] abilityIds, out string error)
        {
            error = string.Empty;
            if (abilityIds == null || abilityIds.Length != ProgressionSaveData.LoadoutSlotCount)
            {
                error = "El equipamiento debe tener exactamente cuatro espacios.";
                return false;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < abilityIds.Length; i++)
            {
                string id = ProgressionSaveData.NormalizeId(abilityIds[i]);
                if (string.IsNullOrEmpty(id))
                    continue;
                if (!unlockedAbilityIds.Contains(id) || !abilityDefinitions.ContainsKey(id))
                {
                    error = $"La habilidad del espacio {i + 1} no está disponible.";
                    return false;
                }
                if (!seen.Add(id))
                {
                    error = "Una habilidad no puede ocupar dos espacios.";
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(abilityIds[3]) &&
                (string.IsNullOrEmpty(abilityIds[0]) ||
                 string.IsNullOrEmpty(abilityIds[1]) ||
                 string.IsNullOrEmpty(abilityIds[2])))
            {
                error = "La sobrecarga requiere los tres espacios normales ocupados.";
                return false;
            }

            return true;
        }

        public float CalculateEffectiveStat(
            PlayerStatType stat,
            float baseValue,
            AbilityLoadoutDraft draft = null)
        {
            EnsureData();
            List<PlayerStatModifier> permanent = new List<PlayerStatModifier>();
            foreach (CollectedTokenSaveData token in collectedTokens.Values)
            {
                if (tokenDefinitions.TryGetValue(token.tokenDefinitionId, out TokenDefinition definition))
                    permanent.Add(definition.PermanentModifier);
            }

            string[] loadout = OwnsDraft(draft) ? draft.Slots : data.equippedAbilityIds;
            List<PlayerStatModifier> temporary = new List<PlayerStatModifier>();
            for (int i = 0; i < loadout.Length; i++)
            {
                string id = ProgressionSaveData.NormalizeId(loadout[i]);
                if (string.IsNullOrEmpty(id) ||
                    !abilityDefinitions.TryGetValue(id, out AbilityDefinition ability))
                {
                    continue;
                }

                AddModifiers(temporary, ability.EquippedModifiers);
                if (i == 3)
                    AddModifiers(temporary, ability.OverloadPenalties);
            }

            return PlayerStatMath.Calculate(baseValue, stat, permanent, temporary);
        }

        public void ReloadProgression()
        {
            LoadForCurrentMode();
            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && scene.isLoaded)
                AttachStatsRuntime(scene);
        }

        private static bool CanEditAtBench(RestBench bench, PlayerController player)
        {
            return bench != null && player != null && bench.IsOccupiedBy(player);
        }

        private bool OwnsDraft(AbilityLoadoutDraft draft)
        {
            return draft != null && draft.Owner == this &&
                   draft.Slots != null &&
                   draft.Slots.Length == ProgressionSaveData.LoadoutSlotCount;
        }

        private static void AddModifiers(
            List<PlayerStatModifier> destination,
            PlayerStatModifier[] modifiers)
        {
            if (modifiers == null)
                return;
            for (int i = 0; i < modifiers.Length; i++)
                destination.Add(modifiers[i]);
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            AttachStatsRuntime(scene);
        }

        private void AttachStatsRuntime(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                PlayerController player = roots[i].GetComponentInChildren<PlayerController>(true);
                if (player == null)
                    continue;

                PlayerResources resources = player.GetComponent<PlayerResources>();
                if (resources == null)
                    return;

                PlayerStatsRuntime runtime = player.GetComponent<PlayerStatsRuntime>();
                if (runtime == null)
                    runtime = player.gameObject.AddComponent<PlayerStatsRuntime>();
                runtime.Bind(player, resources, this);

                PlayerUpdraftCape cape = player.GetComponent<PlayerUpdraftCape>();
                if (cape == null)
                    cape = player.gameObject.AddComponent<PlayerUpdraftCape>();
                cape.Bind(player, this);
                return;
            }
        }

        private void BuildDefinitionRegistries()
        {
            tokenDefinitions.Clear();
            abilityDefinitions.Clear();
            abilityDefinitionList.Clear();

            ProgressionRuntimeSettings settings = ProgressionRuntimeSettings.Load();
            if (settings == null)
            {
                Debug.LogWarning("[PROGRESSION] ProgressionRuntimeSettings is missing.", this);
                return;
            }

            TokenDefinition[] tokens = settings.TokenDefinitions;
            for (int i = 0; tokens != null && i < tokens.Length; i++)
            {
                TokenDefinition definition = tokens[i];
                string id = definition != null ? definition.TokenTypeId : string.Empty;
                if (string.IsNullOrEmpty(id) || tokenDefinitions.ContainsKey(id))
                {
                    Debug.LogWarning($"[PROGRESSION] Invalid or duplicate token definition '{id}'.", settings);
                    continue;
                }
                tokenDefinitions.Add(id, definition);
            }

            AbilityDefinition[] abilities = settings.AbilityDefinitions;
            for (int i = 0; abilities != null && i < abilities.Length; i++)
            {
                AbilityDefinition definition = abilities[i];
                string id = definition != null ? definition.AbilityId : string.Empty;
                if (string.IsNullOrEmpty(id) || abilityDefinitions.ContainsKey(id))
                {
                    Debug.LogWarning($"[PROGRESSION] Invalid or duplicate ability definition '{id}'.", settings);
                    continue;
                }
                abilityDefinitions.Add(id, definition);
                abilityDefinitionList.Add(definition);
            }

            abilityDefinitionList.Sort((left, right) =>
                string.CompareOrdinal(left.AbilityId, right.AbilityId));
        }

        private void LoadForCurrentMode()
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorPrefs.GetBool(FreshProgressionPreferenceKey, true))
            {
                usesTemporaryProgression = true;
                LoadData(new ProgressionSaveData());
                Debug.Log(
                    "[PROGRESSION][TEST MODE] Fresh in-memory progression active. " +
                    "The persistent progression was not loaded and will not be written.",
                    this);
                return;
            }
#endif

            usesTemporaryProgression = false;
            LoadData(ProgressionSaveSystem.LoadOrCreate());
        }

        private void LoadData(ProgressionSaveData loadedData)
        {
            data = loadedData ?? new ProgressionSaveData();
            data.Normalize();

            collectedTokens.Clear();
            for (int i = 0; i < data.collectedTokens.Count; i++)
            {
                CollectedTokenSaveData token = data.collectedTokens[i].Clone();
                collectedTokens.Add(token.pickupKey, token);
            }

            unlockedAbilityIds.Clear();
            for (int i = 0; i < data.unlockedAbilityIds.Count; i++)
            {
                string id = data.unlockedAbilityIds[i];
                if (abilityDefinitions.ContainsKey(id))
                    unlockedAbilityIds.Add(id);
            }

            SanitizeLoadedLoadout();
            OnProgressionLoaded?.Invoke();
            OnProgressionChanged?.Invoke();
        }

        private void SanitizeLoadedLoadout()
        {
            for (int i = 0; i < data.equippedAbilityIds.Length; i++)
            {
                string id = data.equippedAbilityIds[i];
                if (!string.IsNullOrEmpty(id) &&
                    (!unlockedAbilityIds.Contains(id) || !abilityDefinitions.ContainsKey(id)))
                {
                    data.equippedAbilityIds[i] = string.Empty;
                }
            }

            data.equippedAbilityIds =
                ProgressionSaveData.NormalizeAbilityLoadout(data.equippedAbilityIds);
            if (!ValidateLoadout(data.equippedAbilityIds, out _))
                data.equippedAbilityIds[3] = string.Empty;
        }

        private bool CommitCurrentState()
        {
            SyncRuntimeCollectionsToData();
            return usesTemporaryProgression || ProgressionSaveSystem.Save(data);
        }

        private void SyncRuntimeCollectionsToData()
        {
            data.saveVersion = ProgressionSaveData.CurrentSaveVersion;
            data.collectedTokens.Clear();
            foreach (CollectedTokenSaveData token in collectedTokens.Values)
                data.collectedTokens.Add(token.Clone());
            data.collectedTokens.Sort((left, right) =>
                string.CompareOrdinal(left.pickupKey, right.pickupKey));

            data.unlockedAbilityIds.Clear();
            foreach (string id in unlockedAbilityIds)
                data.unlockedAbilityIds.Add(id);
            data.unlockedAbilityIds.Sort(StringComparer.Ordinal);
            data.Normalize();
        }

        private void EnsureData()
        {
            if (data == null)
                LoadForCurrentMode();
        }

        [ContextMenu("Diagnostics/Log Progression Snapshot")]
        private void LogProgressionSnapshot()
        {
            EnsureData();
            Debug.Log(
                $"[PROGRESSION][SNAPSHOT] temporary={usesTemporaryProgression} " +
                $"tokens={collectedTokens.Count} unlocked={unlockedAbilityIds.Count} " +
                $"slots=[{string.Join(", ", data.equippedAbilityIds)}]",
                this);
        }

#if UNITY_EDITOR
        [ContextMenu("Development/Unlock All Abilities For Temporary Session")]
        private void UnlockAllAbilitiesForTemporarySession()
        {
            if (!Application.isPlaying || !usesTemporaryProgression)
            {
                Debug.LogWarning(
                    "[PROGRESSION] Development unlock is only allowed in Play Mode with " +
                    "Fresh Progression Every Play enabled.",
                    this);
                return;
            }

            for (int i = 0; i < abilityDefinitionList.Count; i++)
                UnlockAbility(abilityDefinitionList[i].AbilityId);
        }

        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeFreshProgressionMenu()
        {
            UnityEditor.EditorApplication.delayCall -= RefreshFreshProgressionMenu;
            UnityEditor.EditorApplication.delayCall += RefreshFreshProgressionMenu;
        }

        [UnityEditor.MenuItem(FreshProgressionMenuPath, false, 2020)]
        private static void ToggleFreshProgressionEveryPlay()
        {
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            bool enabled = !IsFreshProgressionEveryPlayEnabled();
            UnityEditor.EditorPrefs.SetBool(FreshProgressionPreferenceKey, enabled);
            RefreshFreshProgressionMenu();
            Debug.Log(
                $"[PROGRESSION][TEST MODE] Fresh Progression Every Play " +
                $"{(enabled ? "enabled" : "disabled")}. " +
                "The change applies on the next Play Mode entry.");
        }

        [UnityEditor.MenuItem(FreshProgressionMenuPath, true)]
        private static bool ValidateFreshProgressionEveryPlay()
        {
            RefreshFreshProgressionMenu();
            return !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static bool IsFreshProgressionEveryPlayEnabled()
        {
            return UnityEditor.EditorPrefs.GetBool(FreshProgressionPreferenceKey, true);
        }

        private static void RefreshFreshProgressionMenu()
        {
            UnityEditor.Menu.SetChecked(
                FreshProgressionMenuPath,
                IsFreshProgressionEveryPlayEnabled());
        }
#endif
    }
}

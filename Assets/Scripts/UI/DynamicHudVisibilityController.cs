using System.Collections;
using System.Collections.Generic;
using Game.Player;
using UnityEngine;

namespace Game.UI
{
    [DisallowMultipleComponent]
    public sealed class DynamicHudVisibilityController : MonoBehaviour
    {
        private enum HudGroupId
        {
            Vitals,
            Resources,
            Economy,
            Notifications,
            Count
        }

        private sealed class RuntimeGroup
        {
            public readonly List<CanvasGroup> Targets = new List<CanvasGroup>();
            public float Alpha;
            public float VisibleUntil;
            public bool Pinned;
        }

        [Header("Groups")]
        [SerializeField] private CanvasGroup vitalsGroup;
        [SerializeField] private CanvasGroup resourcesGroup;
        [SerializeField] private CanvasGroup economyGroup;
        [SerializeField] private CanvasGroup notificationGroup;

        [Header("Timing")]
        [SerializeField, Min(0f)] private float visibleDuration = 3f;
        [SerializeField, Min(0f)] private float fadeInDuration = 0.2f;
        [SerializeField, Min(0f)] private float fadeOutDuration = 0.45f;
        [SerializeField] private bool showOnStart = true;
        [SerializeField] private bool showOnRespawn = true;

        [Header("Vitals")]
        [SerializeField, Min(0)] private int lowHealthThreshold = 1;

        private readonly RuntimeGroup[] groups = new RuntimeGroup[(int)HudGroupId.Count];

        private PlayerResources playerResources;
        private PlayerHealth playerHealth;
        private PlayerMana playerMana;
        private PlayerStamina playerStamina;
        private bool usesManaResourceFallback;
        private bool usesStaminaResourceFallback;

        private bool hasHealthSnapshot;
        private int lastHealth;
        private int lastMaxHealth;
        private bool hasManaSnapshot;
        private int lastMana;
        private int lastMaxMana;
        private bool hasStaminaSnapshot;
        private int lastStamina;
        private int lastMaxStamina;

        private Coroutine visibilityRoutine;
        private bool initialized;

        private void Awake()
        {
            InitializeGroups();
        }

        private void OnEnable()
        {
            InitializeGroups();
            ResetRuntimeVisibility();
            BindPlayerSources();

            if (showOnStart)
                ShowAllGroups();
        }

        private void Start()
        {
            // PlayerResources executes early, so this is only a one-shot fallback for
            // unusual scene activation order. It never polls for the Player.
            if (playerResources == null)
                BindPlayerSources();
        }

        private void OnDisable()
        {
            UnbindPlayerSources();
            if (visibilityRoutine != null)
            {
                StopCoroutine(visibilityRoutine);
                visibilityRoutine = null;
            }
        }

        private void OnValidate()
        {
            visibleDuration = Mathf.Max(0f, visibleDuration);
            fadeInDuration = Mathf.Max(0f, fadeInDuration);
            fadeOutDuration = Mathf.Max(0f, fadeOutDuration);
            lowHealthThreshold = Mathf.Max(0, lowHealthThreshold);
        }

        public void ShowVitals()
        {
            ShowGroup(HudGroupId.Vitals);
        }

        public void ShowResources()
        {
            ShowGroup(HudGroupId.Resources);
        }

        public void ShowEconomy()
        {
            ShowGroup(HudGroupId.Economy);
        }

        public void ShowNotifications()
        {
            ShowGroup(HudGroupId.Notifications);
        }

        public void RegisterEconomyTarget(CanvasGroup target)
        {
            RegisterTarget(HudGroupId.Economy, target);
        }

        public void UnregisterEconomyTarget(CanvasGroup target)
        {
            UnregisterTarget(HudGroupId.Economy, target);
        }

        private void InitializeGroups()
        {
            if (initialized)
                return;

            for (int i = 0; i < groups.Length; i++)
                groups[i] = new RuntimeGroup();

            RegisterTarget(HudGroupId.Vitals, vitalsGroup);
            RegisterTarget(HudGroupId.Resources, resourcesGroup);
            RegisterTarget(HudGroupId.Economy, economyGroup);
            RegisterTarget(HudGroupId.Notifications, notificationGroup);
            initialized = true;
        }

        private void ResetRuntimeVisibility()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < groups.Length; i++)
            {
                RuntimeGroup group = groups[i];
                group.Alpha = 0f;
                group.VisibleUntil = now;
                group.Pinned = false;
                ApplyGroupVisual(group);
            }
        }

        private void RegisterTarget(HudGroupId id, CanvasGroup target)
        {
            if (target == null)
                return;

            if (!initialized && groups[(int)id] == null)
                groups[(int)id] = new RuntimeGroup();

            RuntimeGroup group = groups[(int)id];
            if (!group.Targets.Contains(target))
                group.Targets.Add(target);

            ApplyTargetVisual(target, group.Alpha);
        }

        private void UnregisterTarget(HudGroupId id, CanvasGroup target)
        {
            if (target == null || groups[(int)id] == null)
                return;

            groups[(int)id].Targets.Remove(target);
        }

        private void ShowAllGroups()
        {
            for (int i = 0; i < groups.Length; i++)
                ShowGroup((HudGroupId)i);
        }

        private void ShowGroup(HudGroupId id)
        {
            InitializeGroups();
            RuntimeGroup group = groups[(int)id];
            group.VisibleUntil = Time.unscaledTime + visibleDuration;
            EnsureVisibilityRoutine();
        }

        private void SetPinned(HudGroupId id, bool pinned)
        {
            RuntimeGroup group = groups[(int)id];
            if (group.Pinned == pinned)
                return;

            group.Pinned = pinned;
            if (pinned)
                group.VisibleUntil = Mathf.Max(group.VisibleUntil, Time.unscaledTime);
            EnsureVisibilityRoutine();
        }

        private void EnsureVisibilityRoutine()
        {
            if (!isActiveAndEnabled || visibilityRoutine != null)
                return;

            visibilityRoutine = StartCoroutine(UpdateVisibilityWhileNeeded());
        }

        private IEnumerator UpdateVisibilityWhileNeeded()
        {
            while (true)
            {
                bool hasActiveWork = false;
                float now = Time.unscaledTime;
                float delta = Time.unscaledDeltaTime;

                for (int i = 0; i < groups.Length; i++)
                {
                    RuntimeGroup group = groups[i];
                    bool shouldBeVisible = group.Pinned || now < group.VisibleUntil;
                    float targetAlpha = shouldBeVisible ? 1f : 0f;
                    float duration = shouldBeVisible ? fadeInDuration : fadeOutDuration;
                    float previousAlpha = group.Alpha;

                    group.Alpha = duration <= 0f
                        ? targetAlpha
                        : Mathf.MoveTowards(group.Alpha, targetAlpha, delta / duration);

                    if (!Mathf.Approximately(previousAlpha, group.Alpha))
                        ApplyGroupVisual(group);

                    bool waitingForDeadline = !group.Pinned && now < group.VisibleUntil;
                    bool fading = !Mathf.Approximately(group.Alpha, targetAlpha);
                    hasActiveWork |= waitingForDeadline || fading;
                }

                if (!hasActiveWork)
                    break;

                yield return null;
            }

            visibilityRoutine = null;
        }

        private static void ApplyTargetVisual(CanvasGroup target, float alpha)
        {
            if (target == null)
                return;

            target.alpha = Mathf.Clamp01(alpha);
            // These groups are informational. They must never consume UI input,
            // whether visible or hidden.
            target.interactable = false;
            target.blocksRaycasts = false;
        }

        private static void RemoveMissingTargets(RuntimeGroup group)
        {
            for (int i = group.Targets.Count - 1; i >= 0; i--)
            {
                if (group.Targets[i] == null)
                    group.Targets.RemoveAt(i);
            }
        }

        private static void ApplyGroupVisual(RuntimeGroup group)
        {
            RemoveMissingTargets(group);
            for (int i = 0; i < group.Targets.Count; i++)
                ApplyTargetVisual(group.Targets[i], group.Alpha);
        }

        private void BindPlayerSources()
        {
            UnbindPlayerSources();

            playerResources = FindFirstObjectByType<PlayerResources>();
            if (playerResources == null)
                return;

            playerHealth = playerResources.GetComponent<PlayerHealth>();
            playerMana = playerResources.GetComponent<PlayerMana>();
            playerStamina = playerResources.GetComponent<PlayerStamina>();

            if (playerResources.Health != null)
            {
                CaptureHealth(playerResources.Health.Current, playerResources.Health.Max, false);
                playerResources.Health.OnChanged += HandleHealthChanged;
            }

            if (playerHealth != null)
            {
                playerHealth.OnDamaged += HandlePlayerDamaged;
                playerHealth.OnDied += HandlePlayerDied;
                playerHealth.OnRespawned += HandlePlayerRespawned;
            }

            if (playerMana != null)
            {
                CaptureMana(playerMana.CurrentMana, playerMana.MaxMana, false);
                playerMana.OnManaChanged += HandleManaChanged;
            }
            else if (playerResources.Mana != null)
            {
                usesManaResourceFallback = true;
                CaptureMana(playerResources.Mana.Current, playerResources.Mana.Max, false);
                playerResources.Mana.OnChanged += HandleManaChanged;
            }

            if (playerStamina != null)
            {
                CaptureStamina(playerStamina.CurrentStamina, playerStamina.MaxStamina, false);
                playerStamina.OnStaminaChanged += HandleStaminaChanged;
            }
            else
            {
                usesStaminaResourceFallback = true;
                CaptureStamina(playerResources.CurrentStamina, playerResources.MaxStamina, false);
                playerResources.OnStaminaChanged += HandleStaminaChanged;
            }
        }

        private void UnbindPlayerSources()
        {
            if (playerResources != null)
            {
                if (playerResources.Health != null)
                    playerResources.Health.OnChanged -= HandleHealthChanged;
                if (usesManaResourceFallback && playerResources.Mana != null)
                    playerResources.Mana.OnChanged -= HandleManaChanged;
                if (usesStaminaResourceFallback)
                    playerResources.OnStaminaChanged -= HandleStaminaChanged;
            }

            if (playerHealth != null)
            {
                playerHealth.OnDamaged -= HandlePlayerDamaged;
                playerHealth.OnDied -= HandlePlayerDied;
                playerHealth.OnRespawned -= HandlePlayerRespawned;
            }

            if (playerMana != null)
                playerMana.OnManaChanged -= HandleManaChanged;
            if (playerStamina != null)
                playerStamina.OnStaminaChanged -= HandleStaminaChanged;

            playerResources = null;
            playerHealth = null;
            playerMana = null;
            playerStamina = null;
            usesManaResourceFallback = false;
            usesStaminaResourceFallback = false;
            hasHealthSnapshot = false;
            hasManaSnapshot = false;
            hasStaminaSnapshot = false;
        }

        private void HandleHealthChanged(int current, int max)
        {
            CaptureHealth(current, max, true);
        }

        private void CaptureHealth(int current, int max, bool notify)
        {
            bool changed = hasHealthSnapshot && (current != lastHealth || max != lastMaxHealth);
            lastHealth = current;
            lastMaxHealth = max;
            hasHealthSnapshot = true;

            bool isCritical = max > 0 && current <= lowHealthThreshold;
            SetPinned(HudGroupId.Vitals, isCritical);
            if (notify && changed)
                ShowVitals();
        }

        private void HandleManaChanged(int current, int max)
        {
            CaptureMana(current, max, true);
        }

        private void CaptureMana(int current, int max, bool notify)
        {
            bool changed = hasManaSnapshot && (current != lastMana || max != lastMaxMana);
            lastMana = current;
            lastMaxMana = max;
            hasManaSnapshot = true;
            if (notify && changed)
                ShowResources();
        }

        private void HandleStaminaChanged(int current, int max)
        {
            CaptureStamina(current, max, true);
        }

        private void CaptureStamina(int current, int max, bool notify)
        {
            bool changed = hasStaminaSnapshot &&
                (current != lastStamina || max != lastMaxStamina);
            lastStamina = current;
            lastMaxStamina = max;
            hasStaminaSnapshot = true;
            if (notify && changed)
                ShowResources();
        }

        private void HandlePlayerDamaged(int damage)
        {
            ShowVitals();
        }

        private void HandlePlayerDied()
        {
            ShowVitals();
        }

        private void HandlePlayerRespawned()
        {
            if (playerResources != null && playerResources.Health != null)
            {
                CaptureHealth(
                    playerResources.Health.Current,
                    playerResources.Health.Max,
                    false);
            }

            if (showOnRespawn)
                ShowAllGroups();
        }
    }
}

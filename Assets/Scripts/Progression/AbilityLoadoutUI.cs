using System.Collections.Generic;
using System.Text;
using Game.Input;
using Game.Interaction;
using Game.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Progression
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class AbilityLoadoutUI : MonoBehaviour
    {
        private static AbilityLoadoutUI instance;
        private static bool missingInstanceWarningShown;

        [Header("Optional prebuilt references (runtime fallback is created when empty)")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private RectTransform inventoryRoot;
        [SerializeField] private Text[] slotTexts = new Text[ProgressionSaveData.LoadoutSlotCount];
        [SerializeField] private Text detailsText;
        [SerializeField] private Text previewText;
        [SerializeField] private Text statusText;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;

        private ProgressionService service;
        private RestBench activeBench;
        private PlayerController activePlayer;
        private PlayerStatsRuntime statsRuntime;
        private InputReader inputReader;
        private PlayerHealth playerHealth;
        private AbilityLoadoutDraft draft;
        private string selectedAbilityId = string.Empty;
        private Font runtimeFont;
        private Button firstInventoryButton;
        private bool readOnlyMode;
        private bool ownsExternalControlLock;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf && draft != null;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            BuildRuntimeViewIfNeeded();
            CloseWithoutSaving();
        }

        private void OnEnable()
        {
            if (instance == null)
                instance = this;
            service = ProgressionService.Instance;
            service.OnProgressionChanged -= HandleProgressionChanged;
            service.OnProgressionChanged += HandleProgressionChanged;
            ResolvePlayerReferences();
            SubscribeToHealth();
        }

        private void OnDisable()
        {
            if (service != null)
                service.OnProgressionChanged -= HandleProgressionChanged;
            UnsubscribeFromHealth();
            CloseWithoutSaving();
            if (instance == this)
                instance = null;
        }

        private void Update()
        {
            ResolvePlayerReferences();
            if (inputReader == null || !inputReader.ConsumeSkillsPressed())
                return;

            LogSkillsToggleRequest(this);

            // RestBench has an earlier execution order and consumes this action while seated.
            if (activeBench != null)
            {
                LogSkillsToggleResult("ignored", "bench-owned-input", this);
                return;
            }

            if (IsOpen)
            {
                CloseWithoutSaving();
                LogSkillsToggleResult("close", "none", this);
                return;
            }

            if (panelRoot == null)
            {
                LogSkillsToggleResult("ignored", "no-ui", this);
                return;
            }
            if (activePlayer == null)
            {
                LogSkillsToggleResult("ignored", "no-player", this);
                return;
            }
            if (activePlayer.IsExternalControlLocked)
            {
                LogSkillsToggleResult("ignored", "external-lock", this);
                return;
            }

            bool opened = OpenReadOnly(activePlayer);
            LogSkillsToggleResult(
                opened ? "open" : "ignored",
                opened ? "none" : "loadout-unavailable",
                this);
        }

        public static bool ToggleForBench(RestBench bench, PlayerController player)
        {
            LogSkillsToggleRequest(instance);
            if (instance == null)
            {
                LogSkillsToggleResult("ignored", "no-ui", null);
                if (!missingInstanceWarningShown)
                {
                    missingInstanceWarningShown = true;
                    Debug.LogWarning(
                        "[PROGRESSION UI] No AbilityLoadoutUI is active. Add the " +
                        "AbilityLoadoutUI prefab as a child of the gameplay Canvas.");
                }
                return false;
            }

            if (instance.IsOpen)
            {
                instance.CloseWithoutSaving();
                LogSkillsToggleResult("close", "none", instance);
                return true;
            }

            bool opened = instance.Open(bench, player);
            LogSkillsToggleResult(
                opened ? "open" : "ignored",
                opened ? "none" : "loadout-unavailable",
                instance);
            return opened;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private static void LogSkillsToggleRequest(Object context)
        {
            Debug.Log("[SKILLS UI] toggle-request", context);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private static void LogSkillsToggleResult(
            string result,
            string reason,
            Object context)
        {
            Debug.Log($"[SKILLS UI] result={result} reason={reason}", context);
        }

        public static void NotifyBenchExited(RestBench bench)
        {
            if (instance != null && instance.activeBench == bench)
                instance.CloseWithoutSaving();
        }

        private bool Open(RestBench bench, PlayerController player)
        {
            service = ProgressionService.Instance;
            draft = service.BeginLoadoutEdit(bench, player);
            if (draft == null)
                return false;

            activeBench = bench;
            activePlayer = player;
            readOnlyMode = false;
            statsRuntime = player != null ? player.GetComponent<PlayerStatsRuntime>() : null;
            selectedAbilityId = string.Empty;
            panelRoot.SetActive(true);
            SetStatus("Selecciona una habilidad y después un espacio. Sin selección, pulsa un espacio para vaciarlo.");
            RefreshView();
            SelectFirstControl();
            return true;
        }

        private bool OpenReadOnly(PlayerController player)
        {
            if (player == null)
                return false;

            service = ProgressionService.Instance;
            draft = service.CreateReadOnlyLoadoutSnapshot();
            activeBench = null;
            activePlayer = player;
            readOnlyMode = true;
            statsRuntime = player.GetComponent<PlayerStatsRuntime>();
            selectedAbilityId = string.Empty;
            player.SetExternalControlLocked(true);
            ownsExternalControlLock = true;
            DiscardGameplayInputs();
            panelRoot.SetActive(true);
            SetStatus("Modo consulta. El equipamiento solo puede modificarse descansando en un banco.");
            RefreshView();
            SelectFirstControl();
            return true;
        }

        private void CloseWithoutSaving()
        {
            if (ownsExternalControlLock && activePlayer != null)
                activePlayer.SetExternalControlLocked(false);
            ownsExternalControlLock = false;
            draft = null;
            activeBench = null;
            activePlayer = null;
            statsRuntime = null;
            selectedAbilityId = string.Empty;
            readOnlyMode = false;
            if (panelRoot != null)
                panelRoot.SetActive(false);
            DiscardGameplayInputs();
        }

        private void HandleProgressionChanged()
        {
            if (IsOpen)
                RefreshView();
        }

        private void HandleAbilitySelected(string abilityId)
        {
            selectedAbilityId = ProgressionSaveData.NormalizeId(abilityId);
            SetStatus("Habilidad seleccionada. Elige un espacio.");
            RefreshView();
        }

        private void HandleSlotPressed(int slotIndex)
        {
            if (draft == null)
                return;

            if (readOnlyMode)
            {
                SetStatus("Modo consulta: descansa en un banco para modificar los espacios.");
                return;
            }

            bool succeeded;
            string error;
            if (string.IsNullOrEmpty(selectedAbilityId))
            {
                succeeded = service.TryClearSlot(draft, slotIndex, out error);
            }
            else
            {
                succeeded = service.TryAssignAbility(
                    draft,
                    slotIndex,
                    selectedAbilityId,
                    out error);
            }

            if (succeeded)
            {
                selectedAbilityId = string.Empty;
                SetStatus("Configuración provisional actualizada. Confirma para guardarla.");
            }
            else
            {
                SetStatus(error);
            }

            RefreshView();
        }

        private void HandleConfirmPressed()
        {
            if (draft == null)
                return;

            if (!service.TryConfirmLoadout(
                    activeBench,
                    activePlayer,
                    draft,
                    out string error))
            {
                SetStatus(error);
                return;
            }

            CloseWithoutSaving();
        }

        private void RefreshView()
        {
            if (draft == null)
                return;

            RebuildInventoryButtons();
            bool overloadAvailable = service.IsOverloadSlotAvailable(draft);
            for (int i = 0; i < slotTexts.Length; i++)
            {
                if (slotTexts[i] == null)
                    continue;
                string id = draft.GetAbilityId(i);
                AbilityDefinition definition = service.GetAbilityDefinition(id);
                bool overloadLocked = i == 3 && !overloadAvailable;
                string slotName = i == 3
                    ? "4 — SOBRECARGA" + (overloadLocked ? " [BLOQUEADO]" : string.Empty)
                    : (i + 1).ToString();
                slotTexts[i].text = $"Espacio {slotName}\n" +
                    (definition != null ? definition.DisplayName : "— vacío —");

                Button slotButton = slotTexts[i].GetComponentInParent<Button>();
                if (slotButton != null)
                    slotButton.interactable = !readOnlyMode && !overloadLocked;
            }

            AbilityDefinition selected = service.GetAbilityDefinition(selectedAbilityId);
            if (detailsText != null)
            {
                detailsText.text = selected == null
                    ? "Selecciona una habilidad para ver su información."
                    : BuildAbilityDetails(selected);
            }

            RefreshStatPreview();
            if (confirmButton != null)
                confirmButton.gameObject.SetActive(!readOnlyMode);
            if (cancelButton != null)
            {
                Text cancelLabel = cancelButton.GetComponentInChildren<Text>();
                if (cancelLabel != null)
                    cancelLabel.text = readOnlyMode ? "CERRAR" : "CANCELAR";
            }
        }

        private void RebuildInventoryButtons()
        {
            if (inventoryRoot == null)
                return;

            firstInventoryButton = null;

            for (int i = inventoryRoot.childCount - 1; i >= 0; i--)
                Destroy(inventoryRoot.GetChild(i).gameObject);

            List<AbilityDefinition> unlocked = service.GetUnlockedAbilityDefinitions();
            if (unlocked.Count == 0)
            {
                CreateText(
                    "NoAbilities",
                    inventoryRoot,
                    "No hay habilidades desbloqueadas.\nEn pruebas: usa el menú contextual de [ProgressionService].",
                    16,
                    TextAnchor.UpperLeft,
                    new Vector2(0f, -10f),
                    new Vector2(330f, 90f));
                return;
            }

            for (int i = 0; i < unlocked.Count; i++)
            {
                AbilityDefinition definition = unlocked[i];
                string abilityId = definition.AbilityId;
                Button button = CreateButton(
                    "Ability_" + abilityId,
                    inventoryRoot,
                    definition.DisplayName,
                    new Vector2(0f, -i * 54f),
                    new Vector2(330f, 46f));
                button.onClick.AddListener(() => HandleAbilitySelected(abilityId));
                if (firstInventoryButton == null)
                    firstInventoryButton = button;
            }
        }

        private void SelectFirstControl()
        {
            if (EventSystem.current == null)
                return;
            GameObject target = firstInventoryButton != null
                ? firstInventoryButton.gameObject
                : cancelButton != null ? cancelButton.gameObject : null;
            EventSystem.current.SetSelectedGameObject(target);
        }

        private void ResolvePlayerReferences()
        {
            if (activePlayer == null)
                activePlayer = FindObjectOfType<PlayerController>(true);
            if (activePlayer == null)
                return;
            if (inputReader == null)
                inputReader = activePlayer.GetComponent<InputReader>();
            if (playerHealth == null)
            {
                playerHealth = activePlayer.GetComponent<PlayerHealth>();
                SubscribeToHealth();
            }
        }

        private void SubscribeToHealth()
        {
            if (playerHealth == null)
                return;
            playerHealth.OnDamaged -= HandlePlayerInterrupted;
            playerHealth.OnDied -= HandlePlayerInterrupted;
            playerHealth.OnRespawned -= HandlePlayerInterrupted;
            playerHealth.OnDamaged += HandlePlayerInterrupted;
            playerHealth.OnDied += HandlePlayerInterrupted;
            playerHealth.OnRespawned += HandlePlayerInterrupted;
        }

        private void UnsubscribeFromHealth()
        {
            if (playerHealth == null)
                return;
            playerHealth.OnDamaged -= HandlePlayerInterrupted;
            playerHealth.OnDied -= HandlePlayerInterrupted;
            playerHealth.OnRespawned -= HandlePlayerInterrupted;
        }

        private void HandlePlayerInterrupted(int damage)
        {
            CloseWithoutSaving();
        }

        private void HandlePlayerInterrupted()
        {
            CloseWithoutSaving();
        }

        private void DiscardGameplayInputs()
        {
            if (inputReader == null)
                return;
            inputReader.ConsumeJumpPressed();
            inputReader.ConsumeAttackPressed();
            inputReader.ConsumeDashPressed();
            inputReader.ConsumeParryPressed();
            inputReader.ConsumeInteractPressed();
            inputReader.CancelCrouchGesture("AbilityLoadoutUI");
            inputReader.ClearSwingGrabBuffer();
        }

        private string BuildAbilityDetails(AbilityDefinition definition)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(definition.DisplayName);
            builder.AppendLine(definition.Description);
            builder.Append("Efecto: ");
            builder.AppendLine(FormatModifiers(definition.EquippedModifiers, "sin modificador estadístico"));
            builder.Append("Sobrecarga: ");
            builder.Append(FormatModifiers(definition.OverloadPenalties, "sin penalización"));
            return builder.ToString();
        }

        private void RefreshStatPreview()
        {
            if (previewText == null || activePlayer == null)
                return;

            CharacterStats characterStats = activePlayer.stats;
            PlayerResources resources = activePlayer.GetComponent<PlayerResources>();
            if (characterStats == null || resources == null)
            {
                previewText.text = "No se pudieron resolver las estadísticas del Player.";
                return;
            }

            float baseSpeed = statsRuntime != null ? statsRuntime.BaseRunSpeed : characterStats.maxRunSpeed;
            float baseJump = statsRuntime != null ? statsRuntime.BaseJumpForce : characterStats.jumpForce;
            float baseHealth = statsRuntime != null ? statsRuntime.BaseMaxHealth : resources.ConfiguredMaxHealth;
            float baseStamina = statsRuntime != null ? statsRuntime.BaseMaxStamina : resources.ConfiguredMaxStamina;

            previewText.text =
                "Vista previa (confirmado → provisional)\n" +
                FormatPreviewLine(PlayerStatType.MovementSpeed, "Velocidad", baseSpeed) + "\n" +
                FormatPreviewLine(PlayerStatType.JumpForce, "Salto", baseJump) + "\n" +
                FormatPreviewLine(PlayerStatType.MaxHealth, "Vida máxima", baseHealth, true) + "\n" +
                FormatPreviewLine(PlayerStatType.MaxStamina, "Stamina máxima", baseStamina, true);
        }

        private string FormatPreviewLine(
            PlayerStatType stat,
            string label,
            float baseValue,
            bool integer = false)
        {
            float current = service.CalculateEffectiveStat(stat, baseValue);
            float preview = service.CalculateEffectiveStat(stat, baseValue, draft);
            return integer
                ? $"{label}: {Mathf.RoundToInt(current)} → {Mathf.RoundToInt(preview)}"
                : $"{label}: {current:0.##} → {preview:0.##}";
        }

        private static string FormatModifiers(
            PlayerStatModifier[] modifiers,
            string emptyText)
        {
            if (modifiers == null || modifiers.Length == 0)
                return emptyText;

            List<string> parts = new List<string>();
            for (int i = 0; i < modifiers.Length; i++)
            {
                PlayerStatModifier modifier = modifiers[i];
                string stat = GetStatLabel(modifier.stat);
                if (Mathf.Abs(modifier.flatAmount) > 0.0001f)
                    parts.Add($"{stat} {modifier.flatAmount:+0.##;-0.##}");
                if (Mathf.Abs(modifier.percentAmount) > 0.0001f)
                    parts.Add($"{stat} {modifier.percentAmount * 100f:+0.#;-0.#}%");
            }
            return parts.Count > 0 ? string.Join(", ", parts) : emptyText;
        }

        private static string GetStatLabel(PlayerStatType stat)
        {
            switch (stat)
            {
                case PlayerStatType.MovementSpeed:
                    return "Velocidad";
                case PlayerStatType.JumpForce:
                    return "Salto";
                case PlayerStatType.MaxHealth:
                    return "Vida";
                case PlayerStatType.MaxStamina:
                    return "Stamina";
                default:
                    return stat.ToString();
            }
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }

        private void BuildRuntimeViewIfNeeded()
        {
            if (panelRoot != null)
                return;

            runtimeFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            RectTransform host = (RectTransform)transform;
            host.anchorMin = Vector2.zero;
            host.anchorMax = Vector2.one;
            host.offsetMin = Vector2.zero;
            host.offsetMax = Vector2.zero;

            panelRoot = CreateRect("Panel", host, Vector2.zero, new Vector2(980f, 680f)).gameObject;
            Image panelImage = panelRoot.AddComponent<Image>();
            panelImage.color = new Color(0.035f, 0.05f, 0.08f, 0.97f);

            CreateText("Title", panelRoot.transform, "HABILIDADES — H / START para cancelar", 28,
                TextAnchor.MiddleCenter, new Vector2(0f, 315f), new Vector2(900f, 42f));
            CreateText("InventoryLabel", panelRoot.transform, "Habilidades desbloqueadas", 19,
                TextAnchor.MiddleLeft, new Vector2(-445f, 265f), new Vector2(340f, 34f));
            CreateText("SlotsLabel", panelRoot.transform, "Equipamiento", 19,
                TextAnchor.MiddleLeft, new Vector2(-50f, 265f), new Vector2(250f, 34f));

            inventoryRoot = CreateRect(
                "Inventory",
                panelRoot.transform,
                new Vector2(-445f, 215f),
                new Vector2(340f, 250f));

            slotTexts = new Text[ProgressionSaveData.LoadoutSlotCount];
            for (int i = 0; i < slotTexts.Length; i++)
            {
                int slotIndex = i;
                Button slotButton = CreateButton(
                    "Slot" + (i + 1),
                    panelRoot.transform,
                    "",
                    new Vector2(-50f, 215f - i * 64f),
                    new Vector2(420f, 54f));
                slotTexts[i] = slotButton.GetComponentInChildren<Text>();
                slotButton.onClick.AddListener(() => HandleSlotPressed(slotIndex));
            }

            detailsText = CreateText("Details", panelRoot.transform, "", 16,
                TextAnchor.UpperLeft, new Vector2(-445f, -100f), new Vector2(430f, 170f));
            previewText = CreateText("Preview", panelRoot.transform, "", 16,
                TextAnchor.UpperLeft, new Vector2(10f, -100f), new Vector2(420f, 170f));
            statusText = CreateText("Status", panelRoot.transform, "", 15,
                TextAnchor.MiddleCenter, new Vector2(0f, -265f), new Vector2(850f, 48f));

            confirmButton = CreateButton("Confirm", panelRoot.transform, "CONFIRMAR",
                new Vector2(155f, -315f), new Vector2(220f, 50f));
            cancelButton = CreateButton("Cancel", panelRoot.transform, "CANCELAR",
                new Vector2(-155f, -315f), new Vector2(220f, 50f));
            confirmButton.onClick.AddListener(HandleConfirmPressed);
            cancelButton.onClick.AddListener(CloseWithoutSaving);
        }

        private RectTransform CreateRect(
            string objectName,
            Transform parent,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            GameObject child = new GameObject(objectName, typeof(RectTransform));
            child.layer = parent.gameObject.layer;
            RectTransform rect = child.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return rect;
        }

        private Text CreateText(
            string objectName,
            Transform parent,
            string content,
            int fontSize,
            TextAnchor alignment,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            RectTransform rect = CreateRect(objectName, parent, anchoredPosition, size);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = runtimeFont != null
                ? runtimeFont
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.text = content;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private Button CreateButton(
            string objectName,
            Transform parent,
            string label,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            RectTransform rect = CreateRect(objectName, parent, anchoredPosition, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.12f, 0.22f, 0.32f, 1f);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            Text text = CreateText("Label", rect, label, 16, TextAnchor.MiddleCenter, Vector2.zero, size);
            text.raycastTarget = false;
            return button;
        }
    }
}

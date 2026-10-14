using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Game.Input;
using Game.Systems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.UI.Menus
{
    [DefaultExecutionOrder(-8500)]
    [DisallowMultipleComponent]
    public sealed class SpaceWorldMenuController : MonoBehaviour
    {
        private const string MainMenuScenePath = "Assets/Scenes/System/MAINMENU.unity";
        private const string MainMenuSceneName = "MAINMENU";
        private const string FirstGameplaySceneName = "MUELLE";
        private const string CatalogResourcePath = "UI/SpaceWorldControlHelpCatalog";
        private const string UiSelectionEventId = "ui.navigation.select";
        private const string UiConfirmEventId = "ui.action.confirm";
        private const string UiBackEventId = "ui.navigation.back";
        private const string UiMenuOpenEventId = "ui.menu.open";
        private const string UiMenuCloseEventId = "ui.menu.close";

        private static readonly Color BackgroundColor = new Color(0.012f, 0.035f, 0.075f, 0.97f);
        private static readonly Color PanelColor = new Color(0.035f, 0.09f, 0.16f, 0.94f);
        private static readonly Color ButtonColor = new Color(0.055f, 0.18f, 0.27f, 0.96f);
        private static readonly Color Cyan = new Color(0.20f, 0.90f, 1f, 1f);
        private static readonly Color PaleText = new Color(0.83f, 0.94f, 1f, 1f);
        private static readonly Color MutedText = new Color(0.55f, 0.72f, 0.81f, 1f);

        private enum MenuScreen
        {
            Hidden,
            Main,
            Pause,
            Options,
            Controls
        }

        private enum ControlView
        {
            KeyboardMouse,
            Gamepad
        }

        private sealed class SuspendedInput
        {
            public InputReader reader;
            public bool wasEnabled;
        }

        private readonly struct ControlSpec
        {
            public readonly string path;
            public readonly string label;
            public readonly Vector2 position;
            public readonly Vector2 size;

            public ControlSpec(string path, string label, float x, float y, float width = 72f, float height = 52f)
            {
                this.path = path;
                this.label = label;
                position = new Vector2(x, y);
                size = new Vector2(width, height);
            }
        }

        private static SpaceWorldMenuController instance;

        private PlayerInputActions inputActions;
        private InputAction pauseAction;
        private InputAction backAction;
        private InputAction dpadNavigateAction;
        private ControlHelpCatalog catalog;
        private bool ownsRuntimeCatalog;

        private GameObject menuRoot;
        private GameObject mainPanel;
        private GameObject pausePanel;
        private GameObject optionsPanel;
        private GameObject controlsPanel;
        private RectTransform controlLayoutRoot;
        private Text controlButtonText;
        private Text controlActionText;
        private Text controlDetailsText;
        private Button keyboardTabButton;
        private Button gamepadTabButton;
        private Button mainFirstButton;
        private Button pauseFirstButton;
        private Button optionsFirstButton;
        private Button firstControlButton;

        private GameObject ownedEventSystemObject;
        private EventSystem ownedEventSystem;
        private readonly List<SuspendedInput> suspendedInputs = new List<SuspendedInput>();
        private readonly List<int> suppressedDpadBindingIndices = new List<int>();
        private readonly Queue<MoveDirection> pendingDpadMoves = new Queue<MoveDirection>();
        private InputSystemUIInputModule activeUiInputModule;
        private InputAction suppressedDpadMoveAction;
        private MoveDirection heldDpadDirection = MoveDirection.None;
        private float nextDpadRepeatTime;
        private GameObject lastSelectedMenuObject;

        private MenuScreen currentScreen = MenuScreen.Hidden;
        private MenuScreen optionsReturnScreen = MenuScreen.Main;
        private ControlView controlView = ControlView.KeyboardMouse;
        private bool gameplayPaused;
        private bool cursorVisibleBeforePause;
        private CursorLockMode cursorLockBeforePause;
        private Font font;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null || FindFirstObjectByType<SpaceWorldMenuController>() != null)
                return;

            GameObject root = new GameObject("[SpaceWorldMenus]");
            root.AddComponent<SpaceWorldMenuController>();
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

            inputActions = new PlayerInputActions();
            pauseAction = inputActions.asset.FindAction("Gameplay/Pause", true);
            pauseAction.performed += HandlePausePerformed;
            pauseAction.Enable();

            backAction = new InputAction("MenuBack", InputActionType.Button);
            backAction.AddBinding("<Gamepad>/buttonEast");
            backAction.AddBinding("<Keyboard>/backspace");
            backAction.performed += HandleBackPerformed;
            backAction.Enable();

            dpadNavigateAction = new InputAction(
                "MenuDpadNavigate",
                InputActionType.PassThrough,
                "<Gamepad>/dpad",
                expectedControlType: "Vector2");
            dpadNavigateAction.performed += HandleDpadNavigate;
            dpadNavigateAction.Enable();

            catalog = Resources.Load<ControlHelpCatalog>(CatalogResourcePath);
            if (catalog == null)
            {
                catalog = ControlHelpCatalog.CreateRuntimeDefault();
                ownsRuntimeCatalog = true;
            }

            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildInterface();
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void Start()
        {
            ApplySceneState(SceneManager.GetActiveScene());
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;

            if (pauseAction != null)
                pauseAction.performed -= HandlePausePerformed;
            if (backAction != null)
            {
                backAction.performed -= HandleBackPerformed;
                backAction.Dispose();
            }
            if (dpadNavigateAction != null)
            {
                dpadNavigateAction.performed -= HandleDpadNavigate;
                dpadNavigateAction.Dispose();
            }

            RestoreDefaultDpadNavigation();

            inputActions?.Dispose();
            if (ownsRuntimeCatalog && catalog != null)
                Destroy(catalog);

            if (gameplayPaused)
                ReleaseGameplayPause();
            if (instance == this)
                instance = null;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplySceneState(scene);
        }

        private void ApplySceneState(Scene scene)
        {
            if (IsMainMenuScene(scene))
            {
                gameplayPaused = false;
                HitStopManager.SetExternalPause(false);
                Time.timeScale = 1f;
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                ShowScreen(MenuScreen.Main);
                return;
            }

            currentScreen = MenuScreen.Hidden;
            menuRoot.SetActive(false);
            DisableOwnedEventSystem();
        }

        private static bool IsMainMenuScene(Scene scene)
        {
            return string.Equals(scene.path, MainMenuScenePath, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(scene.name, MainMenuSceneName, StringComparison.OrdinalIgnoreCase);
        }

        private void HandlePausePerformed(InputAction.CallbackContext context)
        {
            if (IsMainMenuScene(SceneManager.GetActiveScene()))
            {
                if (currentScreen == MenuScreen.Controls || currentScreen == MenuScreen.Options)
                    NavigateBack();
                return;
            }

            if (!gameplayPaused)
            {
                OpenPauseMenu();
                return;
            }

            if (currentScreen == MenuScreen.Pause)
                ResumeGameplay();
            else
                NavigateBack();
        }

        private void HandleBackPerformed(InputAction.CallbackContext context)
        {
            if (currentScreen != MenuScreen.Hidden)
                NavigateBack();
        }

        private void HandleDpadNavigate(InputAction.CallbackContext context)
        {
            MoveDirection direction = GetMoveDirection(context.ReadValue<Vector2>());
            if (direction == MoveDirection.None)
            {
                heldDpadDirection = MoveDirection.None;
                nextDpadRepeatTime = 0f;
                return;
            }

            if (direction == heldDpadDirection)
                return;

            heldDpadDirection = direction;
            nextDpadRepeatTime = Time.unscaledTime + GetDpadRepeatDelay();
            if (currentScreen != MenuScreen.Hidden)
                pendingDpadMoves.Enqueue(direction);
        }

        private void LateUpdate()
        {
            if (currentScreen == MenuScreen.Hidden)
            {
                pendingDpadMoves.Clear();
                lastSelectedMenuObject = null;
                return;
            }

            if (heldDpadDirection != MoveDirection.None &&
                Time.unscaledTime >= nextDpadRepeatTime)
            {
                pendingDpadMoves.Enqueue(heldDpadDirection);
                nextDpadRepeatTime = Time.unscaledTime + GetDpadRepeatRate();
            }

            EventSystem eventSystem = EventSystem.current;
            while (pendingDpadMoves.Count > 0 &&
                   eventSystem != null &&
                   eventSystem.currentSelectedGameObject != null)
            {
                SendDpadMove(eventSystem, pendingDpadMoves.Dequeue());
            }

            UpdateSelectionAudio(eventSystem);
        }

        private static MoveDirection GetMoveDirection(Vector2 value)
        {
            if (value.sqrMagnitude < 0.25f)
                return MoveDirection.None;

            if (Mathf.Abs(value.x) > Mathf.Abs(value.y))
                return value.x > 0f ? MoveDirection.Right : MoveDirection.Left;
            return value.y > 0f ? MoveDirection.Up : MoveDirection.Down;
        }

        private static void SendDpadMove(EventSystem eventSystem, MoveDirection direction)
        {
            Vector2 moveVector = direction switch
            {
                MoveDirection.Up => Vector2.up,
                MoveDirection.Down => Vector2.down,
                MoveDirection.Left => Vector2.left,
                MoveDirection.Right => Vector2.right,
                _ => Vector2.zero
            };
            AxisEventData eventData = new AxisEventData(eventSystem)
            {
                moveDir = direction,
                moveVector = moveVector
            };
            ExecuteEvents.Execute(
                eventSystem.currentSelectedGameObject,
                eventData,
                ExecuteEvents.moveHandler);
        }

        private void UpdateSelectionAudio(EventSystem eventSystem)
        {
            GameObject selected = eventSystem != null
                ? eventSystem.currentSelectedGameObject
                : null;
            if (selected == lastSelectedMenuObject)
                return;

            lastSelectedMenuObject = selected;
            if (selected != null && selected.activeInHierarchy)
                Game.Audio.AudioService.Play(UiSelectionEventId);
        }

        private void PlayUiEvent(string eventId)
        {
            EventSystem eventSystem = EventSystem.current;
            lastSelectedMenuObject = eventSystem != null
                ? eventSystem.currentSelectedGameObject
                : null;
            Game.Audio.AudioService.Play(eventId);
        }

        private float GetDpadRepeatDelay()
        {
            return activeUiInputModule != null
                ? activeUiInputModule.moveRepeatDelay
                : 0.5f;
        }

        private float GetDpadRepeatRate()
        {
            float repeatRate = activeUiInputModule != null
                ? activeUiInputModule.moveRepeatRate
                : 0.1f;
            return Mathf.Max(0.01f, repeatRate);
        }

        private void OpenPauseMenu()
        {
            if (gameplayPaused || IsMainMenuScene(SceneManager.GetActiveScene()))
                return;

            cursorVisibleBeforePause = Cursor.visible;
            cursorLockBeforePause = Cursor.lockState;
            SuspendGameplayInputs();
            gameplayPaused = true;
            HitStopManager.SetExternalPause(true);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            ShowScreen(MenuScreen.Pause);
            PlayUiEvent(UiMenuOpenEventId);
        }

        private void ResumeGameplay()
        {
            if (!gameplayPaused)
                return;

            PlayUiEvent(UiMenuCloseEventId);
            currentScreen = MenuScreen.Hidden;
            menuRoot.SetActive(false);
            ReleaseGameplayPause();
            DisableOwnedEventSystem();
        }

        private void ReleaseGameplayPause()
        {
            gameplayPaused = false;
            HitStopManager.SetExternalPause(false);

            for (int i = 0; i < suspendedInputs.Count; i++)
            {
                SuspendedInput state = suspendedInputs[i];
                if (state.reader == null)
                    continue;

                state.reader.ClearGameplayInputState();
                if (state.wasEnabled)
                    state.reader.enabled = true;
            }

            suspendedInputs.Clear();
            Cursor.visible = cursorVisibleBeforePause;
            Cursor.lockState = cursorLockBeforePause;
        }

        private void SuspendGameplayInputs()
        {
            suspendedInputs.Clear();
            InputReader[] readers = FindObjectsOfType<InputReader>(true);
            for (int i = 0; i < readers.Length; i++)
            {
                InputReader reader = readers[i];
                SuspendedInput state = new SuspendedInput
                {
                    reader = reader,
                    wasEnabled = reader.enabled
                };
                suspendedInputs.Add(state);
                reader.ClearGameplayInputState();
                if (reader.enabled)
                    reader.enabled = false;
            }
        }

        private void StartGame()
        {
            PrepareSceneTransition();
            SceneManager.LoadScene(FirstGameplaySceneName);
        }

        private void ReturnToMainMenu()
        {
            if (gameplayPaused)
                ReleaseGameplayPause();
            HitStopManager.CancelAll();
            PrepareSceneTransition();
            SceneManager.LoadScene(MainMenuSceneName);
        }

        private void PrepareSceneTransition()
        {
            currentScreen = MenuScreen.Hidden;
            menuRoot.SetActive(false);
            DisableOwnedEventSystem();
        }

        private void QuitGame()
        {
#if UNITY_EDITOR
            Debug.Log("[MENUS] Salir del juego se ejecutará únicamente en una build.");
#else
            Application.Quit();
#endif
        }

        private void OpenOptions(MenuScreen returnScreen)
        {
            optionsReturnScreen = returnScreen;
            PlayUiEvent(UiMenuOpenEventId);
            ShowScreen(MenuScreen.Options);
        }

        private void OpenControls()
        {
            PlayUiEvent(UiMenuOpenEventId);
            ShowScreen(MenuScreen.Controls);
            RebuildControlView(controlView);
        }

        private void NavigateBack()
        {
            switch (currentScreen)
            {
                case MenuScreen.Controls:
                    PlayUiEvent(UiBackEventId);
                    ShowScreen(MenuScreen.Options);
                    break;
                case MenuScreen.Options:
                    PlayUiEvent(UiBackEventId);
                    ShowScreen(optionsReturnScreen);
                    break;
                case MenuScreen.Pause:
                    ResumeGameplay();
                    break;
            }
        }

        private void ShowScreen(MenuScreen screen)
        {
            ResetDpadNavigationState();
            currentScreen = screen;
            menuRoot.SetActive(true);
            mainPanel.SetActive(screen == MenuScreen.Main);
            pausePanel.SetActive(screen == MenuScreen.Pause);
            optionsPanel.SetActive(screen == MenuScreen.Options);
            controlsPanel.SetActive(screen == MenuScreen.Controls);

            EnsureEventSystem();
            Button target = null;
            switch (screen)
            {
                case MenuScreen.Main:
                    target = mainFirstButton;
                    break;
                case MenuScreen.Pause:
                    target = pauseFirstButton;
                    break;
                case MenuScreen.Options:
                    target = optionsFirstButton;
                    break;
                case MenuScreen.Controls:
                    target = firstControlButton != null ? firstControlButton : keyboardTabButton;
                    break;
            }

            if (target != null)
                StartCoroutine(SelectOnNextFrame(target.gameObject));
        }

        private IEnumerator SelectOnNextFrame(GameObject target)
        {
            yield return null;
            if (target != null && target.activeInHierarchy && EventSystem.current != null)
            {
                lastSelectedMenuObject = target;
                EventSystem.current.SetSelectedGameObject(target);
            }
        }

        private void EnsureEventSystem()
        {
            EventSystem[] systems = FindObjectsOfType<EventSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                EventSystem candidate = systems[i];
                if (candidate == null || candidate == ownedEventSystem || !candidate.isActiveAndEnabled)
                    continue;

                DisableOwnedEventSystem();
                ConfigureDpadNavigation(candidate.GetComponent<InputSystemUIInputModule>());
                return;
            }

            if (ownedEventSystemObject == null)
            {
                ownedEventSystemObject = new GameObject(
                    "MenuEventSystem",
                    typeof(EventSystem),
                    typeof(InputSystemUIInputModule));
                ownedEventSystemObject.transform.SetParent(transform, false);
                ownedEventSystem = ownedEventSystemObject.GetComponent<EventSystem>();
                ownedEventSystemObject.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }

            ownedEventSystemObject.SetActive(true);
            ConfigureDpadNavigation(ownedEventSystemObject.GetComponent<InputSystemUIInputModule>());
        }

        private void DisableOwnedEventSystem()
        {
            RestoreDefaultDpadNavigation();
            ResetDpadNavigationState();
            if (ownedEventSystemObject != null)
                ownedEventSystemObject.SetActive(false);
        }

        private void ConfigureDpadNavigation(InputSystemUIInputModule module)
        {
            RestoreDefaultDpadNavigation();
            activeUiInputModule = module;
            InputAction moveAction = module != null && module.move != null
                ? module.move.action
                : null;
            if (moveAction == null)
                return;

            suppressedDpadMoveAction = moveAction;
            for (int i = 0; i < moveAction.bindings.Count; i++)
            {
                string path = moveAction.bindings[i].path;
                if (string.IsNullOrEmpty(path) ||
                    !path.StartsWith("<Gamepad>/dpad", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                moveAction.ApplyBindingOverride(i, string.Empty);
                suppressedDpadBindingIndices.Add(i);
            }
        }

        private void RestoreDefaultDpadNavigation()
        {
            if (suppressedDpadMoveAction != null)
            {
                for (int i = 0; i < suppressedDpadBindingIndices.Count; i++)
                    suppressedDpadMoveAction.RemoveBindingOverride(suppressedDpadBindingIndices[i]);
            }

            suppressedDpadBindingIndices.Clear();
            suppressedDpadMoveAction = null;
            activeUiInputModule = null;
        }

        private void ResetDpadNavigationState()
        {
            pendingDpadMoves.Clear();
            heldDpadDirection = MoveDirection.None;
            nextDpadRepeatTime = 0f;
        }

        private void BuildInterface()
        {
            menuRoot = new GameObject("MenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            menuRoot.transform.SetParent(transform, false);

            Canvas canvas = menuRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;

            CanvasScaler scaler = menuRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            Image background = menuRoot.AddComponent<Image>();
            background.color = BackgroundColor;

            CreateDecorativeFrame(menuRoot.transform);
            BuildMainPanel();
            BuildPausePanel();
            BuildOptionsPanel();
            BuildControlsPanel();
            menuRoot.SetActive(false);
        }

        private void CreateDecorativeFrame(Transform parent)
        {
            Image topLine = CreatePanel(parent, "TopGlow", new Color(Cyan.r, Cyan.g, Cyan.b, 0.65f),
                new Vector2(0.06f, 0.93f), new Vector2(0.94f, 0.935f));
            topLine.raycastTarget = false;
            Image bottomLine = CreatePanel(parent, "BottomGlow", new Color(Cyan.r, Cyan.g, Cyan.b, 0.28f),
                new Vector2(0.16f, 0.065f), new Vector2(0.84f, 0.068f));
            bottomLine.raycastTarget = false;
        }

        private void BuildMainPanel()
        {
            mainPanel = CreateFullScreenContainer(menuRoot.transform, "MainMenu");
            CreateText(mainPanel.transform, "Title", "SPACE WORLD ALONE", 72, FontStyle.Bold,
                TextAnchor.MiddleCenter, new Vector2(0.12f, 0.70f), new Vector2(0.88f, 0.88f), Cyan);
            CreateText(mainPanel.transform, "Subtitle", "SEÑAL DE ORIGEN // SESIÓN DE EXPLORACIÓN", 19, FontStyle.Normal,
                TextAnchor.MiddleCenter, new Vector2(0.22f, 0.64f), new Vector2(0.78f, 0.70f), MutedText);

            mainFirstButton = CreateMenuButton(mainPanel.transform, "JUGAR", new Vector2(0f, 20f), StartGame);
            CreateMenuButton(mainPanel.transform, "OPCIONES", new Vector2(0f, -70f),
                () => OpenOptions(MenuScreen.Main), null);
            CreateMenuButton(mainPanel.transform, "SALIR DEL JUEGO", new Vector2(0f, -160f), QuitGame);
        }

        private void BuildPausePanel()
        {
            pausePanel = CreateFullScreenContainer(menuRoot.transform, "PauseMenu");
            CreateText(pausePanel.transform, "Title", "MENÚ", 66, FontStyle.Bold,
                TextAnchor.MiddleCenter, new Vector2(0.25f, 0.70f), new Vector2(0.75f, 0.86f), Cyan);
            pauseFirstButton = CreateMenuButton(
                pausePanel.transform, "CONTINUAR", new Vector2(0f, 30f), ResumeGameplay, null);
            CreateMenuButton(pausePanel.transform, "OPCIONES", new Vector2(0f, -60f),
                () => OpenOptions(MenuScreen.Pause), null);
            CreateMenuButton(pausePanel.transform, "SALIR AL MENÚ", new Vector2(0f, -150f), ReturnToMainMenu);
        }

        private void BuildOptionsPanel()
        {
            optionsPanel = CreateFullScreenContainer(menuRoot.transform, "OptionsMenu");
            CreateText(optionsPanel.transform, "Title", "OPCIONES", 64, FontStyle.Bold,
                TextAnchor.MiddleCenter, new Vector2(0.25f, 0.70f), new Vector2(0.75f, 0.86f), Cyan);
            optionsFirstButton = CreateMenuButton(
                optionsPanel.transform, "CONTROLES", new Vector2(0f, 10f), OpenControls, null);
            CreateMenuButton(
                optionsPanel.transform, "VOLVER", new Vector2(0f, -80f), NavigateBack, null);
            CreateText(optionsPanel.transform, "Future", "AUDIO · VÍDEO · REASIGNACIÓN — PRÓXIMAMENTE", 17, FontStyle.Normal,
                TextAnchor.MiddleCenter, new Vector2(0.20f, 0.18f), new Vector2(0.80f, 0.26f), MutedText);
        }

        private void BuildControlsPanel()
        {
            controlsPanel = CreateFullScreenContainer(menuRoot.transform, "ControlsMenu");
            CreateText(controlsPanel.transform, "Title", "CONTROLES", 50, FontStyle.Bold,
                TextAnchor.MiddleLeft, new Vector2(0.05f, 0.86f), new Vector2(0.35f, 0.94f), Cyan);

            keyboardTabButton = CreateSmallButton(controlsPanel.transform, "TECLADO Y RATÓN",
                new Vector2(0.38f, 0.865f), new Vector2(0.55f, 0.925f),
                () => RebuildControlView(ControlView.KeyboardMouse));
            gamepadTabButton = CreateSmallButton(controlsPanel.transform, "MANDO",
                new Vector2(0.56f, 0.865f), new Vector2(0.69f, 0.925f),
                () => RebuildControlView(ControlView.Gamepad));

            Image layoutPanel = CreatePanel(controlsPanel.transform, "DeviceLayout", PanelColor,
                new Vector2(0.04f, 0.15f), new Vector2(0.65f, 0.83f));
            controlLayoutRoot = layoutPanel.rectTransform;

            Image infoPanel = CreatePanel(controlsPanel.transform, "Information", PanelColor,
                new Vector2(0.68f, 0.15f), new Vector2(0.96f, 0.83f));
            controlButtonText = CreateText(infoPanel.transform, "Button", "BOTÓN: —", 22, FontStyle.Bold,
                TextAnchor.UpperLeft, new Vector2(0.08f, 0.82f), new Vector2(0.92f, 0.94f), Cyan);
            controlActionText = CreateText(infoPanel.transform, "Action", "ACCIÓN: —", 22, FontStyle.Bold,
                TextAnchor.UpperLeft, new Vector2(0.08f, 0.70f), new Vector2(0.92f, 0.84f), PaleText);
            controlDetailsText = CreateText(infoPanel.transform, "Details",
                "Selecciona una tecla o botón para consultar su función.", 18, FontStyle.Normal,
                TextAnchor.UpperLeft, new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.70f), PaleText);
            controlDetailsText.verticalOverflow = VerticalWrapMode.Overflow;

            CreateSmallButton(controlsPanel.transform, "VOLVER",
                new Vector2(0.80f, 0.055f), new Vector2(0.96f, 0.12f), NavigateBack, null);
        }

        private void RebuildControlView(ControlView nextView)
        {
            controlView = nextView;
            for (int i = controlLayoutRoot.childCount - 1; i >= 0; i--)
                Destroy(controlLayoutRoot.GetChild(i).gameObject);

            Dictionary<string, List<string>> bindings = CollectEffectiveBindings(nextView);
            ControlSpec[] specs = nextView == ControlView.KeyboardMouse
                ? KeyboardSpecs()
                : GamepadSpecs();

            firstControlButton = null;
            HashSet<string> represented = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < specs.Length; i++)
            {
                ControlSpec spec = specs[i];
                string normalizedPath = NormalizeControlPath(spec.path);
                if (!bindings.TryGetValue(normalizedPath, out List<string> actionNames))
                    continue;

                represented.Add(normalizedPath);
                CreateControlButton(spec, actionNames);
            }

            int fallbackIndex = 0;
            foreach (KeyValuePair<string, List<string>> pair in bindings)
            {
                if (represented.Contains(pair.Key))
                    continue;

                float x = -420f + (fallbackIndex % 5) * 190f;
                float y = -250f - (fallbackIndex / 5) * 65f;
                ControlSpec fallback = new ControlSpec(
                    pair.Key,
                    InputControlPath.ToHumanReadableString(pair.Key, InputControlPath.HumanReadableStringOptions.OmitDevice),
                    x,
                    y,
                    170f,
                    48f);
                CreateControlButton(fallback, pair.Value);
                fallbackIndex++;
            }

            keyboardTabButton.interactable = nextView != ControlView.KeyboardMouse;
            gamepadTabButton.interactable = nextView != ControlView.Gamepad;
            if (firstControlButton != null)
                StartCoroutine(SelectOnNextFrame(firstControlButton.gameObject));
        }

        private Dictionary<string, List<string>> CollectEffectiveBindings(ControlView view)
        {
            Dictionary<string, List<string>> result =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            InputActionMap map = inputActions.asset.FindActionMap("Gameplay", true);

            foreach (InputAction action in map.actions)
            {
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    InputBinding binding = action.bindings[i];
                    if (binding.isComposite)
                        continue;

                    string path = string.IsNullOrWhiteSpace(binding.effectivePath)
                        ? binding.path
                        : binding.effectivePath;
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    string normalized = NormalizeControlPath(path);
                    bool matchesView = view == ControlView.KeyboardMouse
                        ? normalized.StartsWith("<keyboard>/", StringComparison.OrdinalIgnoreCase) ||
                          normalized.StartsWith("<mouse>/", StringComparison.OrdinalIgnoreCase)
                        : normalized.StartsWith("<gamepad>/", StringComparison.OrdinalIgnoreCase);
                    if (!matchesView)
                        continue;

                    if (!result.TryGetValue(normalized, out List<string> actionsForControl))
                    {
                        actionsForControl = new List<string>();
                        result.Add(normalized, actionsForControl);
                    }

                    if (!actionsForControl.Contains(action.name))
                        actionsForControl.Add(action.name);
                }
            }

            return result;
        }

        private static string NormalizeControlPath(string path)
        {
            string normalized = path.Trim().ToLowerInvariant();
            if (normalized.StartsWith("<gamepad>/leftstick/", StringComparison.Ordinal))
                return "<gamepad>/leftstick";
            if (normalized.StartsWith("<gamepad>/rightstick/", StringComparison.Ordinal))
                return "<gamepad>/rightstick";
            return normalized;
        }

        private void CreateControlButton(ControlSpec spec, IReadOnlyList<string> actionNames)
        {
            GameObject buttonObject = new GameObject(spec.label, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(controlLayoutRoot, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = spec.position;
            rect.sizeDelta = spec.size;

            Color categoryColor = GetCategoryColor(actionNames.Count > 0 ? actionNames[0] : string.Empty);
            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(categoryColor.r * 0.35f, categoryColor.g * 0.35f, categoryColor.b * 0.35f, 0.98f);

            Button button = buttonObject.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = image.color;
            colors.highlightedColor = new Color(categoryColor.r * 0.7f, categoryColor.g * 0.7f, categoryColor.b * 0.7f, 1f);
            colors.selectedColor = categoryColor;
            colors.pressedColor = Color.white;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };

            Text label = CreateText(buttonObject.transform, "Label", spec.label, 17, FontStyle.Bold,
                TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Color.white);
            label.raycastTarget = false;

            ControlBindingButton bindingView = buttonObject.AddComponent<ControlBindingButton>();
            bindingView.Configure(this, spec.label, actionNames);

            if (firstControlButton == null)
            {
                firstControlButton = button;
                ShowControlDetails(spec.label, actionNames);
            }
        }

        public void ShowControlDetails(string controlName, IReadOnlyList<string> actionNames)
        {
            controlButtonText.text = $"BOTÓN: {controlName}";
            if (actionNames == null || actionNames.Count == 0)
            {
                controlActionText.text = "ACCIÓN: SIN ASIGNACIÓN";
                controlDetailsText.text = "Este control no tiene una acción jugable asignada.";
                return;
            }

            List<string> displayNames = new List<string>();
            StringBuilder details = new StringBuilder();
            for (int i = 0; i < actionNames.Count; i++)
            {
                ControlHelpCatalog.Entry entry = catalog.Find(actionNames[i]);
                if (entry == null)
                {
                    displayNames.Add(actionNames[i].ToUpperInvariant());
                    details.Append("DESCRIPCIÓN:\nAcción disponible en el mapa de entrada actual.");
                }
                else
                {
                    displayNames.Add(entry.displayName);
                    if (details.Length > 0)
                        details.Append("\n\n────────────\n\n");
                    details.Append("DESCRIPCIÓN:\n").Append(entry.description)
                        .Append("\n\nTIPO:\n").Append(entry.inputType)
                        .Append("\n\nCONDICIÓN:\n").Append(entry.condition)
                        .Append("\n\nCONTEXTO:\n").Append(entry.context);
                    if (!string.IsNullOrWhiteSpace(entry.notes))
                        details.Append("\n\nNOTAS:\n").Append(entry.notes);
                }
            }

            controlActionText.text = "ACCIÓN: " + string.Join(" / ", displayNames);
            controlDetailsText.text = details.ToString();
        }

        private Color GetCategoryColor(string actionName)
        {
            ControlHelpCatalog.Entry entry = catalog.Find(actionName);
            if (entry == null)
                return Cyan;

            switch (entry.category)
            {
                case ControlHelpCatalog.ControlCategory.Movement:
                    return new Color(0.16f, 0.78f, 1f, 1f);
                case ControlHelpCatalog.ControlCategory.Combat:
                    return new Color(1f, 0.32f, 0.38f, 1f);
                case ControlHelpCatalog.ControlCategory.Traversal:
                    return new Color(0.35f, 0.95f, 0.60f, 1f);
                case ControlHelpCatalog.ControlCategory.Interaction:
                    return new Color(1f, 0.72f, 0.22f, 1f);
                default:
                    return new Color(0.70f, 0.48f, 1f, 1f);
            }
        }

        private static ControlSpec[] KeyboardSpecs()
        {
            return new[]
            {
                new ControlSpec("<Keyboard>/escape", "ESC", -470f, 250f, 86f),
                new ControlSpec("<Keyboard>/capsLock", "CAPS", -455f, 80f, 92f),
                new ControlSpec("<Keyboard>/w", "W", -315f, 135f),
                new ControlSpec("<Keyboard>/a", "A", -395f, 75f),
                new ControlSpec("<Keyboard>/s", "S", -315f, 75f),
                new ControlSpec("<Keyboard>/d", "D", -235f, 75f),
                new ControlSpec("<Keyboard>/leftShift", "SHIFT", -440f, 5f, 120f),
                new ControlSpec("<Keyboard>/ctrl", "CTRL", -440f, -60f, 120f),
                new ControlSpec("<Keyboard>/leftAlt", "ALT", -315f, -60f, 105f),
                new ControlSpec("<Keyboard>/space", "ESPACIO", -150f, -60f, 210f),
                new ControlSpec("<Keyboard>/e", "E", -205f, 135f),
                new ControlSpec("<Keyboard>/f", "F", -125f, 135f),
                new ControlSpec("<Keyboard>/g", "G", -45f, 135f),
                new ControlSpec("<Keyboard>/h", "H", 35f, 135f),
                new ControlSpec("<Keyboard>/j", "J", 115f, 135f),
                new ControlSpec("<Keyboard>/k", "K", 195f, 135f),
                new ControlSpec("<Keyboard>/m", "M", 275f, 135f),
                new ControlSpec("<Keyboard>/r", "R", -125f, 75f),
                new ControlSpec("<Keyboard>/upArrow", "↑", 325f, 10f),
                new ControlSpec("<Keyboard>/leftArrow", "←", 245f, -50f),
                new ControlSpec("<Keyboard>/downArrow", "↓", 325f, -50f),
                new ControlSpec("<Keyboard>/rightArrow", "→", 405f, -50f),
                new ControlSpec("<Mouse>/leftButton", "RATÓN IZQ.", 430f, 140f, 145f, 100f)
            };
        }

        private static ControlSpec[] GamepadSpecs()
        {
            return new[]
            {
                new ControlSpec("<Gamepad>/leftTrigger", "L2 / LT", -420f, 235f, 115f),
                new ControlSpec("<Gamepad>/leftShoulder", "L1 / LB", -310f, 235f, 115f),
                new ControlSpec("<Gamepad>/rightShoulder", "R1 / RB", 310f, 235f, 115f),
                new ControlSpec("<Gamepad>/rightTrigger", "R2 / RT", 420f, 235f, 115f),
                new ControlSpec("<Gamepad>/select", "VIEW", -100f, 160f, 105f),
                new ControlSpec("<Gamepad>/start", "OPTIONS", 100f, 160f, 120f),
                new ControlSpec("<Gamepad>/dpad/up", "D-PAD ↑", -250f, 80f, 120f),
                new ControlSpec("<Gamepad>/leftStick", "STICK IZQ.", -235f, -70f, 150f, 82f),
                new ControlSpec("<Gamepad>/leftStickPress", "L3", -235f, -150f, 76f),
                new ControlSpec("<Gamepad>/rightStick", "STICK DER.", 165f, -105f, 150f, 82f),
                new ControlSpec("<Gamepad>/rightStickPress", "R3", 165f, -185f, 76f),
                new ControlSpec("<Gamepad>/buttonNorth", "Y / △", 350f, 95f),
                new ControlSpec("<Gamepad>/buttonWest", "X / □", 275f, 20f),
                new ControlSpec("<Gamepad>/buttonEast", "B / ○", 425f, 20f),
                new ControlSpec("<Gamepad>/buttonSouth", "A / ×", 350f, -55f)
            };
        }

        private Button CreateMenuButton(
            Transform parent,
            string label,
            Vector2 position,
            UnityEngine.Events.UnityAction action,
            string clickAudioEventId = UiConfirmEventId)
        {
            GameObject buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(540f, 66f);
            rect.anchoredPosition = position;
            ConfigureButton(
                buttonObject.GetComponent<Button>(),
                buttonObject.GetComponent<Image>(),
                action,
                clickAudioEventId);
            CreateText(buttonObject.transform, "Label", label, 24, FontStyle.Bold,
                TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, PaleText).raycastTarget = false;
            return buttonObject.GetComponent<Button>();
        }

        private Button CreateSmallButton(
            Transform parent,
            string label,
            Vector2 anchorMin,
            Vector2 anchorMax,
            UnityEngine.Events.UnityAction action,
            string clickAudioEventId = UiConfirmEventId)
        {
            GameObject buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            ConfigureButton(
                buttonObject.GetComponent<Button>(),
                buttonObject.GetComponent<Image>(),
                action,
                clickAudioEventId);
            CreateText(buttonObject.transform, "Label", label, 18, FontStyle.Bold,
                TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, PaleText).raycastTarget = false;
            return buttonObject.GetComponent<Button>();
        }

        private void ConfigureButton(
            Button button,
            Image image,
            UnityEngine.Events.UnityAction action,
            string clickAudioEventId)
        {
            image.color = ButtonColor;
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = ButtonColor;
            colors.highlightedColor = new Color(0.10f, 0.42f, 0.55f, 1f);
            colors.selectedColor = new Color(0.12f, 0.60f, 0.72f, 1f);
            colors.pressedColor = Cyan;
            colors.disabledColor = new Color(0.04f, 0.10f, 0.14f, 0.75f);
            colors.fadeDuration = 0.10f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            button.onClick.AddListener(() =>
            {
                if (!string.IsNullOrEmpty(clickAudioEventId))
                    PlayUiEvent(clickAudioEventId);
                action?.Invoke();
            });
        }

        private GameObject CreateFullScreenContainer(Transform parent, string name)
        {
            GameObject container = new GameObject(name, typeof(RectTransform));
            container.transform.SetParent(parent, false);
            RectTransform rect = container.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return container;
        }

        private static Image CreatePanel(
            Transform parent,
            string name,
            Color color,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = panel.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private Text CreateText(
            Transform parent,
            string name,
            string value,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Color color)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Text text = textObject.GetComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
    }
}

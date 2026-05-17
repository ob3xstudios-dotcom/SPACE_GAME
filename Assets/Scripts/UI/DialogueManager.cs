using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public class DialogueManager : MonoBehaviour
    {
        [System.Serializable]
        public class DialogueEntry
        {
            public string id;
            public string speaker;
            [TextArea(2, 5)] public string[] lines;
        }

        private static DialogueManager instance;

        [Header("Dialogue Data")]
        [SerializeField] private List<DialogueEntry> dialogues = new List<DialogueEntry>
        {
            new DialogueEntry
            {
                id = "plaza_jose_01",
                speaker = "Jose",
                lines = new[]
                {
                    "Has llegado a la plaza.",
                    "Todavia queda mucho por descubrir al otro lado."
                }
            }
        };

        [Header("UI")]
        [SerializeField] private GameObject dialogueRoot;
        [SerializeField] private Text speakerText;
        [SerializeField] private Text bodyText;
        [SerializeField] private Font dialogueFont;

        [Header("Debug")]
        [SerializeField] private bool debugLogs;

        private readonly Dictionary<string, DialogueEntry> dialogueById = new Dictionary<string, DialogueEntry>();
        private DialogueEntry activeDialogue;
        private int activeLineIndex;

        public static DialogueManager Instance
        {
            get
            {
                if (instance != null) return instance;

                instance = FindFirstObjectByType<DialogueManager>();
                if (instance != null) return instance;

                GameObject go = new GameObject("DialogueManager");
                instance = go.AddComponent<DialogueManager>();
                return instance;
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            RebuildLookup();
            EnsureUI();
            HideDialogue();
        }

        public static void RequestDialogue(GameObject interactor, string dialogueId)
        {
            Instance.ShowDialogue(interactor, dialogueId);
        }

        public void ShowDialogue(GameObject interactor, string dialogueId)
        {
            if (string.IsNullOrWhiteSpace(dialogueId))
            {
                Debug.LogWarning("[DIALOGUE] Empty dialogue id.");
                return;
            }

            EnsureReady();

            if (!dialogueById.TryGetValue(dialogueId, out DialogueEntry dialogue))
            {
                dialogue = CreateFallbackDialogue(dialogueId);
                dialogueById[dialogueId] = dialogue;
                Debug.LogWarning($"[DIALOGUE] Dialogue id not found: {dialogueId}. Showing fallback text.");
            }

            if (dialogueRoot.activeSelf && activeDialogue == dialogue)
            {
                AdvanceDialogue();
                return;
            }

            activeDialogue = dialogue;
            activeLineIndex = 0;
            dialogueRoot.SetActive(true);
            RefreshLine();

            if (debugLogs)
                Debug.Log($"[DIALOGUE] Start {dialogueId} by {interactor?.name}");
        }

        public static void RequestDialogueLines(GameObject interactor, string dialogueId, string speaker, string[] lines)
        {
            Instance.ShowDialogueLines(interactor, dialogueId, speaker, lines);
        }

        public void ShowDialogueLines(GameObject interactor, string dialogueId, string speaker, string[] lines)
        {
            EnsureReady();

            activeDialogue = new DialogueEntry
            {
                id = dialogueId,
                speaker = speaker,
                lines = lines != null && lines.Length > 0 ? lines : new[] { dialogueId }
            };

            activeLineIndex = 0;
            dialogueRoot.SetActive(true);
            RefreshLine();

            if (debugLogs)
                Debug.Log($"[DIALOGUE] Start custom {dialogueId} by {interactor?.name}");
        }

        public void AdvanceDialogue()
        {
            if (activeDialogue == null) return;

            activeLineIndex++;
            if (activeDialogue.lines == null || activeLineIndex >= activeDialogue.lines.Length)
            {
                HideDialogue();
                return;
            }

            RefreshLine();
        }

        public void HideDialogue()
        {
            activeDialogue = null;
            activeLineIndex = 0;

            if (dialogueRoot != null)
                dialogueRoot.SetActive(false);
        }

        public static void HideCurrentDialogue()
        {
            if (instance != null)
                instance.HideDialogue();
        }

        private void EnsureReady()
        {
            if (dialogueById.Count == 0)
                RebuildLookup();

            EnsureUI();
        }

        private void RebuildLookup()
        {
            dialogueById.Clear();

            foreach (DialogueEntry dialogue in dialogues)
            {
                if (dialogue == null || string.IsNullOrWhiteSpace(dialogue.id)) continue;
                dialogueById[dialogue.id] = dialogue;
            }
        }

        private void RefreshLine()
        {
            if (activeDialogue == null) return;

            if (speakerText != null)
                speakerText.text = activeDialogue.speaker;

            if (bodyText != null)
            {
                string[] lines = activeDialogue.lines;
                bodyText.text = lines != null && activeLineIndex < lines.Length ? lines[activeLineIndex] : string.Empty;
            }
        }

        private void EnsureUI()
        {
            if (dialogueRoot != null && speakerText != null && bodyText != null) return;

            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasGo = new GameObject("Canvas");
                canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasGo.AddComponent<CanvasScaler>();
                canvasGo.AddComponent<GraphicRaycaster>();
            }

            if (dialogueRoot == null)
                CreateDefaultDialogueUI(canvas.transform);
            else
                EnsureTextChildren();
        }

        private void CreateDefaultDialogueUI(Transform parent)
        {
            GameObject root = new GameObject("DialogueRoot");
            root.transform.SetParent(parent, false);
            dialogueRoot = root;

            RectTransform rootRect = root.AddComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.08f, 0.04f);
            rootRect.anchorMax = new Vector2(0.92f, 0.26f);
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            Image background = root.AddComponent<Image>();
            background.color = new Color(0.02f, 0.02f, 0.025f, 0.88f);

            EnsureTextChildren();
        }

        private void EnsureTextChildren()
        {
            if (dialogueRoot == null) return;

            if (speakerText == null)
                speakerText = CreateText("SpeakerText", new Vector2(0.04f, 0.66f), new Vector2(0.96f, 0.92f), 24, FontStyle.Bold, new Color(1f, 0.92f, 0.72f, 1f));

            if (bodyText == null)
                bodyText = CreateText("BodyText", new Vector2(0.04f, 0.12f), new Vector2(0.96f, 0.64f), 22, FontStyle.Normal, Color.white);
        }

        private Text CreateText(string objectName, Vector2 anchorMin, Vector2 anchorMax, int fontSize, FontStyle fontStyle, Color color)
        {
            Transform existing = dialogueRoot.transform.Find(objectName);
            GameObject textGo = existing != null ? existing.gameObject : new GameObject(objectName);
            textGo.transform.SetParent(dialogueRoot.transform, false);

            Text text = textGo.GetComponent<Text>();
            if (text == null)
                text = textGo.AddComponent<Text>();

            if (dialogueFont == null)
                dialogueFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            text.font = dialogueFont;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;

            RectTransform rect = textGo.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return text;
        }

        private static DialogueEntry CreateFallbackDialogue(string dialogueId)
        {
            return new DialogueEntry
            {
                id = dialogueId,
                speaker = "Dialogo",
                lines = new[] { dialogueId }
            };
        }
    }
}

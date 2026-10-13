using System.Collections.Generic;
using Game.UI;
using UnityEngine;

namespace Game.Tutorials
{
    [DefaultExecutionOrder(-9000)]
    [DisallowMultipleComponent]
    public sealed class RadioTutorialManager : MonoBehaviour
    {
        private const string ServiceObjectName = "[RadioTutorialManager]";
        private const string DialogueOwnerPrefix = "RadioTutorial:";
        private const int TutorialDialoguePriority = 10;

        private enum TutorialPhase
        {
            None,
            AwaitingInstructionPresentation,
            AwaitingActions,
            AwaitingSuccessPresentation,
            ShowingSuccess
        }

        private static RadioTutorialManager instance;

        [SerializeField, Min(0.5f)] private float successDisplaySeconds = 2.5f;
        [SerializeField] private bool debugLogs;

        private readonly Queue<RadioTutorialDefinition> queue =
            new Queue<RadioTutorialDefinition>();
        private readonly HashSet<string> queuedIds = new HashSet<string>();
        private readonly HashSet<string> sessionCompleted = new HashSet<string>();
        private readonly HashSet<RadioTutorialAction> achievedActions =
            new HashSet<RadioTutorialAction>();

        private RadioTutorialDefinition current;
        private TutorialPhase phase;
        private float phaseEndsAt;

        public static string ActiveTutorialId =>
            instance != null && instance.current != null
                ? instance.current.TutorialId
                : null;

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
            if (instance != null) return;

            instance = FindFirstObjectByType<RadioTutorialManager>();
            if (instance != null) return;

            GameObject serviceObject = new GameObject(ServiceObjectName);
            instance = serviceObject.AddComponent<RadioTutorialManager>();
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

            if (GetComponent<RadioTutorialPlayerSignalBridge>() == null)
                gameObject.AddComponent<RadioTutorialPlayerSignalBridge>();
        }

        private void OnDestroy()
        {
            if (instance != this) return;

            HideCurrentPresentation();
            instance = null;
        }

        private void Update()
        {
            if (current == null) return;

            switch (phase)
            {
                case TutorialPhase.AwaitingInstructionPresentation:
                    if (TryPresentInstruction())
                        phase = TutorialPhase.AwaitingActions;
                    break;

                case TutorialPhase.AwaitingActions:
                    if (!DialogueManager.IsDialogueOwnedBy(CurrentDialogueOwnerId))
                        phase = TutorialPhase.AwaitingInstructionPresentation;
                    break;

                case TutorialPhase.AwaitingSuccessPresentation:
                    if (TryPresentSuccess())
                    {
                        phase = TutorialPhase.ShowingSuccess;
                        phaseEndsAt = Time.unscaledTime + successDisplaySeconds;
                    }
                    break;

                case TutorialPhase.ShowingSuccess:
                    if (!DialogueManager.IsDialogueOwnedBy(CurrentDialogueOwnerId))
                    {
                        phase = TutorialPhase.AwaitingSuccessPresentation;
                    }
                    else if (Time.unscaledTime >= phaseEndsAt)
                    {
                        HideCurrentPresentation();
                        Advance();
                    }
                    break;
            }
        }

        public static bool TryStart(RadioTutorialDefinition definition)
        {
            EnsureInstance();
            return instance.Enqueue(definition);
        }

        public static bool Report(RadioTutorialAction action)
        {
            return instance != null && instance.AcceptAction(action);
        }

        public static bool TryPause(RadioTutorialDefinition definition)
        {
            return instance != null && instance.Pause(definition);
        }

        public static bool IsCompletedThisSession(string tutorialId)
        {
            return instance != null &&
                   !string.IsNullOrWhiteSpace(tutorialId) &&
                   instance.sessionCompleted.Contains(tutorialId);
        }

        private bool Enqueue(RadioTutorialDefinition definition)
        {
            if (definition == null || !definition.HasValidConfiguration)
            {
                Debug.LogWarning(
                    "[RADIO TUTORIAL] Cannot start a tutorial with an invalid definition.",
                    definition);
                return false;
            }

            string tutorialId = definition.TutorialId;
            if (sessionCompleted.Contains(tutorialId)) return false;
            if (current != null && current.TutorialId == tutorialId) return false;
            if (!queuedIds.Add(tutorialId)) return false;

            queue.Enqueue(definition);
            if (current == null)
                Advance();
            return true;
        }

        private bool Pause(RadioTutorialDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.TutorialId))
                return false;

            string tutorialId = definition.TutorialId;
            bool removedFromQueue = RemoveQueuedTutorial(tutorialId);
            if (current == null || current.TutorialId != tutorialId)
                return removedFromQueue;

            HideCurrentPresentation();
            current = null;
            phase = TutorialPhase.None;
            achievedActions.Clear();

            if (debugLogs)
                Debug.Log($"[RADIO TUTORIAL] Paused '{tutorialId}' after leaving its marker.", definition);

            Advance();
            return true;
        }

        private bool RemoveQueuedTutorial(string tutorialId)
        {
            if (!queuedIds.Remove(tutorialId)) return false;

            int queuedCount = queue.Count;
            for (int index = 0; index < queuedCount; index++)
            {
                RadioTutorialDefinition queued = queue.Dequeue();
                if (queued == null || queued.TutorialId != tutorialId)
                    queue.Enqueue(queued);
            }

            return true;
        }

        private void Advance()
        {
            current = null;
            phase = TutorialPhase.None;
            achievedActions.Clear();

            if (queue.Count == 0) return;

            current = queue.Dequeue();
            queuedIds.Remove(current.TutorialId);
            phase = TutorialPhase.AwaitingInstructionPresentation;

            if (debugLogs)
                Debug.Log($"[RADIO TUTORIAL] Started '{current.TutorialId}'.", current);
        }

        private bool AcceptAction(RadioTutorialAction action)
        {
            if (current == null || phase != TutorialPhase.AwaitingActions)
                return false;
            if (action == RadioTutorialAction.None)
                return false;
            if (!DialogueManager.IsDialogueOwnedBy(CurrentDialogueOwnerId))
                return false;

            RadioTutorialAction[] required = current.RequiredActions;
            bool recognized = false;
            for (int index = 0; index < required.Length; index++)
            {
                if (required[index] != action) continue;

                achievedActions.Add(action);
                recognized = true;
            }

            if (!recognized) return false;

            for (int index = 0; index < required.Length; index++)
            {
                if (!achievedActions.Contains(required[index]))
                    return true;
            }

            sessionCompleted.Add(current.TutorialId);
            phase = TutorialPhase.AwaitingSuccessPresentation;
            return true;
        }

        private bool TryPresentInstruction()
        {
            return DialogueManager.TryRequestOwnedDialogueLines(
                gameObject,
                CurrentDialogueOwnerId,
                TutorialDialoguePriority,
                current.TutorialId,
                current.Speaker,
                new[] { JoinLines(current.OpeningLine, current.Instruction) });
        }

        private bool TryPresentSuccess()
        {
            return DialogueManager.TryRequestOwnedDialogueLines(
                gameObject,
                CurrentDialogueOwnerId,
                TutorialDialoguePriority,
                current.TutorialId,
                current.Speaker,
                new[] { current.SuccessLine });
        }

        private void HideCurrentPresentation()
        {
            if (current == null) return;
            DialogueManager.HideDialogueOwnedBy(CurrentDialogueOwnerId);
        }

        private string CurrentDialogueOwnerId =>
            current != null ? DialogueOwnerPrefix + current.TutorialId : null;

        private static string JoinLines(params string[] parts)
        {
            string result = string.Empty;
            for (int index = 0; index < parts.Length; index++)
            {
                string line = parts[index];
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (result.Length > 0) result += "\n";
                result += line;
            }

            return result;
        }
    }
}

using UnityEngine;
using UnityEngine.Events;
using Game.UI;

namespace Game.Interaction
{
    public class DialogueInteractable : InteractableBase
    {
        [System.Serializable]
        public class DialogueRequestedEvent : UnityEvent<GameObject, string> { }

        [SerializeField, TextArea] private string dialogueId;
        [SerializeField] private bool overrideDialogueInInspector;
        [SerializeField] private string speaker = "Jose";
        [SerializeField, TextArea(3, 8)] private string dialogueText;
        [Header("Advanced")]
        [SerializeField] private DialogueRequestedEvent onDialogueRequested;

        public string DialogueId => dialogueId;

        public override void Interact(GameObject interactor)
        {
            if (onDialogueRequested != null && onDialogueRequested.GetPersistentEventCount() > 0)
            {
                onDialogueRequested.Invoke(interactor, dialogueId);
            }
            else if (overrideDialogueInInspector)
            {
                DialogueManager.RequestDialogueLines(interactor, dialogueId, speaker, GetDialogueLines());
            }
            else
            {
                DialogueManager.RequestDialogue(interactor, dialogueId);
            }

            if (!string.IsNullOrWhiteSpace(dialogueId))
                Debug.Log($"[INTERACT] Dialogue requested: {dialogueId}");
        }

        private string[] GetDialogueLines()
        {
            if (string.IsNullOrWhiteSpace(dialogueText))
                return new[] { dialogueId };

            return dialogueText.Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.RemoveEmptyEntries);
        }
    }
}

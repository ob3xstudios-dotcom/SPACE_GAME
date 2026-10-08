using Game.Progress;
using Game.UI;
using UnityEngine;
using UnityEngine.Events;

namespace Game.Map
{
    [DisallowMultipleComponent]
    public sealed class MapVendorInteractable : MonoBehaviour
    {
        [System.Serializable]
        public sealed class PurchaseRequestedEvent : UnityEvent<GameObject> { }

        [Header("Purchase integration")]
        [Tooltip("Development only. When false, interaction requests a future economy transaction and never grants the map for free.")]
        [SerializeField] private bool grantWithoutPaymentForTesting;
        [SerializeField] private PurchaseRequestedEvent onPurchaseRequested;

        [Header("Dialogue")]
        [SerializeField] private string speaker = "Jose";
        [SerializeField, TextArea(2, 5)] private string[] purchaseOfferLines =
        {
            "Tengo un mapa a la venta."
        };
        [SerializeField, TextArea(2, 5)] private string[] purchasedLines =
        {
            "Aqui tienes el mapa."
        };
        [SerializeField, TextArea(2, 5)] private string[] alreadyOwnedLines =
        {
            "Ya tienes mi mapa."
        };

        public bool CanPurchase()
        {
            return !GameProgressService.Instance.MapOwned;
        }

        public bool CompletePurchase()
        {
            if (!CanPurchase())
                return false;

            return GameProgressService.Instance.GrantMap(true);
        }

        public void HandleDialogueRequested(GameObject interactor, string dialogueId)
        {
            if (!CanPurchase())
            {
                RequestDialogue(interactor, dialogueId, alreadyOwnedLines);
                return;
            }

            if (grantWithoutPaymentForTesting)
            {
                CompletePurchase();
                RequestDialogue(interactor, dialogueId, purchasedLines);
                return;
            }

            onPurchaseRequested?.Invoke(interactor);
            RequestDialogue(interactor, dialogueId, purchaseOfferLines);
        }

        private void RequestDialogue(GameObject interactor, string dialogueId, string[] lines)
        {
            if (lines == null || lines.Length == 0)
                return;

            DialogueManager.RequestDialogueLines(interactor, dialogueId, speaker, lines);
        }
    }
}

using UnityEngine;

namespace Game.Interaction
{
    [RequireComponent(typeof(Collider2D))]
    public class HideTrigger : MonoBehaviour, IInteractable
    {
        [SerializeField] private HideSpot hideSpot;
        [SerializeField] private bool interactToEnter = true;
        [SerializeField] private bool registerCandidate = true;

        public Transform InteractableTransform => transform;

        private void Awake()
        {
            ResolveRefs();
            EnsureTrigger();
        }

        private void Reset()
        {
            ResolveRefs();
            EnsureTrigger();
        }

        private void OnValidate()
        {
            ResolveRefs();
            EnsureTrigger();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (registerCandidate)
                hideSpot?.RegisterCandidate(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (registerCandidate)
                hideSpot?.ClearCandidate(other);
        }

        public bool CanInteract(GameObject interactor)
        {
            return interactToEnter && isActiveAndEnabled && hideSpot != null && hideSpot.CanEnter(interactor);
        }

        public void Interact(GameObject interactor)
        {
            if (!interactToEnter || hideSpot == null) return;
            hideSpot.TryEnter(interactor);
        }

        private void ResolveRefs()
        {
            if (hideSpot == null)
                hideSpot = GetComponentInParent<HideSpot>();
        }

        private void EnsureTrigger()
        {
            Collider2D col = GetComponent<Collider2D>();
            if (col != null)
                col.isTrigger = true;
        }
    }
}

using Game.Interaction;
using UnityEngine;

namespace Game.Barrels
{
    [RequireComponent(typeof(Collider2D))]
    public class BarrelHideTriggerTop : MonoBehaviour, IInteractable
    {
        [SerializeField] private BarrelHideable hideable;

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
            hideable?.HandleTopTriggerEnter(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            hideable?.HandleTopTriggerExit(other);
        }

        public bool CanInteract(GameObject interactor)
        {
            return isActiveAndEnabled && hideable != null && hideable.CanEnter(interactor);
        }

        public void Interact(GameObject interactor)
        {
            if (hideable == null) return;
            hideable.TryEnter(interactor);
        }

        private void ResolveRefs()
        {
            if (hideable == null)
                hideable = GetComponentInParent<BarrelHideable>();
        }

        private void EnsureTrigger()
        {
            Collider2D col = GetComponent<Collider2D>();
            if (col != null)
                col.isTrigger = true;
        }
    }
}

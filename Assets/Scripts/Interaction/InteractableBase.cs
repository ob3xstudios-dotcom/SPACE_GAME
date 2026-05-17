using UnityEngine;

namespace Game.Interaction
{
    [RequireComponent(typeof(Collider2D))]
    public abstract class InteractableBase : MonoBehaviour, IInteractable
    {
        [SerializeField] private bool canInteract = true;

        public Transform InteractableTransform => transform;

        public virtual bool CanInteract(GameObject interactor)
        {
            return canInteract && isActiveAndEnabled;
        }

        public void SetInteractable(bool value)
        {
            canInteract = value;
        }

        protected virtual void Reset()
        {
            Collider2D col = GetComponent<Collider2D>();
            if (col != null)
                col.isTrigger = true;
        }

        public abstract void Interact(GameObject interactor);
    }
}

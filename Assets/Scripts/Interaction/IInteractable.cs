using UnityEngine;

namespace Game.Interaction
{
    public interface IInteractable
    {
        Transform InteractableTransform { get; }
        bool CanInteract(GameObject interactor);
        void Interact(GameObject interactor);
    }
}

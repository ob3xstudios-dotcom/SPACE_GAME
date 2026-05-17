using UnityEngine;
using UnityEngine.Events;

namespace Game.Interaction
{
    public class EventInteractable : InteractableBase
    {
        [SerializeField] private UnityEvent<GameObject> onInteract;

        public override void Interact(GameObject interactor)
        {
            onInteract?.Invoke(interactor);
        }
    }
}

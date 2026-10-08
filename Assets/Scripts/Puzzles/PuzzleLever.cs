using Game.Interaction;
using Game.Player;
using UnityEngine;

namespace Game.Puzzles
{
    /// <summary>
    /// Discrete, player-operated puzzle source that uses the shared interaction pipeline.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class PuzzleLever : PuzzleSignalSource, IInteractable
    {
        [Header("Lever")]
        [SerializeField] private bool startActive;
        [SerializeField] private bool allowDeactivate = true;
        [SerializeField, Min(0f)] private float interactionCooldown = 0.15f;
        [SerializeField] private bool requirePlayerInteractor = true;

        [Header("Interaction")]
        [SerializeField] private BoxCollider2D interactionTrigger;

        private float nextInteractionTime;

        public Transform InteractableTransform => transform;

        private void Awake()
        {
            ResolveTrigger();
            EnsureTrigger();
        }

        private void OnEnable()
        {
            nextInteractionTime = 0f;
            SetActive(startActive);
        }

        private void OnDisable()
        {
            nextInteractionTime = 0f;
            SetActive(false);
        }

        public bool CanInteract(GameObject interactor)
        {
            if (!isActiveAndEnabled || Time.time < nextInteractionTime)
                return false;

            if (IsActive && !allowDeactivate)
                return false;

            return !requirePlayerInteractor || ResolvePlayer(interactor) != null;
        }

        public void Interact(GameObject interactor)
        {
            if (!CanInteract(interactor))
                return;

            bool nextState = !IsActive;
            if (!allowDeactivate && !nextState)
                return;

            if (!SetActive(nextState))
                return;

            nextInteractionTime = Time.time + Mathf.Max(0f, interactionCooldown);

            if (DebugLogsEnabled)
                Debug.Log($"[PUZZLE][LEVER] name={name} state={IsActive}", this);
        }

        private static PlayerController ResolvePlayer(GameObject interactor)
        {
            return interactor != null
                ? interactor.GetComponentInParent<PlayerController>()
                : null;
        }

        private void ResolveTrigger()
        {
            if (interactionTrigger == null)
                interactionTrigger = GetComponent<BoxCollider2D>();
        }

        private void EnsureTrigger()
        {
            if (interactionTrigger != null)
                interactionTrigger.isTrigger = true;
        }

        private void Reset()
        {
            ResolveTrigger();
            EnsureTrigger();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            interactionCooldown = Mathf.Max(0f, interactionCooldown);
            ResolveTrigger();
            EnsureTrigger();
        }
#endif

        private void OnDrawGizmosSelected()
        {
            ResolveTrigger();
            if (interactionTrigger == null)
                return;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = interactionTrigger.transform.localToWorldMatrix;
            Gizmos.color = IsActive
                ? new Color(0.2f, 1f, 0.35f, 0.9f)
                : new Color(1f, 0.65f, 0.15f, 0.9f);
            Gizmos.DrawWireCube(interactionTrigger.offset, interactionTrigger.size);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}

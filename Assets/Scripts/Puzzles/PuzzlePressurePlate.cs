using System.Collections.Generic;
using Game.Player;
using UnityEngine;

namespace Game.Puzzles
{
    public enum PuzzlePressurePlateFilter
    {
        PlayerOnly,
        Rigidbody2D,
        LayerMask
    }

    /// <summary>
    /// Presence-driven signal source with collider-to-occupant contact aggregation.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class PuzzlePressurePlate : PuzzleSignalSource
    {
        [Header("Trigger")]
        [SerializeField] private BoxCollider2D triggerCollider;

        [Header("Activation Filter")]
        [SerializeField] private PuzzlePressurePlateFilter activationFilter =
            PuzzlePressurePlateFilter.PlayerOnly;
        [SerializeField] private LayerMask activationLayers = ~0;
        [SerializeField, Min(1)] private int requiredOccupants = 1;

        private readonly Dictionary<Collider2D, GameObject> contactOwners =
            new Dictionary<Collider2D, GameObject>();
        private readonly Dictionary<GameObject, HashSet<Collider2D>> occupantContacts =
            new Dictionary<GameObject, HashSet<Collider2D>>();

        public int ValidOccupantCount => occupantContacts.Count;

        private void Awake()
        {
            ResolveTrigger();
            EnsureTrigger();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            RegisterContact(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            RegisterContact(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            RemoveContact(other);
        }

        private void OnDisable()
        {
            bool hadContacts = contactOwners.Count > 0;
            contactOwners.Clear();
            occupantContacts.Clear();
            bool stateChanged = SetActive(false);

            if (DebugLogsEnabled && (hadContacts || stateChanged))
                LogPlateState();
        }

        private void RegisterContact(Collider2D other)
        {
            if (other == null)
                return;

            GameObject resolvedOwner = ResolveOccupant(other);
            if (contactOwners.TryGetValue(other, out GameObject previousOwner))
            {
                if (previousOwner == resolvedOwner && resolvedOwner != null)
                    return;

                RemoveContact(other);
            }

            if (resolvedOwner == null)
                return;

            int previousOccupantCount = occupantContacts.Count;
            contactOwners.Add(other, resolvedOwner);

            if (!occupantContacts.TryGetValue(resolvedOwner, out HashSet<Collider2D> contacts))
            {
                contacts = new HashSet<Collider2D>();
                occupantContacts.Add(resolvedOwner, contacts);
            }

            contacts.Add(other);
            if (occupantContacts.Count != previousOccupantCount)
                RefreshState();
        }

        private void RemoveContact(Collider2D other)
        {
            if (other == null || !contactOwners.TryGetValue(other, out GameObject owner))
                return;

            contactOwners.Remove(other);
            int previousOccupantCount = occupantContacts.Count;

            if (occupantContacts.TryGetValue(owner, out HashSet<Collider2D> contacts))
            {
                contacts.Remove(other);
                if (contacts.Count == 0)
                    occupantContacts.Remove(owner);
            }

            if (occupantContacts.Count != previousOccupantCount)
                RefreshState();
        }

        private GameObject ResolveOccupant(Collider2D other)
        {
            switch (activationFilter)
            {
                case PuzzlePressurePlateFilter.Rigidbody2D:
                {
                    Rigidbody2D body = other.attachedRigidbody;
                    return body != null ? body.gameObject : null;
                }

                case PuzzlePressurePlateFilter.LayerMask:
                {
                    int layerBit = 1 << other.gameObject.layer;
                    if ((activationLayers.value & layerBit) == 0)
                        return null;

                    Rigidbody2D body = other.attachedRigidbody;
                    return body != null ? body.gameObject : other.transform.root.gameObject;
                }

                default:
                {
                    PlayerController player = other.GetComponentInParent<PlayerController>();
                    return player != null ? player.gameObject : null;
                }
            }
        }

        private void RefreshState()
        {
            SetActive(occupantContacts.Count >= Mathf.Max(1, requiredOccupants));

            if (DebugLogsEnabled)
                LogPlateState();
        }

        private void LogPlateState()
        {
            Debug.Log(
                $"[PUZZLE][PLATE] name={name} occupants={occupantContacts.Count} state={IsActive}",
                this);
        }

        private void ResolveTrigger()
        {
            if (triggerCollider == null)
                triggerCollider = GetComponent<BoxCollider2D>();
        }

        private void EnsureTrigger()
        {
            if (triggerCollider != null)
                triggerCollider.isTrigger = true;
        }

        private void Reset()
        {
            ResolveTrigger();
            EnsureTrigger();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            requiredOccupants = Mathf.Max(1, requiredOccupants);
            ResolveTrigger();
            EnsureTrigger();
        }
#endif

        private void OnDrawGizmosSelected()
        {
            ResolveTrigger();
            if (triggerCollider == null)
                return;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = triggerCollider.transform.localToWorldMatrix;
            Gizmos.color = IsActive
                ? new Color(0.2f, 1f, 0.35f, 0.9f)
                : new Color(1f, 0.7f, 0.15f, 0.9f);
            Gizmos.DrawWireCube(triggerCollider.offset, triggerCollider.size);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}

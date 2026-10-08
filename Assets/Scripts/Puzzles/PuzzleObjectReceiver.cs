using System;
using System.Collections.Generic;
using Game.Player;
using UnityEngine;

namespace Game.Puzzles
{
    public enum PuzzleObjectFilterMode
    {
        AnyRigidbody2D,
        SpecificObject,
        LayerMask,
        ObjectId
    }

    /// <summary>
    /// Detects accepted physical objects without changing their transform or physics state.
    /// Each Rigidbody2D GameObject is one canonical object regardless of collider count.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class PuzzleObjectReceiver : PuzzleSignalSource
    {
        [Header("Trigger")]
        [SerializeField] private BoxCollider2D triggerCollider;

        [Header("Object Filter")]
        [SerializeField] private PuzzleObjectFilterMode filterMode =
            PuzzleObjectFilterMode.AnyRigidbody2D;
        [SerializeField] private Rigidbody2D specificObject;
        [Tooltip("Layer of the canonical Rigidbody2D GameObject, not the individual collider layer.")]
        [SerializeField] private LayerMask acceptedLayers = ~0;
        [SerializeField] private string acceptedObjectId;
        [SerializeField] private bool excludePlayer = true;
        [SerializeField, Min(1)] private int requiredObjects = 1;

        private readonly Dictionary<Collider2D, GameObject> contactOwners =
            new Dictionary<Collider2D, GameObject>();
        private readonly Dictionary<GameObject, HashSet<Collider2D>> objectContacts =
            new Dictionary<GameObject, HashSet<Collider2D>>();

        public int ValidObjectCount => objectContacts.Count;

        private void Awake()
        {
            acceptedObjectId = PuzzleObjectMarker.NormalizeId(acceptedObjectId);
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
            int previousObjectCount = objectContacts.Count;
            contactOwners.Clear();
            objectContacts.Clear();
            bool stateChanged = SetActive(false);

            if (DebugLogsEnabled && (previousObjectCount > 0 || stateChanged))
                LogState();
        }

        private void RegisterContact(Collider2D other)
        {
            if (other == null)
                return;

            GameObject resolvedOwner = ResolveObject(other, out PuzzleObjectMarker marker);
            if (contactOwners.TryGetValue(other, out GameObject previousOwner))
            {
                if (previousOwner == resolvedOwner && resolvedOwner != null)
                    return;

                RemoveContact(other);
            }

            if (resolvedOwner == null)
                return;

            contactOwners.Add(other, resolvedOwner);
            bool isNewObject = !objectContacts.TryGetValue(
                resolvedOwner,
                out HashSet<Collider2D> contacts);

            if (isNewObject)
            {
                contacts = new HashSet<Collider2D>();
                objectContacts.Add(resolvedOwner, contacts);
            }

            contacts.Add(other);
            if (!isNewObject)
                return;

            if (DebugLogsEnabled)
                LogAcceptedObject(resolvedOwner, marker);

            RefreshState();
        }

        private void RemoveContact(Collider2D other)
        {
            if (other == null || !contactOwners.TryGetValue(other, out GameObject owner))
                return;

            contactOwners.Remove(other);
            if (!objectContacts.TryGetValue(owner, out HashSet<Collider2D> contacts))
                return;

            contacts.Remove(other);
            if (contacts.Count > 0)
                return;

            objectContacts.Remove(owner);
            RefreshState();
        }

        private GameObject ResolveObject(Collider2D other, out PuzzleObjectMarker marker)
        {
            marker = null;

            Rigidbody2D body = other.attachedRigidbody;
            if (body == null || !body.simulated || !body.gameObject.activeInHierarchy)
                return null;

            if (excludePlayer && ResolvePlayer(other, body) != null)
                return null;

            GameObject owner = body.gameObject;
            switch (filterMode)
            {
                case PuzzleObjectFilterMode.SpecificObject:
                    return body == specificObject ? owner : null;

                case PuzzleObjectFilterMode.LayerMask:
                {
                    int layerBit = 1 << owner.layer;
                    return (acceptedLayers.value & layerBit) != 0 ? owner : null;
                }

                case PuzzleObjectFilterMode.ObjectId:
                {
                    marker = ResolveMarker(other, body);
                    string acceptedId = PuzzleObjectMarker.NormalizeId(acceptedObjectId);
                    return marker != null &&
                           acceptedId.Length > 0 &&
                           string.Equals(marker.ObjectId, acceptedId, StringComparison.Ordinal)
                        ? owner
                        : null;
                }

                default:
                    return owner;
            }
        }

        private static PlayerController ResolvePlayer(Collider2D other, Rigidbody2D body)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            return player != null ? player : body.GetComponentInParent<PlayerController>();
        }

        private static PuzzleObjectMarker ResolveMarker(Collider2D other, Rigidbody2D body)
        {
            Transform current = other.transform;
            Transform bodyTransform = body.transform;

            while (current != null)
            {
                PuzzleObjectMarker marker = current.GetComponent<PuzzleObjectMarker>();
                if (marker != null)
                    return marker;

                if (current == bodyTransform)
                    break;

                current = current.parent;
            }

            return body.GetComponent<PuzzleObjectMarker>();
        }

        private void RefreshState()
        {
            SetActive(objectContacts.Count >= Mathf.Max(1, requiredObjects));

            if (DebugLogsEnabled)
                LogState();
        }

        private void LogState()
        {
            Debug.Log(
                $"[PUZZLE][OBJECT RECEIVER] name={name} objects={objectContacts.Count} state={IsActive}",
                this);
        }

        private void LogAcceptedObject(GameObject acceptedObject, PuzzleObjectMarker marker)
        {
            string id = marker != null ? marker.ObjectId : string.Empty;
            Debug.Log(
                $"[PUZZLE][OBJECT RECEIVER] accepted={acceptedObject.name} filter={filterMode} id={id}",
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
            acceptedObjectId = PuzzleObjectMarker.NormalizeId(acceptedObjectId);
            requiredObjects = Mathf.Max(1, requiredObjects);
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
                : new Color(1f, 0.65f, 0.15f, 0.9f);
            Gizmos.DrawWireCube(triggerCollider.offset, triggerCollider.size);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}

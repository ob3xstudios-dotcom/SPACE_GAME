using System.Collections.Generic;
using Game.Player;
using UnityEngine;

namespace Game.World.Traps.Generic
{
    /// <summary>
    /// Trigger volume that refreshes a temporary ground-traction modifier on PlayerController.
    /// Visual presentation is intentionally owned by a separate component.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class SlipperyGooZone : MonoBehaviour
    {
        [Header("Slippery effect")]
        [SerializeField, Min(0.01f)] private float lingerDuration = 3f;
        [SerializeField, Range(0f, 1f)] private float groundAccelerationMultiplier = 0.55f;
        [SerializeField, Range(0f, 1f)] private float groundDecelerationMultiplier = 0.08f;
        [SerializeField, Range(0f, 1f)] private float turnResponsivenessMultiplier = 0.22f;
        [SerializeField] private LayerMask targetLayers;

        [Header("Volume")]
        [SerializeField] private BoxCollider2D gooCollider;

        private readonly Dictionary<Collider2D, PlayerController> contacts =
            new Dictionary<Collider2D, PlayerController>();
        private readonly HashSet<PlayerController> refreshedThisPhysicsStep =
            new HashSet<PlayerController>();

        private void Awake()
        {
            EnsureCollider();
            gooCollider.isTrigger = true;
        }

        private void OnValidate()
        {
            lingerDuration = Mathf.Max(0.01f, lingerDuration);
            groundAccelerationMultiplier = Mathf.Clamp01(groundAccelerationMultiplier);
            groundDecelerationMultiplier = Mathf.Clamp01(groundDecelerationMultiplier);
            turnResponsivenessMultiplier = Mathf.Clamp01(turnResponsivenessMultiplier);
            EnsureCollider();

            if (gooCollider != null)
                gooCollider.isTrigger = true;
        }

        private void FixedUpdate()
        {
            refreshedThisPhysicsStep.Clear();
        }

        private void OnDisable()
        {
            contacts.Clear();
            refreshedThisPhysicsStep.Clear();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            RegisterAndRefresh(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            RegisterAndRefresh(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (other != null)
                contacts.Remove(other);
        }

        private void RegisterAndRefresh(Collider2D other)
        {
            if (other == null || !IsLayerAllowed(other.gameObject.layer))
                return;

            if (!contacts.TryGetValue(other, out PlayerController controller) || controller == null)
            {
                controller =
                    other.GetComponent<PlayerController>() ??
                    other.GetComponentInParent<PlayerController>();
                if (controller == null)
                    return;

                contacts[other] = controller;
            }

            if (!controller.isActiveAndEnabled || !refreshedThisPhysicsStep.Add(controller))
                return;

            controller.ApplySlipperyEffect(
                lingerDuration,
                groundAccelerationMultiplier,
                groundDecelerationMultiplier,
                turnResponsivenessMultiplier);
        }

        private bool IsLayerAllowed(int layer)
        {
            return targetLayers.value == 0 || (targetLayers.value & (1 << layer)) != 0;
        }

        private void EnsureCollider()
        {
            if (gooCollider == null)
                gooCollider = GetComponent<BoxCollider2D>();
        }

        private void OnDrawGizmos()
        {
            BoxCollider2D box = gooCollider != null
                ? gooCollider
                : GetComponent<BoxCollider2D>();
            if (box == null)
                return;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.1f, 1f, 0.25f, 0.18f);
            Gizmos.DrawCube(box.offset, box.size);
            Gizmos.color = new Color(0.1f, 1f, 0.25f, 0.95f);
            Gizmos.DrawWireCube(box.offset, box.size);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}

using System.Collections.Generic;
using Game.Player;
using Game.Systems;
using UnityEngine;

namespace Game.World.Traps.Generic
{
    /// <summary>
    /// Art-independent trigger volume that requests the scene's existing fatal-fall flow.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class DeathVolume2D : MonoBehaviour
    {
        [Header("Detection")]
        [SerializeField] private LayerMask targetLayers = 1 << 9;
        [SerializeField] private BoxCollider2D deathCollider;

        [Header("Respawn")]
        [SerializeField] private SceneFallRespawn coordinator;

        private readonly Dictionary<Collider2D, PlayerHealth> contactOwners =
            new Dictionary<Collider2D, PlayerHealth>();
        private readonly Dictionary<PlayerHealth, int> contactCounts =
            new Dictionary<PlayerHealth, int>();
        private bool missingCoordinatorLogged;

        private void Awake()
        {
            EnsureCollider();
            deathCollider.isTrigger = true;
            ResolveCoordinator();
        }

        private void OnValidate()
        {
            EnsureCollider();
            if (deathCollider != null)
                deathCollider.isTrigger = true;
        }

        private void OnDisable()
        {
            contactOwners.Clear();
            contactCounts.Clear();
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
            if (other == null || !contactOwners.TryGetValue(other, out PlayerHealth playerHealth))
                return;

            contactOwners.Remove(other);
            if (playerHealth == null || !contactCounts.TryGetValue(playerHealth, out int count))
                return;

            count--;
            if (count > 0)
            {
                contactCounts[playerHealth] = count;
                return;
            }

            contactCounts.Remove(playerHealth);
        }

        private void RegisterContact(Collider2D other)
        {
            if (other == null || contactOwners.ContainsKey(other) || !IsLayerAllowed(other.gameObject.layer))
                return;

            PlayerHealth playerHealth =
                other.GetComponent<PlayerHealth>() ??
                other.GetComponentInParent<PlayerHealth>();
            if (playerHealth == null)
                return;

            contactOwners.Add(other, playerHealth);
            if (contactCounts.TryGetValue(playerHealth, out int count))
            {
                contactCounts[playerHealth] = count + 1;
                return;
            }

            contactCounts.Add(playerHealth, 1);
            RequestFatalFall(playerHealth.transform);
        }

        private void RequestFatalFall(Transform playerTransform)
        {
            ResolveCoordinator();
            if (coordinator != null && coordinator.isActiveAndEnabled)
            {
                coordinator.RequestFatalFall(playerTransform);
                return;
            }

            if (missingCoordinatorLogged)
                return;

            missingCoordinatorLogged = true;
            Debug.LogError(
                $"[{nameof(DeathVolume2D)}] No active {nameof(SceneFallRespawn)} coordinator was found.",
                this);
        }

        private void ResolveCoordinator()
        {
            if (coordinator != null && coordinator.isActiveAndEnabled)
                return;

            coordinator = FindObjectOfType<SceneFallRespawn>();
            if (coordinator != null)
                missingCoordinatorLogged = false;
        }

        private bool IsLayerAllowed(int layer)
        {
            return targetLayers.value == 0 || (targetLayers.value & (1 << layer)) != 0;
        }

        private void EnsureCollider()
        {
            if (deathCollider == null)
                deathCollider = GetComponent<BoxCollider2D>();
        }

        private void OnDrawGizmos()
        {
            BoxCollider2D box = deathCollider != null
                ? deathCollider
                : GetComponent<BoxCollider2D>();
            if (box == null)
                return;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.95f, 0.05f, 0.05f, 0.2f);
            Gizmos.DrawCube(box.offset, box.size);
            Gizmos.color = new Color(1f, 0.05f, 0.05f, 0.95f);
            Gizmos.DrawWireCube(box.offset, box.size);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}

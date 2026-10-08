using System.Collections.Generic;
using Game.Player;
using UnityEngine;

namespace Game.World.Traps
{
    /// <summary>
    /// Trigger-only damage zone. It records one valid hit per player contact and activation.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class FlameJetDamageZone : MonoBehaviour
    {
        private const int InitialOverlapCapacity = 16;

        [SerializeField] private BoxCollider2D damageCollider;

        private readonly Dictionary<PlayerHealth, HashSet<Collider2D>> contacts =
            new Dictionary<PlayerHealth, HashSet<Collider2D>>();
        private readonly HashSet<PlayerHealth> damagedThisContact = new HashSet<PlayerHealth>();
        private readonly Collider2D[] overlapResults = new Collider2D[InitialOverlapCapacity];

        private Transform damageSource;
        private LayerMask targetLayers;
        private int damage = 1;
        private bool emissionActive;
        private bool debugLogs;

        private void Awake()
        {
            EnsureCollider();
            damageCollider.enabled = emissionActive;
        }

        private void OnDisable()
        {
            emissionActive = false;
            contacts.Clear();
            damagedThisContact.Clear();

            if (damageCollider != null)
                damageCollider.enabled = false;
        }

        public void Configure(
            Transform source,
            int configuredDamage,
            LayerMask configuredTargetLayers,
            float length,
            float width,
            bool enableDebugLogs)
        {
            EnsureCollider();

            damageSource = source;
            damage = Mathf.Max(1, configuredDamage);
            targetLayers = configuredTargetLayers;
            debugLogs = enableDebugLogs;

            damageCollider.isTrigger = true;
            damageCollider.offset = new Vector2(Mathf.Max(0.1f, length) * 0.5f, 0f);
            damageCollider.size = new Vector2(Mathf.Max(0.1f, length), Mathf.Max(0.1f, width));
        }

        public void SetEmissionActive(bool active)
        {
            EnsureCollider();

            if (emissionActive == active && damageCollider.enabled == active)
                return;

            emissionActive = active;
            contacts.Clear();
            damagedThisContact.Clear();
            damageCollider.enabled = active;

            if (active)
                DamagePlayersAlreadyInside();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            RegisterContact(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            // This also gives a player who entered during i-frames one valid hit once protected time ends.
            RegisterContact(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            PlayerHealth playerHealth = FindPlayerHealth(other);
            if (playerHealth == null || !contacts.TryGetValue(playerHealth, out HashSet<Collider2D> colliders))
                return;

            colliders.Remove(other);
            if (colliders.Count > 0)
                return;

            contacts.Remove(playerHealth);
            damagedThisContact.Remove(playerHealth);
        }

        private void DamagePlayersAlreadyInside()
        {
            ContactFilter2D filter = new ContactFilter2D().NoFilter();
            filter.useTriggers = true;

            if (targetLayers.value != 0)
                filter.SetLayerMask(targetLayers);

            int count = damageCollider.OverlapCollider(filter, overlapResults);
            for (int i = 0; i < count; i++)
            {
                Collider2D overlap = overlapResults[i];
                if (overlap != null)
                    RegisterContact(overlap);
            }

            if (count == overlapResults.Length && debugLogs)
                Debug.LogWarning($"[FLAME JET] {name}: overlap buffer is full; some contacts may wait until the next physics step.", this);
        }

        private void RegisterContact(Collider2D other)
        {
            if (!emissionActive || other == null || !IsLayerAllowed(other.gameObject.layer))
                return;

            PlayerHealth playerHealth = FindPlayerHealth(other);
            if (playerHealth == null)
                return;

            if (!contacts.TryGetValue(playerHealth, out HashSet<Collider2D> colliders))
            {
                colliders = new HashSet<Collider2D>();
                contacts.Add(playerHealth, colliders);
            }

            colliders.Add(other);
            TryDamage(playerHealth);
        }

        private void TryDamage(PlayerHealth playerHealth)
        {
            if (damagedThisContact.Contains(playerHealth) || playerHealth.IsInvulnerable)
                return;

            // Mark before invoking PlayerHealth so multiple Player colliders cannot cause duplicate hits.
            damagedThisContact.Add(playerHealth);
            Vector2 sourcePosition = damageSource != null ? damageSource.position : transform.position;
            playerHealth.TakeDamage(damage, sourcePosition);

            if (debugLogs)
                Debug.Log($"[FLAME JET] {name}: dealt {damage} damage to {playerHealth.name}.", this);
        }

        private bool IsLayerAllowed(int layer)
        {
            return targetLayers.value == 0 || (targetLayers.value & (1 << layer)) != 0;
        }

        private static PlayerHealth FindPlayerHealth(Collider2D other)
        {
            return other.GetComponent<PlayerHealth>() ?? other.GetComponentInParent<PlayerHealth>();
        }

        private void EnsureCollider()
        {
            if (damageCollider == null)
                damageCollider = GetComponent<BoxCollider2D>();
        }
    }
}

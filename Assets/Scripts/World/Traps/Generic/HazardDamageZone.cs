using System.Collections.Generic;
using Game.Player;
using UnityEngine;

namespace Game.World.Traps.Generic
{
    public enum HazardDamageMode
    {
        OnEnter,
        RepeatingWhileInside
    }

    /// <summary>
    /// Art-independent trigger volume that delegates all health, i-frame, knockback,
    /// death and respawn decisions to PlayerHealth.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class HazardDamageZone : MonoBehaviour
    {
        [Header("Damage")]
        [SerializeField, Min(1)] private int damage = 1;
        [SerializeField] private HazardDamageMode damageMode = HazardDamageMode.OnEnter;
        [SerializeField, Min(0.01f)] private float repeatInterval = 0.5f;
        [SerializeField] private LayerMask targetLayers;

        [Header("Volume")]
        [SerializeField] private BoxCollider2D damageCollider;

        private readonly Dictionary<Collider2D, PlayerHealth> contactOwners =
            new Dictionary<Collider2D, PlayerHealth>();
        private readonly Dictionary<PlayerHealth, int> contactCounts =
            new Dictionary<PlayerHealth, int>();
        private readonly Dictionary<PlayerHealth, float> nextDamageTimes =
            new Dictionary<PlayerHealth, float>();
        private readonly List<PlayerHealth> stalePlayers = new List<PlayerHealth>(2);

        private void Awake()
        {
            EnsureCollider();
            damageCollider.isTrigger = true;
        }

        private void OnValidate()
        {
            damage = Mathf.Max(1, damage);
            repeatInterval = Mathf.Max(0.01f, repeatInterval);
            EnsureCollider();

            if (damageCollider != null)
                damageCollider.isTrigger = true;
        }

        private void OnDisable()
        {
            contactOwners.Clear();
            contactCounts.Clear();
            nextDamageTimes.Clear();
            stalePlayers.Clear();
        }

        private void FixedUpdate()
        {
            if (damageMode != HazardDamageMode.RepeatingWhileInside || contactCounts.Count == 0)
                return;

            float now = Time.time;
            stalePlayers.Clear();

            foreach (KeyValuePair<PlayerHealth, int> pair in contactCounts)
            {
                PlayerHealth playerHealth = pair.Key;
                if (playerHealth == null || pair.Value <= 0)
                {
                    stalePlayers.Add(playerHealth);
                    continue;
                }

                if (nextDamageTimes.TryGetValue(playerHealth, out float nextTime) && now < nextTime)
                    continue;

                nextDamageTimes[playerHealth] = now + repeatInterval;
                DamagePlayer(playerHealth);
            }

            for (int i = 0; i < stalePlayers.Count; i++)
            {
                PlayerHealth stalePlayer = stalePlayers[i];
                contactCounts.Remove(stalePlayer);
                nextDamageTimes.Remove(stalePlayer);
            }
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
            nextDamageTimes.Remove(playerHealth);
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
            nextDamageTimes[playerHealth] = Time.time + repeatInterval;
            DamagePlayer(playerHealth);
        }

        private void DamagePlayer(PlayerHealth playerHealth)
        {
            if (playerHealth == null)
                return;

            playerHealth.TakeDamage(damage, transform.position);
        }

        private bool IsLayerAllowed(int layer)
        {
            return targetLayers.value == 0 || (targetLayers.value & (1 << layer)) != 0;
        }

        private void EnsureCollider()
        {
            if (damageCollider == null)
                damageCollider = GetComponent<BoxCollider2D>();
        }

        private void OnDrawGizmos()
        {
            BoxCollider2D box = damageCollider != null
                ? damageCollider
                : GetComponent<BoxCollider2D>();
            if (box == null)
                return;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.12f, 0.05f, 0.18f);
            Gizmos.DrawCube(box.offset, box.size);
            Gizmos.color = new Color(1f, 0.12f, 0.05f, 0.95f);
            Gizmos.DrawWireCube(box.offset, box.size);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}

using Game.Player;
using UnityEngine;

namespace Game.World.Pickups
{
    /// <summary>
    /// Heals a PlayerHealth on contact and is consumed only when health increases.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public sealed class HealthPickup : MonoBehaviour
    {
        [SerializeField, Min(1)] private int healAmount = 1;
        [SerializeField] private Collider2D pickupCollider;

        private bool isConsumed;
        private bool isHealing;

        private void Awake()
        {
            healAmount = Mathf.Max(1, healAmount);
            EnsureCollider();

            if (pickupCollider != null)
                pickupCollider.isTrigger = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (isConsumed || isHealing || other == null)
                return;

            PlayerHealth playerHealth =
                other.GetComponent<PlayerHealth>() ??
                other.GetComponentInParent<PlayerHealth>();

            if (playerHealth == null)
                return;

            TryConsume(playerHealth);
        }

        private void TryConsume(PlayerHealth playerHealth)
        {
            bool healthIncreased = false;

            void HandleHeartsChanged(int current, int max)
            {
                healthIncreased = true;
            }

            // PlayerHealth.Heal is the canonical API. Its event is raised synchronously only
            // when IntResource.Restore actually changes the current health value.
            isHealing = true;
            playerHealth.OnHeartsChanged += HandleHeartsChanged;

            try
            {
                playerHealth.Heal(healAmount);
            }
            finally
            {
                playerHealth.OnHeartsChanged -= HandleHeartsChanged;
                isHealing = false;
            }

            if (!healthIncreased)
                return;

            // Mark and disable before Destroy so another Player collider in the same
            // physics step cannot trigger a second heal.
            isConsumed = true;

            if (pickupCollider != null)
                pickupCollider.enabled = false;

            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        private void Reset()
        {
            EnsureCollider();

            if (pickupCollider != null)
                pickupCollider.isTrigger = true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            healAmount = Mathf.Max(1, healAmount);
            EnsureCollider();

            if (pickupCollider != null)
                pickupCollider.isTrigger = true;
        }
#endif

        private void EnsureCollider()
        {
            if (pickupCollider == null)
                pickupCollider = GetComponent<Collider2D>();
        }
    }
}

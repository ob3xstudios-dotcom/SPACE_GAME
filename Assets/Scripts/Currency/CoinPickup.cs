using System;
using Game.Player;
using UnityEngine;

namespace Game.Currency
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public sealed class CoinPickup : MonoBehaviour
    {
        [Header("Persistent identity")]
        [Tooltip("Stable and unique within this scene. Generate a new ID for every placed instance.")]
        [SerializeField] private string pickupId = string.Empty;

        [Header("Coin value")]
        [SerializeField, Min(1)] private int coinAmount = 1;
        [SerializeField] private Collider2D pickupCollider;

        private bool consumed;

        public string PickupId => pickupId;
        public int CoinAmount => coinAmount;

        private void Awake()
        {
            coinAmount = Mathf.Max(1, coinAmount);
            pickupId = CurrencySaveData.NormalizeId(pickupId);
            ResolveCollider();
            if (pickupCollider != null)
                pickupCollider.isTrigger = true;

            if (string.IsNullOrEmpty(pickupId))
            {
                Debug.LogWarning(
                    $"[CURRENCY] CoinPickup '{name}' has no persistent ID and cannot be collected safely. " +
                    "Use 'Currency/Generate New Persistent ID' on its placed scene instance.",
                    this);
                return;
            }

            string sceneName = gameObject.scene.name;
            if (CurrencyService.Instance.IsPlacedCoinCollected(sceneName, pickupId))
                ConsumeWithoutCredit();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (consumed || other == null || string.IsNullOrEmpty(pickupId))
                return;

            PlayerController player = other.GetComponent<PlayerController>() ??
                                      other.GetComponentInParent<PlayerController>();
            if (player == null)
                return;

            string sceneName = gameObject.scene.name;
            CurrencyService service = CurrencyService.Instance;
            if (service.IsPlacedCoinCollected(sceneName, pickupId))
            {
                ConsumeWithoutCredit();
                return;
            }

            if (service.TryCollectPlacedCoin(sceneName, pickupId, coinAmount))
                ConsumeWithoutCredit();
        }

        private void ConsumeWithoutCredit()
        {
            if (consumed)
                return;

            consumed = true;
            if (pickupCollider != null)
                pickupCollider.enabled = false;

            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        private void Reset()
        {
            coinAmount = 1;
            ResolveCollider();
            if (pickupCollider != null)
                pickupCollider.isTrigger = true;
        }

#if UNITY_EDITOR
        [ContextMenu("Currency/Generate New Persistent ID")]
        private void GenerateNewPersistentId()
        {
            UnityEditor.Undo.RecordObject(this, "Generate Coin Pickup ID");
            pickupId = Guid.NewGuid().ToString("N");
            UnityEditor.EditorUtility.SetDirty(this);
        }

        private void OnValidate()
        {
            pickupId = CurrencySaveData.NormalizeId(pickupId);
            coinAmount = Mathf.Max(1, coinAmount);
            ResolveCollider();
            if (pickupCollider != null)
                pickupCollider.isTrigger = true;
        }
#endif

        private void ResolveCollider()
        {
            if (pickupCollider == null)
                pickupCollider = GetComponent<Collider2D>();
        }
    }
}

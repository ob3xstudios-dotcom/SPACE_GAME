using Game.Player;
using UnityEngine;

namespace Game.Currency
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public sealed class DroppedCoinBag : MonoBehaviour
    {
        [SerializeField] private string bagId = string.Empty;
        [SerializeField, Min(0)] private int coinAmount;
        [SerializeField] private string sceneName = string.Empty;
        [SerializeField] private Collider2D pickupCollider;

        private readonly Collider2D[] overlapResults = new Collider2D[16];
        private bool consumed;
        private bool pickupArmed;

        public string BagId => bagId;
        public int CoinAmount => coinAmount;
        public string SceneName => sceneName;
        public bool PickupArmed => pickupArmed;

        private void Awake()
        {
            ResolveCollider();
            if (pickupCollider != null)
                pickupCollider.isTrigger = true;
        }

        private void FixedUpdate()
        {
            if (consumed || pickupArmed || pickupCollider == null || !pickupCollider.enabled)
                return;

            if (HasOverlappingPlayer())
                return;

            pickupArmed = true;
            CurrencyService.Instance.LogDiagnostic(
                $"[CURRENCY][BAG SPAWN] bagId='{bagId}' amount={coinAmount} " +
                $"scene='{sceneName}' position=({transform.position.x:0.###}, " +
                $"{transform.position.y:0.###}, {transform.position.z:0.###}) " +
                "result=armed reason=player-clear",
                this);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!pickupArmed || consumed || other == null ||
                coinAmount <= 0 || string.IsNullOrEmpty(bagId))
                return;

            PlayerController player = other.GetComponent<PlayerController>() ??
                                      other.GetComponentInParent<PlayerController>();
            if (player == null)
                return;

            if (!CurrencyService.Instance.TryRecoverDroppedBag(bagId))
                return;

            consumed = true;
            coinAmount = 0;
            if (pickupCollider != null)
                pickupCollider.enabled = false;

            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        public void Initialize(DroppedCoinBagSaveData saveData)
        {
            if (saveData == null)
                return;

            bagId = CurrencySaveData.NormalizeId(saveData.bagId);
            coinAmount = Mathf.Max(0, saveData.coinAmount);
            sceneName = saveData.sceneName != null ? saveData.sceneName.Trim() : string.Empty;
            transform.position = saveData.position;
            consumed = false;
            pickupArmed = false;
        }

        public static DroppedCoinBag CreateRuntime(DroppedCoinBagSaveData saveData)
        {
            if (saveData == null || string.IsNullOrEmpty(saveData.bagId) || saveData.coinAmount <= 0)
                return null;

            string shortId = saveData.bagId.Length > 8
                ? saveData.bagId.Substring(0, 8)
                : saveData.bagId;
            GameObject root = new GameObject("DroppedCoinBag_" + shortId);
            root.transform.position = saveData.position;

            CircleCollider2D collider = root.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 0.55f;

            DroppedCoinBag bag = root.AddComponent<DroppedCoinBag>();
            bag.pickupCollider = collider;
            bag.Initialize(saveData);

            GameObject visualObject = new GameObject("Visual");
            visualObject.transform.SetParent(root.transform, false);
            CurrencyPickupVisual visual = visualObject.AddComponent<CurrencyPickupVisual>();
            visual.Configure(CurrencyVisualKind.Bag, "Characters", 0);
            return bag;
        }

        private bool HasOverlappingPlayer()
        {
            Bounds bounds = pickupCollider.bounds;
            int count = Physics2D.OverlapBoxNonAlloc(
                bounds.center,
                bounds.size,
                0f,
                overlapResults);

            bool playerFound = false;
            for (int i = 0; i < count; i++)
            {
                Collider2D overlap = overlapResults[i];
                overlapResults[i] = null;
                if (overlap == null || overlap == pickupCollider)
                    continue;

                PlayerController player = overlap.GetComponent<PlayerController>() ??
                                          overlap.GetComponentInParent<PlayerController>();
                if (player != null)
                    playerFound = true;
            }

            return playerFound;
        }

        private void Reset()
        {
            ResolveCollider();
            if (pickupCollider != null)
                pickupCollider.isTrigger = true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            bagId = CurrencySaveData.NormalizeId(bagId);
            coinAmount = Mathf.Max(0, coinAmount);
            sceneName = sceneName != null ? sceneName.Trim() : string.Empty;
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

using UnityEngine;

namespace Game.Currency
{
    public sealed class CurrencyRuntimeSettings : ScriptableObject
    {
        private const string ResourcePath = "CurrencyRuntimeSettings";

        [SerializeField] private DroppedCoinBag droppedCoinBagPrefab;

        private static CurrencyRuntimeSettings cached;

        public DroppedCoinBag DroppedCoinBagPrefab => droppedCoinBagPrefab;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            cached = null;
        }

        public static CurrencyRuntimeSettings Load()
        {
            if (cached == null)
                cached = Resources.Load<CurrencyRuntimeSettings>(ResourcePath);
            return cached;
        }
    }
}

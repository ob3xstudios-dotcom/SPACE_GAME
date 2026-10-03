using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Currency
{
    [Serializable]
    public sealed class DroppedCoinBagSaveData
    {
        public string bagId = string.Empty;
        public int coinAmount;
        public string sceneName = string.Empty;
        public Vector3 position;

        public DroppedCoinBagSaveData Clone()
        {
            return new DroppedCoinBagSaveData
            {
                bagId = bagId,
                coinAmount = coinAmount,
                sceneName = sceneName,
                position = position
            };
        }
    }

    [Serializable]
    public sealed class CurrencySaveData
    {
        public const int CurrentSaveVersion = 1;

        public int saveVersion = CurrentSaveVersion;
        public int carriedCoins;
        public int bankedCoins;
        public List<string> collectedPickupKeys = new List<string>();
        public List<DroppedCoinBagSaveData> droppedCoinBags = new List<DroppedCoinBagSaveData>();

        public void Normalize()
        {
            if (saveVersion <= 0)
                saveVersion = CurrentSaveVersion;

            carriedCoins = Mathf.Max(0, carriedCoins);
            bankedCoins = Mathf.Max(0, bankedCoins);

            if (collectedPickupKeys == null)
                collectedPickupKeys = new List<string>();

            HashSet<string> uniquePickupKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = collectedPickupKeys.Count - 1; i >= 0; i--)
            {
                string key = NormalizeId(collectedPickupKeys[i]);
                if (string.IsNullOrEmpty(key) || !uniquePickupKeys.Add(key))
                    collectedPickupKeys.RemoveAt(i);
                else
                    collectedPickupKeys[i] = key;
            }
            collectedPickupKeys.Sort(StringComparer.Ordinal);

            if (droppedCoinBags == null)
                droppedCoinBags = new List<DroppedCoinBagSaveData>();

            HashSet<string> uniqueBagIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = droppedCoinBags.Count - 1; i >= 0; i--)
            {
                DroppedCoinBagSaveData bag = droppedCoinBags[i];
                if (bag == null)
                {
                    droppedCoinBags.RemoveAt(i);
                    continue;
                }

                bag.bagId = NormalizeId(bag.bagId);
                bag.sceneName = bag.sceneName != null ? bag.sceneName.Trim() : string.Empty;
                bag.coinAmount = Mathf.Max(0, bag.coinAmount);

                if (string.IsNullOrEmpty(bag.bagId) ||
                    string.IsNullOrEmpty(bag.sceneName) ||
                    bag.coinAmount <= 0 ||
                    !IsFinite(bag.position) ||
                    !uniqueBagIds.Add(bag.bagId))
                {
                    droppedCoinBags.RemoveAt(i);
                }
            }

            droppedCoinBags.Sort((left, right) =>
                string.CompareOrdinal(left.bagId, right.bagId));
        }

        public static string NormalizeId(string value)
        {
            return value != null ? value.Trim() : string.Empty;
        }

        public static string BuildPickupKey(string sceneName, string pickupId)
        {
            string scene = sceneName != null ? sceneName.Trim() : string.Empty;
            string id = NormalizeId(pickupId);
            return string.IsNullOrEmpty(scene) || string.IsNullOrEmpty(id)
                ? string.Empty
                : scene + "::" + id;
        }

        public static bool IsFinite(Vector3 value)
        {
            return
                !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}

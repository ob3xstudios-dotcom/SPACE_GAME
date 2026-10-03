using System;
using System.Collections.Generic;

namespace Game.Progression
{
    [Serializable]
    public sealed class CollectedTokenSaveData
    {
        public string pickupKey = string.Empty;
        public string tokenDefinitionId = string.Empty;

        public CollectedTokenSaveData Clone()
        {
            return new CollectedTokenSaveData
            {
                pickupKey = pickupKey,
                tokenDefinitionId = tokenDefinitionId
            };
        }
    }

    [Serializable]
    public sealed class ProgressionSaveData
    {
        public const int CurrentSaveVersion = 1;
        public const int LoadoutSlotCount = 4;

        public int saveVersion = CurrentSaveVersion;
        public List<CollectedTokenSaveData> collectedTokens = new List<CollectedTokenSaveData>();
        public List<string> unlockedAbilityIds = new List<string>();
        public string[] equippedAbilityIds = new string[LoadoutSlotCount];

        public void Normalize()
        {
            if (saveVersion <= 0)
                saveVersion = CurrentSaveVersion;
            if (collectedTokens == null)
                collectedTokens = new List<CollectedTokenSaveData>();
            if (unlockedAbilityIds == null)
                unlockedAbilityIds = new List<string>();

            NormalizeTokens();
            NormalizeUnlockedAbilities();
            NormalizeLoadout();
        }

        public static string NormalizeId(string value)
        {
            return value != null ? value.Trim() : string.Empty;
        }

        public static string BuildPickupKey(string sceneName, string pickupId)
        {
            string scene = NormalizeId(sceneName);
            string pickup = NormalizeId(pickupId);
            return string.IsNullOrEmpty(scene) || string.IsNullOrEmpty(pickup)
                ? string.Empty
                : scene + ":" + pickup;
        }

        private void NormalizeTokens()
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = collectedTokens.Count - 1; i >= 0; i--)
            {
                CollectedTokenSaveData token = collectedTokens[i];
                if (token == null)
                {
                    collectedTokens.RemoveAt(i);
                    continue;
                }

                token.pickupKey = NormalizeId(token.pickupKey);
                token.tokenDefinitionId = NormalizeId(token.tokenDefinitionId);
                if (string.IsNullOrEmpty(token.pickupKey) ||
                    string.IsNullOrEmpty(token.tokenDefinitionId) ||
                    !keys.Add(token.pickupKey))
                {
                    collectedTokens.RemoveAt(i);
                }
            }

            collectedTokens.Sort((left, right) =>
                string.CompareOrdinal(left.pickupKey, right.pickupKey));
        }

        private void NormalizeUnlockedAbilities()
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = unlockedAbilityIds.Count - 1; i >= 0; i--)
            {
                string id = NormalizeId(unlockedAbilityIds[i]);
                if (string.IsNullOrEmpty(id) || !ids.Add(id))
                    unlockedAbilityIds.RemoveAt(i);
                else
                    unlockedAbilityIds[i] = id;
            }

            unlockedAbilityIds.Sort(StringComparer.Ordinal);
        }

        private void NormalizeLoadout()
        {
            equippedAbilityIds = NormalizeAbilityLoadout(equippedAbilityIds);
        }

        internal static string[] NormalizeAbilityLoadout(
            string[] abilityIds,
            int preferredPromotionSlot = -1)
        {
            string[] normalized = new string[LoadoutSlotCount];
            HashSet<string> equipped = new HashSet<string>(StringComparer.Ordinal);
            if (abilityIds != null)
            {
                int count = Math.Min(abilityIds.Length, LoadoutSlotCount);
                for (int i = 0; i < count; i++)
                {
                    string id = NormalizeId(abilityIds[i]);
                    if (!string.IsNullOrEmpty(id) && equipped.Add(id))
                        normalized[i] = id;
                }
            }

            if (!string.IsNullOrEmpty(normalized[3]) &&
                (string.IsNullOrEmpty(normalized[0]) ||
                 string.IsNullOrEmpty(normalized[1]) ||
                 string.IsNullOrEmpty(normalized[2])))
            {
                int promotionSlot =
                    preferredPromotionSlot >= 0 &&
                    preferredPromotionSlot < 3 &&
                    string.IsNullOrEmpty(normalized[preferredPromotionSlot])
                        ? preferredPromotionSlot
                        : FindFirstEmptyNormalSlot(normalized);

                normalized[promotionSlot] = normalized[3];
                normalized[3] = string.Empty;
            }

            return normalized;
        }

        private static int FindFirstEmptyNormalSlot(string[] abilityIds)
        {
            for (int i = 0; i < 3; i++)
            {
                if (string.IsNullOrEmpty(abilityIds[i]))
                    return i;
            }

            return 0;
        }
    }
}

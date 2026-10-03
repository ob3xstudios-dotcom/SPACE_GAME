using System;
using System.Collections.Generic;

namespace Game.Progress
{
    [Serializable]
    public sealed class GameProgressData
    {
        public const int CurrentSaveVersion = 1;

        public int saveVersion = CurrentSaveVersion;
        public bool mapOwned;
        public List<string> confirmedExploredRegionIds = new List<string>();
        public string lastRestBenchId = string.Empty;
        public string lastRestScene = string.Empty;

        public void Normalize()
        {
            if (saveVersion <= 0)
                saveVersion = CurrentSaveVersion;

            if (confirmedExploredRegionIds == null)
                confirmedExploredRegionIds = new List<string>();

            HashSet<string> uniqueIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = confirmedExploredRegionIds.Count - 1; i >= 0; i--)
            {
                string normalizedId = NormalizeId(confirmedExploredRegionIds[i]);
                if (string.IsNullOrEmpty(normalizedId) || !uniqueIds.Add(normalizedId))
                    confirmedExploredRegionIds.RemoveAt(i);
                else
                    confirmedExploredRegionIds[i] = normalizedId;
            }

            confirmedExploredRegionIds.Sort(StringComparer.Ordinal);
            lastRestBenchId = NormalizeId(lastRestBenchId);
            lastRestScene = lastRestScene != null ? lastRestScene.Trim() : string.Empty;
        }

        public static string NormalizeId(string value)
        {
            return value != null ? value.Trim() : string.Empty;
        }
    }
}

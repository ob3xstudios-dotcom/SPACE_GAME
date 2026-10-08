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
        public List<string> confirmedMapCellIds = new List<string>();
        public string lastRestBenchId = string.Empty;
        public string lastRestScene = string.Empty;

        public void Normalize()
        {
            if (saveVersion <= 0)
                saveVersion = CurrentSaveVersion;

            if (confirmedExploredRegionIds == null)
                confirmedExploredRegionIds = new List<string>();
            if (confirmedMapCellIds == null)
                confirmedMapCellIds = new List<string>();

            NormalizeIds(confirmedExploredRegionIds);
            NormalizeIds(confirmedMapCellIds);
            lastRestBenchId = NormalizeId(lastRestBenchId);
            lastRestScene = lastRestScene != null ? lastRestScene.Trim() : string.Empty;
        }

        private static void NormalizeIds(List<string> ids)
        {
            HashSet<string> uniqueIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = ids.Count - 1; i >= 0; i--)
            {
                string normalizedId = NormalizeId(ids[i]);
                if (string.IsNullOrEmpty(normalizedId) || !uniqueIds.Add(normalizedId))
                    ids.RemoveAt(i);
                else
                    ids[i] = normalizedId;
            }

            ids.Sort(StringComparer.Ordinal);
        }

        public static string NormalizeId(string value)
        {
            return value != null ? value.Trim() : string.Empty;
        }
    }
}

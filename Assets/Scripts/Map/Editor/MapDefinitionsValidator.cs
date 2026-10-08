using System;
using System.Collections.Generic;
using System.Text;
using Game.Map;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools.MapAuthoring
{
    public static class MapDefinitionsValidator
    {
        [MenuItem("SpaceWorld/Map/Validate Map Definitions")]
        public static void Validate()
        {
            MapCellDefinition[] cells = LoadAssets<MapCellDefinition>("t:MapCellDefinition");
            MapAreaDefinition[] areas = LoadAssets<MapAreaDefinition>("t:MapAreaDefinition");
            int issueCount = ValidateCells(cells);
            issueCount += ValidateAreas(areas);

            for (int i = 0; i < areas.Length; i++)
                LogAreaSummary(areas[i]);

            string summary =
                $"[MAP DEFINITIONS] Validation complete: {cells.Length} cell(s), " +
                $"{areas.Length} area(s), {issueCount} issue(s).";
            if (issueCount == 0)
                Debug.Log(summary);
            else
                Debug.LogError(summary);
        }

        private static int ValidateCells(MapCellDefinition[] cells)
        {
            int issues = 0;
            Dictionary<string, MapCellDefinition> ids =
                new Dictionary<string, MapCellDefinition>(StringComparer.Ordinal);

            for (int i = 0; i < cells.Length; i++)
            {
                MapCellDefinition cell = cells[i];
                string cellId = Normalize(cell.CellId);
                if (string.IsNullOrEmpty(cellId))
                {
                    LogIssue("Empty Cell ID", cell);
                    issues++;
                }
                else if (ids.TryGetValue(cellId, out MapCellDefinition existing))
                {
                    Debug.LogError(
                        $"[MAP DEFINITIONS] Duplicate Cell ID '{cellId}' in " +
                        $"'{AssetDatabase.GetAssetPath(existing)}' and '{AssetDatabase.GetAssetPath(cell)}'.",
                        cell);
                    issues++;
                }
                else
                {
                    ids.Add(cellId, cell);
                }

                if (string.IsNullOrEmpty(Normalize(cell.AreaId)))
                {
                    LogIssue($"Cell '{cellId}' has an empty Area ID", cell);
                    issues++;
                }

                if (string.IsNullOrEmpty(Normalize(cell.RoomId)))
                {
                    LogIssue($"Cell '{cellId}' has an empty Room ID", cell);
                    issues++;
                }
            }

            return issues;
        }

        private static int ValidateAreas(MapAreaDefinition[] areas)
        {
            int issues = 0;
            for (int areaIndex = 0; areaIndex < areas.Length; areaIndex++)
            {
                MapAreaDefinition area = areas[areaIndex];
                if (string.IsNullOrEmpty(Normalize(area.AreaId)))
                {
                    LogIssue("Map area has an empty Area ID", area);
                    issues++;
                }

                IReadOnlyList<MapCellDefinition> cells = area.Cells;
                if (cells == null)
                {
                    LogIssue($"Area '{area.AreaId}' has a null cell list", area);
                    issues++;
                    continue;
                }

                HashSet<MapCellDefinition> uniqueCells = new HashSet<MapCellDefinition>();
                for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
                {
                    MapCellDefinition cell = cells[cellIndex];
                    if (cell == null)
                    {
                        LogIssue(
                            $"Area '{area.AreaId}' has a null cell reference at index {cellIndex}",
                            area);
                        issues++;
                    }
                    else if (!uniqueCells.Add(cell))
                    {
                        LogIssue(
                            $"Area '{area.AreaId}' repeats definition '{cell.CellId}'",
                            area);
                        issues++;
                    }
                }
            }

            return issues;
        }

        private static void LogAreaSummary(MapAreaDefinition area)
        {
            if (area == null || area.Cells == null)
                return;

            SortedDictionary<string, int> roomCounts =
                new SortedDictionary<string, int>(StringComparer.Ordinal);
            int cellCount = 0;
            for (int i = 0; i < area.Cells.Count; i++)
            {
                MapCellDefinition cell = area.Cells[i];
                if (cell == null)
                    continue;

                cellCount++;
                string roomId = Normalize(cell.RoomId);
                roomCounts.TryGetValue(roomId, out int count);
                roomCounts[roomId] = count + 1;
            }

            StringBuilder report = new StringBuilder();
            report.AppendLine($"{area.DisplayName}: {roomCounts.Count} rooms, {cellCount} cells");
            foreach (KeyValuePair<string, int> room in roomCounts)
                report.AppendLine($"{room.Key}: {room.Value}");

            Debug.Log(report.ToString().TrimEnd(), area);
        }

        private static T[] LoadAssets<T>(string filter) where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets(filter);
            List<T> assets = new List<T>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null)
                    assets.Add(asset);
            }

            return assets.ToArray();
        }

        private static void LogIssue(string message, UnityEngine.Object context)
        {
            Debug.LogError($"[MAP DEFINITIONS] {message}.", context);
        }

        private static string Normalize(string value)
        {
            return value != null ? value.Trim() : string.Empty;
        }
    }
}

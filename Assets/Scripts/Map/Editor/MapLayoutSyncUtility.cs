using System;
using System.Collections.Generic;
using System.Text;
using Game.Map;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.EditorTools.MapAuthoring
{
    public sealed class MapLayoutSyncReport
    {
        private readonly List<string> findings = new List<string>();

        public string AreaName { get; internal set; }
        public string LayoutName { get; internal set; }
        public int DefinitionCount { get; internal set; }
        public int Existing { get; internal set; }
        public int Created { get; internal set; }
        public int Missing { get; internal set; }
        public int Orphaned { get; internal set; }
        public int Duplicates { get; internal set; }
        public int MissingColliders { get; internal set; }
        public int NonTriggerColliders { get; internal set; }
        public int MissingContentRoots { get; internal set; }
        public int DefinitionIssues { get; internal set; }

        public bool HasProblems =>
            Missing > 0 ||
            Orphaned > 0 ||
            Duplicates > 0 ||
            MissingColliders > 0 ||
            NonTriggerColliders > 0 ||
            MissingContentRoots > 0 ||
            DefinitionIssues > 0;

        internal void AddFinding(string finding)
        {
            findings.Add(finding);
        }

        public override string ToString()
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine($"{AreaName} {LayoutName}:");
            text.AppendLine($"{DefinitionCount} definitions");
            text.AppendLine($"{Existing} existing");
            text.AppendLine($"{Created} created");
            text.AppendLine($"{Missing} missing");
            text.AppendLine($"{Orphaned} orphaned");
            text.AppendLine($"{Duplicates} duplicates");
            text.AppendLine($"{DefinitionIssues} definition issues");

            if (LayoutName == "WORLD")
            {
                text.AppendLine($"{MissingColliders} missing colliders");
                text.AppendLine($"{NonTriggerColliders} non-trigger colliders");
            }
            else
            {
                text.AppendLine($"{MissingContentRoots} missing ContentRoot references");
            }

            for (int i = 0; i < findings.Count; i++)
                text.AppendLine($"- {findings[i]}");

            return text.ToString().TrimEnd();
        }
    }

    public static class MapLayoutSyncUtility
    {
        private static readonly Vector2 DefaultWorldCellSize = new Vector2(6f, 6f);
        private static readonly Vector2 DefaultUiCellSize = new Vector2(80f, 40f);

        public static MapLayoutSyncReport SyncWorldLayout(
            MapAreaDefinition area,
            GameObject prefab)
        {
            return ProcessWorldLayout(area, prefab, true);
        }

        public static MapLayoutSyncReport ValidateWorldLayout(
            MapAreaDefinition area,
            GameObject prefab)
        {
            return ProcessWorldLayout(area, prefab, false);
        }

        public static MapLayoutSyncReport SyncUiLayout(
            MapAreaDefinition area,
            GameObject prefab)
        {
            return ProcessUiLayout(area, prefab, true);
        }

        public static MapLayoutSyncReport ValidateUiLayout(
            MapAreaDefinition area,
            GameObject prefab)
        {
            return ProcessUiLayout(area, prefab, false);
        }

        public static GameObject CreateWorldLayoutPrefab(
            MapAreaDefinition area,
            string path,
            out MapLayoutSyncReport report)
        {
            ValidateAreaAndNewPath(area, path);
            GameObject root = new GameObject(GetDefaultPrefabName(area, false));
            try
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            report = SyncWorldLayout(area, prefab);
            return prefab;
        }

        public static GameObject CreateUiLayoutPrefab(
            MapAreaDefinition area,
            string path,
            out MapLayoutSyncReport report)
        {
            ValidateAreaAndNewPath(area, path);
            GameObject root = new GameObject(
                GetDefaultPrefabName(area, true),
                typeof(RectTransform),
                typeof(MapAreaView));
            root.layer = 5;
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(800f, 600f);
            ConfigureNewAreaView(root.GetComponent<MapAreaView>(), area, root);

            try
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            report = SyncUiLayout(area, prefab);
            return prefab;
        }

        public static string GetDefaultPrefabName(MapAreaDefinition area, bool ui)
        {
            string source = area != null && !string.IsNullOrWhiteSpace(area.DisplayName)
                ? area.DisplayName
                : area != null ? area.AreaId : "MapArea";
            string token = ToPascalToken(source);
            return ui ? $"{token}MapLayout" : $"{token}ExplorationLayout";
        }

        private static MapLayoutSyncReport ProcessWorldLayout(
            MapAreaDefinition area,
            GameObject prefab,
            bool createMissing)
        {
            string path = ValidateAreaAndPrefab(area, prefab);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            MapLayoutSyncReport report;
            try
            {
                report = AnalyzeWorldLayout(area, root, createMissing);
                if (createMissing && report.Created > 0)
                    PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            LogReport(report, prefab);
            return report;
        }

        private static MapLayoutSyncReport ProcessUiLayout(
            MapAreaDefinition area,
            GameObject prefab,
            bool createMissing)
        {
            string path = ValidateAreaAndPrefab(area, prefab);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            MapLayoutSyncReport report;
            try
            {
                report = AnalyzeUiLayout(area, root, createMissing);
                if (createMissing && report.Created > 0)
                    PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            LogReport(report, prefab);
            return report;
        }

        private static MapLayoutSyncReport AnalyzeWorldLayout(
            MapAreaDefinition area,
            GameObject root,
            bool createMissing)
        {
            MapLayoutSyncReport report = CreateReport(area, "WORLD");
            List<MapCellDefinition> expected = GetUniqueDefinitions(area, report);
            HashSet<MapCellDefinition> expectedSet = new HashSet<MapCellDefinition>(expected);
            Dictionary<MapCellDefinition, List<MapExplorationCell>> byDefinition =
                new Dictionary<MapCellDefinition, List<MapExplorationCell>>();
            MapExplorationCell[] cells = root.GetComponentsInChildren<MapExplorationCell>(true);

            for (int i = 0; i < cells.Length; i++)
            {
                MapExplorationCell cell = cells[i];
                MapCellDefinition definition = cell.CellDefinition;
                BoxCollider2D collider = cell.GetComponent<BoxCollider2D>();
                if (collider == null)
                {
                    report.MissingColliders++;
                    report.AddFinding($"WORLD CELL without BoxCollider2D: {GetPath(cell.transform, root.transform)}");
                }
                else if (!collider.isTrigger)
                {
                    report.NonTriggerColliders++;
                    report.AddFinding($"WORLD CELL collider is not a trigger: {GetPath(cell.transform, root.transform)}");
                }

                if (definition == null || !expectedSet.Contains(definition))
                {
                    report.Orphaned++;
                    string id = definition != null ? definition.CellId : "<no definition>";
                    report.AddFinding(
                        $"ORPHANED WORLD CELL '{id}': {GetPath(cell.transform, root.transform)}");
                    continue;
                }

                AddByDefinition(byDefinition, definition, cell);
            }

            for (int i = 0; i < expected.Count; i++)
            {
                MapCellDefinition definition = expected[i];
                if (!byDefinition.TryGetValue(definition, out List<MapExplorationCell> matches))
                {
                    if (createMissing)
                    {
                        Transform room = FindOrCreateRoom(root.transform, definition, false);
                        CreateWorldCell(room, definition);
                        report.Created++;
                    }
                    else
                    {
                        report.Missing++;
                        report.AddFinding($"MISSING WORLD CELL '{definition.CellId}'");
                    }

                    continue;
                }

                report.Existing++;
                if (matches.Count <= 1)
                    continue;

                int duplicateCount = matches.Count - 1;
                report.Duplicates += duplicateCount;
                report.AddFinding(
                    $"DUPLICATE WORLD CELL '{definition.CellId}': {matches.Count} instances");
            }

            return report;
        }

        private static MapLayoutSyncReport AnalyzeUiLayout(
            MapAreaDefinition area,
            GameObject root,
            bool createMissing)
        {
            MapLayoutSyncReport report = CreateReport(area, "UI");
            List<MapCellDefinition> expected = GetUniqueDefinitions(area, report);
            HashSet<MapCellDefinition> expectedSet = new HashSet<MapCellDefinition>(expected);
            Dictionary<MapCellDefinition, List<MapCellView>> byDefinition =
                new Dictionary<MapCellDefinition, List<MapCellView>>();
            MapCellView[] views = root.GetComponentsInChildren<MapCellView>(true);

            for (int i = 0; i < views.Length; i++)
            {
                MapCellView view = views[i];
                MapCellDefinition definition = view.CellDefinition;
                SerializedProperty contentRoot =
                    new SerializedObject(view).FindProperty("contentRoot");
                if (contentRoot == null || contentRoot.objectReferenceValue == null)
                {
                    report.MissingContentRoots++;
                    report.AddFinding($"MAP VIEW without ContentRoot: {GetPath(view.transform, root.transform)}");
                }

                if (definition == null || !expectedSet.Contains(definition))
                {
                    report.Orphaned++;
                    string id = definition != null ? definition.CellId : "<no definition>";
                    report.AddFinding(
                        $"ORPHANED MAP VIEW '{id}': {GetPath(view.transform, root.transform)}");
                    continue;
                }

                AddByDefinition(byDefinition, definition, view);
            }

            for (int i = 0; i < expected.Count; i++)
            {
                MapCellDefinition definition = expected[i];
                if (!byDefinition.TryGetValue(definition, out List<MapCellView> matches))
                {
                    if (createMissing)
                    {
                        Transform room = FindOrCreateRoom(root.transform, definition, true);
                        CreateUiCell(room, definition);
                        report.Created++;
                    }
                    else
                    {
                        report.Missing++;
                        report.AddFinding($"MISSING MAP VIEW '{definition.CellId}'");
                    }

                    continue;
                }

                report.Existing++;
                if (matches.Count <= 1)
                    continue;

                int duplicateCount = matches.Count - 1;
                report.Duplicates += duplicateCount;
                report.AddFinding(
                    $"DUPLICATE MAP VIEW '{definition.CellId}': {matches.Count} instances");
            }

            return report;
        }

        private static List<MapCellDefinition> GetUniqueDefinitions(
            MapAreaDefinition area,
            MapLayoutSyncReport report)
        {
            IReadOnlyList<MapCellDefinition> cells = area.Cells;
            report.DefinitionCount = cells != null ? cells.Count : 0;
            List<MapCellDefinition> definitions = new List<MapCellDefinition>();
            HashSet<MapCellDefinition> unique = new HashSet<MapCellDefinition>();
            if (cells == null)
            {
                report.DefinitionIssues++;
                report.AddFinding("AREA DEFINITION has a null cell list");
                return definitions;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                MapCellDefinition definition = cells[i];
                if (definition == null)
                {
                    report.DefinitionIssues++;
                    report.AddFinding($"NULL DEFINITION at area index {i}");
                    continue;
                }

                if (!unique.Add(definition))
                {
                    report.DefinitionIssues++;
                    report.AddFinding($"DUPLICATE DEFINITION IN AREA '{definition.CellId}'");
                    continue;
                }

                definitions.Add(definition);
            }

            return definitions;
        }

        private static Transform FindOrCreateRoom(
            Transform root,
            MapCellDefinition definition,
            bool ui)
        {
            Component[] existingCells = ui
                ? (Component[])root.GetComponentsInChildren<MapCellView>(true)
                : root.GetComponentsInChildren<MapExplorationCell>(true);
            for (int i = 0; i < existingCells.Length; i++)
            {
                MapCellDefinition existingDefinition = ui
                    ? ((MapCellView)existingCells[i]).CellDefinition
                    : ((MapExplorationCell)existingCells[i]).CellDefinition;
                if (existingDefinition == null ||
                    !string.Equals(existingDefinition.RoomId, definition.RoomId, StringComparison.Ordinal))
                {
                    continue;
                }

                Transform topLevel = existingCells[i].transform;
                while (topLevel.parent != null && topLevel.parent != root)
                    topLevel = topLevel.parent;

                if (topLevel.parent == root && topLevel != existingCells[i].transform)
                    return topLevel;
            }

            string roomName = GetRoomObjectName(definition.RoomId);
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == roomName)
                    return child;
            }

            GameObject room = ui
                ? new GameObject(roomName, typeof(RectTransform))
                : new GameObject(roomName);
            room.layer = ui ? 5 : root.gameObject.layer;
            room.transform.SetParent(root, false);
            return room.transform;
        }

        private static void CreateWorldCell(Transform room, MapCellDefinition definition)
        {
            GameObject cellObject = new GameObject(GetCellObjectName(definition));
            cellObject.layer = room.gameObject.layer;
            cellObject.transform.SetParent(room, false);

            BoxCollider2D collider = cellObject.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = DefaultWorldCellSize;
            MapExplorationCell cell = cellObject.AddComponent<MapExplorationCell>();

            SerializedObject serializedCell = new SerializedObject(cell);
            serializedCell.FindProperty("cellDefinition").objectReferenceValue = definition;
            serializedCell.FindProperty("triggerCollider").objectReferenceValue = collider;
            serializedCell.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateUiCell(Transform room, MapCellDefinition definition)
        {
            GameObject cellObject = new GameObject(GetCellObjectName(definition), typeof(RectTransform));
            cellObject.layer = 5;
            cellObject.transform.SetParent(room, false);
            RectTransform cellRect = (RectTransform)cellObject.transform;
            cellRect.anchorMin = new Vector2(0.5f, 0.5f);
            cellRect.anchorMax = new Vector2(0.5f, 0.5f);
            cellRect.sizeDelta = DefaultUiCellSize;

            GameObject outline = CreateUiVisualChild(
                cellObject.transform,
                "OutlineRoot",
                new Color(0.16f, 0.48f, 0.62f, 0.3f));
            GameObject content = CreateUiVisualChild(
                cellObject.transform,
                "ContentRoot",
                new Color(0.95f, 0.62f, 0.16f, 0.95f));
            MapCellView view = cellObject.AddComponent<MapCellView>();

            SerializedObject serializedView = new SerializedObject(view);
            serializedView.FindProperty("cellDefinition").objectReferenceValue = definition;
            serializedView.FindProperty("outlineRoot").objectReferenceValue = outline;
            serializedView.FindProperty("contentRoot").objectReferenceValue = content;
            serializedView.FindProperty("showOutlineWhenMapOwned").boolValue = false;
            serializedView.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject CreateUiVisualChild(
            Transform parent,
            string name,
            Color color)
        {
            GameObject child = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            child.layer = 5;
            child.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)child.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = child.GetComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
            return child;
        }

        private static void ConfigureNewAreaView(
            MapAreaView view,
            MapAreaDefinition area,
            GameObject root)
        {
            SerializedObject serializedView = new SerializedObject(view);
            serializedView.FindProperty("areaDefinition").objectReferenceValue = area;
            serializedView.FindProperty("areaId").stringValue = area.AreaId;
            serializedView.FindProperty("displayName").stringValue = area.DisplayName;
            serializedView.FindProperty("areaRoot").objectReferenceValue = root;
            serializedView.ApplyModifiedPropertiesWithoutUndo();
        }

        private static MapLayoutSyncReport CreateReport(
            MapAreaDefinition area,
            string layoutName)
        {
            return new MapLayoutSyncReport
            {
                AreaName = !string.IsNullOrWhiteSpace(area.DisplayName)
                    ? area.DisplayName.ToUpperInvariant()
                    : area.AreaId.ToUpperInvariant(),
                LayoutName = layoutName
            };
        }

        private static void AddByDefinition<T>(
            Dictionary<MapCellDefinition, List<T>> byDefinition,
            MapCellDefinition definition,
            T component)
        {
            if (!byDefinition.TryGetValue(definition, out List<T> matches))
            {
                matches = new List<T>();
                byDefinition.Add(definition, matches);
            }

            matches.Add(component);
        }

        private static string ValidateAreaAndPrefab(
            MapAreaDefinition area,
            GameObject prefab)
        {
            if (area == null)
                throw new ArgumentNullException(nameof(area));
            if (prefab == null)
                throw new ArgumentNullException(nameof(prefab));

            string path = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(path) ||
                !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The layout must be a prefab asset.", nameof(prefab));
            }

            return path;
        }

        private static void ValidateAreaAndNewPath(MapAreaDefinition area, string path)
        {
            if (area == null)
                throw new ArgumentNullException(nameof(area));
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("A prefab path is required.", nameof(path));
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                throw new InvalidOperationException($"A prefab already exists at '{path}'.");
        }

        private static string GetRoomObjectName(string roomId)
        {
            string normalized = roomId != null ? roomId.Trim() : string.Empty;
            if (normalized.Length > 1 &&
                (normalized[0] == 'r' || normalized[0] == 'R'))
            {
                normalized = normalized.Substring(1);
            }

            return $"Room_{normalized.ToUpperInvariant()}";
        }

        private static string GetCellObjectName(MapCellDefinition definition)
        {
            string cellId = definition != null ? definition.CellId : string.Empty;
            int separator = cellId.LastIndexOf('.');
            string suffix = separator >= 0 ? cellId.Substring(separator + 1) : cellId;
            return string.IsNullOrEmpty(suffix) ? "Cell" : suffix.ToUpperInvariant();
        }

        private static string GetPath(Transform transform, Transform root)
        {
            string path = transform.name;
            Transform parent = transform.parent;
            while (parent != null && parent != root)
            {
                path = $"{parent.name}/{path}";
                parent = parent.parent;
            }

            return path;
        }

        private static string ToPascalToken(string value)
        {
            StringBuilder token = new StringBuilder();
            bool capitalize = true;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (!char.IsLetterOrDigit(character))
                {
                    capitalize = true;
                    continue;
                }

                token.Append(capitalize
                    ? char.ToUpperInvariant(character)
                    : char.ToLowerInvariant(character));
                capitalize = false;
            }

            return token.Length > 0 ? token.ToString() : "MapArea";
        }

        private static void LogReport(MapLayoutSyncReport report, UnityEngine.Object context)
        {
            if (report.HasProblems)
                Debug.LogWarning(report.ToString(), context);
            else
                Debug.Log(report.ToString(), context);
        }
    }
}

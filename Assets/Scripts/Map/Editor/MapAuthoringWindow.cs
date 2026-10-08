using System;
using System.Collections.Generic;
using Game.Map;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools.MapAuthoring
{
    public sealed class MapAuthoringWindow : EditorWindow
    {
        [SerializeField] private MapAreaDefinition areaDefinition;
        [SerializeField] private GameObject worldLayoutPrefab;
        [SerializeField] private GameObject uiLayoutPrefab;
        private Vector2 scroll;
        private string newAreaId = "factory";
        private string newRoomId = "r11";
        private string newSuffix = "a";
        private string newDisplayName = "Sala 11 - A";
        private bool newSecret;
        private string worldLayoutReport;
        private string uiLayoutReport;

        [MenuItem("SpaceWorld/Map/Map Authoring")]
        private static void Open()
        {
            GetWindow<MapAuthoringWindow>("Map Authoring");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Map Authoring", EditorStyles.boldLabel);
            areaDefinition = (MapAreaDefinition)EditorGUILayout.ObjectField(
                "Area Definition",
                areaDefinition,
                typeof(MapAreaDefinition),
                false);
            if (GUILayout.Button(areaDefinition == null ? "Find Area Asset" : "Choose Another Area"))
                ShowAreaAssetMenu();

            EditorGUILayout.Space();
            DrawAreaContents();
            EditorGUILayout.Space();
            DrawCreateCell();
            EditorGUILayout.Space();
            DrawLayoutAuthoring();
        }

        private void DrawAreaContents()
        {
            if (areaDefinition == null)
            {
                EditorGUILayout.HelpBox(
                    "Select a MapAreaDefinition to inspect its rooms and cells.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(
                string.IsNullOrEmpty(areaDefinition.DisplayName)
                    ? areaDefinition.AreaId
                    : areaDefinition.DisplayName,
                EditorStyles.boldLabel);

            IReadOnlyList<MapCellDefinition> cells = areaDefinition.Cells;
            if (cells == null)
            {
                EditorGUILayout.HelpBox("The selected area has a null cell list.", MessageType.Error);
                return;
            }

            SortedDictionary<string, List<MapCellDefinition>> rooms =
                new SortedDictionary<string, List<MapCellDefinition>>(StringComparer.Ordinal);
            int nullCount = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                MapCellDefinition cell = cells[i];
                if (cell == null)
                {
                    nullCount++;
                    continue;
                }

                string roomId = string.IsNullOrWhiteSpace(cell.RoomId) ? "<no room>" : cell.RoomId;
                if (!rooms.TryGetValue(roomId, out List<MapCellDefinition> roomCells))
                {
                    roomCells = new List<MapCellDefinition>();
                    rooms.Add(roomId, roomCells);
                }

                roomCells.Add(cell);
            }

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(180f));
            foreach (KeyValuePair<string, List<MapCellDefinition>> room in rooms)
            {
                room.Value.Sort(CompareCells);
                EditorGUILayout.LabelField(RoomLabel(room.Key), EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                for (int i = 0; i < room.Value.Count; i++)
                    DrawCell(room.Value[i]);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(2f);
            }
            EditorGUILayout.EndScrollView();

            if (nullCount > 0)
            {
                EditorGUILayout.HelpBox(
                    $"The selected area contains {nullCount} null reference(s).",
                    MessageType.Error);
            }
        }

        private static void DrawCell(MapCellDefinition cell)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        $"{CellSuffix(cell.CellId)}  {cell.DisplayName}",
                        EditorStyles.boldLabel);
                    if (GUILayout.Button("Ping", GUILayout.Width(45f)))
                        EditorGUIUtility.PingObject(cell);
                    if (GUILayout.Button("Select", GUILayout.Width(50f)))
                        Selection.activeObject = cell;
                }

                EditorGUILayout.SelectableLabel(cell.CellId, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.LabelField("Secret", cell.Secret ? "Yes" : "No");
            }
        }

        private void DrawCreateCell()
        {
            EditorGUILayout.LabelField("Create New Cell Definition", EditorStyles.boldLabel);
            newAreaId = EditorGUILayout.TextField("Area", newAreaId);
            newRoomId = EditorGUILayout.TextField("Room", newRoomId);
            newSuffix = EditorGUILayout.TextField("Suffix", newSuffix);
            newDisplayName = EditorGUILayout.TextField("Display Name", newDisplayName);
            newSecret = EditorGUILayout.Toggle("Secret", newSecret);

            string previewId = BuildCellId(newAreaId, newRoomId, newSuffix);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("ID to create", previewId);

            bool canCreate = !string.IsNullOrEmpty(previewId);
            using (new EditorGUI.DisabledScope(!canCreate))
            {
                if (GUILayout.Button("Create New Cell Definition"))
                    CreateCell(previewId);
            }
        }

        private void DrawLayoutAuthoring()
        {
            EditorGUILayout.LabelField("Layout Authoring", EditorStyles.boldLabel);
            worldLayoutPrefab = (GameObject)EditorGUILayout.ObjectField(
                "World Layout Prefab",
                worldLayoutPrefab,
                typeof(GameObject),
                false);
            uiLayoutPrefab = (GameObject)EditorGUILayout.ObjectField(
                "UI Layout Prefab",
                uiLayoutPrefab,
                typeof(GameObject),
                false);

            if (areaDefinition == null)
            {
                EditorGUILayout.HelpBox(
                    "Select a MapAreaDefinition before creating, syncing, or validating layouts.",
                    MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(areaDefinition == null))
            {
                if (GUILayout.Button("Create/Sync World Layout"))
                    CreateOrSyncWorldLayout();
                if (GUILayout.Button("Create/Sync UI Layout"))
                    CreateOrSyncUiLayout();
                if (GUILayout.Button("Validate Layout"))
                    ValidateLayouts();
            }

            if (!string.IsNullOrEmpty(worldLayoutReport))
                EditorGUILayout.HelpBox(worldLayoutReport, MessageType.None);
            if (!string.IsNullOrEmpty(uiLayoutReport))
                EditorGUILayout.HelpBox(uiLayoutReport, MessageType.None);
        }

        private void CreateOrSyncWorldLayout()
        {
            try
            {
                if (worldLayoutPrefab == null)
                {
                    string path = ChooseLayoutPath(false);
                    if (string.IsNullOrEmpty(path))
                        return;

                    GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (existing != null)
                    {
                        worldLayoutPrefab = existing;
                        worldLayoutReport =
                            MapLayoutSyncUtility.SyncWorldLayout(areaDefinition, existing).ToString();
                    }
                    else
                    {
                        worldLayoutPrefab = MapLayoutSyncUtility.CreateWorldLayoutPrefab(
                            areaDefinition,
                            path,
                            out MapLayoutSyncReport report);
                        worldLayoutReport = report.ToString();
                    }
                }
                else
                {
                    worldLayoutReport = MapLayoutSyncUtility.SyncWorldLayout(
                        areaDefinition,
                        worldLayoutPrefab).ToString();
                }
            }
            catch (Exception exception)
            {
                worldLayoutReport = $"WORLD layout sync failed: {exception.Message}";
                Debug.LogException(exception);
            }
        }

        private void CreateOrSyncUiLayout()
        {
            try
            {
                if (uiLayoutPrefab == null)
                {
                    string path = ChooseLayoutPath(true);
                    if (string.IsNullOrEmpty(path))
                        return;

                    GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (existing != null)
                    {
                        uiLayoutPrefab = existing;
                        uiLayoutReport =
                            MapLayoutSyncUtility.SyncUiLayout(areaDefinition, existing).ToString();
                    }
                    else
                    {
                        uiLayoutPrefab = MapLayoutSyncUtility.CreateUiLayoutPrefab(
                            areaDefinition,
                            path,
                            out MapLayoutSyncReport report);
                        uiLayoutReport = report.ToString();
                    }
                }
                else
                {
                    uiLayoutReport = MapLayoutSyncUtility.SyncUiLayout(
                        areaDefinition,
                        uiLayoutPrefab).ToString();
                }
            }
            catch (Exception exception)
            {
                uiLayoutReport = $"UI layout sync failed: {exception.Message}";
                Debug.LogException(exception);
            }
        }

        private void ValidateLayouts()
        {
            try
            {
                worldLayoutReport = worldLayoutPrefab != null
                    ? MapLayoutSyncUtility.ValidateWorldLayout(
                        areaDefinition,
                        worldLayoutPrefab).ToString()
                    : "WORLD layout not selected.";
                uiLayoutReport = uiLayoutPrefab != null
                    ? MapLayoutSyncUtility.ValidateUiLayout(
                        areaDefinition,
                        uiLayoutPrefab).ToString()
                    : "UI layout not selected.";
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private string ChooseLayoutPath(bool ui)
        {
            string defaultName = MapLayoutSyncUtility.GetDefaultPrefabName(areaDefinition, ui);
            string defaultFolder = ui ? "Assets/Prefabs/UI/Map" : "Assets/Prefabs/Map";
            return EditorUtility.SaveFilePanelInProject(
                ui ? "Create or Select Map UI Layout" : "Create or Select World Layout",
                defaultName,
                "prefab",
                "Choose a new path, or select an existing layout prefab to sync it.",
                defaultFolder);
        }

        private void ShowAreaAssetMenu()
        {
            string[] guids = AssetDatabase.FindAssets("t:MapAreaDefinition");
            if (guids.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "No Map Area Definitions",
                    "No MapAreaDefinition assets were found in the project.",
                    "OK");
                return;
            }

            if (guids.Length == 1)
            {
                areaDefinition = AssetDatabase.LoadAssetAtPath<MapAreaDefinition>(
                    AssetDatabase.GUIDToAssetPath(guids[0]));
                return;
            }

            GenericMenu menu = new GenericMenu();
            for (int i = 0; i < guids.Length; i++)
            {
                MapAreaDefinition candidate = AssetDatabase.LoadAssetAtPath<MapAreaDefinition>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (candidate == null)
                    continue;

                string label = !string.IsNullOrWhiteSpace(candidate.DisplayName)
                    ? $"{candidate.DisplayName} ({candidate.AreaId})"
                    : candidate.AreaId;
                menu.AddItem(
                    new GUIContent(label),
                    candidate == areaDefinition,
                    () =>
                    {
                        areaDefinition = candidate;
                        Repaint();
                    });
            }

            menu.ShowAsContext();
        }

        private void CreateCell(string cellId)
        {
            MapCellDefinition existing = FindCell(cellId);
            if (existing != null)
            {
                EditorUtility.DisplayDialog(
                    "Cell ID already exists",
                    $"'{cellId}' is already used by {AssetDatabase.GetAssetPath(existing)}.",
                    "OK");
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                return;
            }

            string defaultName = cellId.Replace('.', '_');
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Map Cell Definition",
                defaultName,
                "asset",
                $"Create the visible Cell ID '{cellId}'.");
            if (string.IsNullOrEmpty(path))
                return;

            MapCellDefinition definition = CreateInstance<MapCellDefinition>();
            SerializedObject serializedDefinition = new SerializedObject(definition);
            serializedDefinition.FindProperty("cellId").stringValue = cellId;
            serializedDefinition.FindProperty("areaId").stringValue = NormalizePart(newAreaId);
            serializedDefinition.FindProperty("roomId").stringValue = NormalizePart(newRoomId);
            serializedDefinition.FindProperty("displayName").stringValue =
                newDisplayName != null ? newDisplayName.Trim() : string.Empty;
            serializedDefinition.FindProperty("secret").boolValue = newSecret;
            serializedDefinition.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(definition, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = definition;
            EditorGUIUtility.PingObject(definition);
            Debug.Log($"[MAP AUTHORING] Created '{cellId}' at '{path}'.", definition);
        }

        private static MapCellDefinition FindCell(string cellId)
        {
            string[] guids = AssetDatabase.FindAssets("t:MapCellDefinition");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                MapCellDefinition cell = AssetDatabase.LoadAssetAtPath<MapCellDefinition>(path);
                if (cell != null && string.Equals(cell.CellId, cellId, StringComparison.Ordinal))
                    return cell;
            }

            return null;
        }

        private static string BuildCellId(string areaId, string roomId, string suffix)
        {
            string area = NormalizePart(areaId);
            string room = NormalizePart(roomId);
            string cellSuffix = NormalizePart(suffix);
            if (string.IsNullOrEmpty(area) || string.IsNullOrEmpty(room) || string.IsNullOrEmpty(cellSuffix))
                return string.Empty;

            return $"{area}.{room}.{cellSuffix}";
        }

        private static string NormalizePart(string value)
        {
            return value != null ? value.Trim().ToLowerInvariant() : string.Empty;
        }

        private static int CompareCells(MapCellDefinition left, MapCellDefinition right)
        {
            return string.Compare(left.CellId, right.CellId, StringComparison.Ordinal);
        }

        private static string CellSuffix(string cellId)
        {
            if (string.IsNullOrEmpty(cellId))
                return "?";

            int separator = cellId.LastIndexOf('.');
            string suffix = separator >= 0 ? cellId.Substring(separator + 1) : cellId;
            return suffix.ToUpperInvariant();
        }

        private static string RoomLabel(string roomId)
        {
            if (roomId != null && roomId.Length > 1 && roomId[0] == 'r')
                return $"Sala {roomId.Substring(1)}";

            return roomId;
        }
    }
}

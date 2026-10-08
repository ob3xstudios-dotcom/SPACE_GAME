using Game.Map;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools.MapAuthoring
{
    [CustomEditor(typeof(MapExplorationCell))]
    public sealed class MapExplorationCellEditor : Editor
    {
        private bool showLegacyIdentity;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawScriptField((MonoBehaviour)target);

            SerializedProperty definition = serializedObject.FindProperty("cellDefinition");
            SerializedProperty legacyCellId = serializedObject.FindProperty("cellId");
            SerializedProperty legacyAreaId = serializedObject.FindProperty("areaId");
            SerializedProperty legacyRoomId = serializedObject.FindProperty("subzoneId");

            EditorGUILayout.PropertyField(definition, new GUIContent("Cell Definition"));
            DrawResolvedIdentity(
                definition.objectReferenceValue as MapCellDefinition,
                legacyCellId.stringValue,
                legacyAreaId.stringValue,
                legacyRoomId.stringValue);
            DrawLegacyFields(definition, legacyCellId, legacyAreaId, legacyRoomId);

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("triggerCollider"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("logDiagnostics"));
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawLegacyFields(
            SerializedProperty definition,
            SerializedProperty cellId,
            SerializedProperty areaId,
            SerializedProperty roomId)
        {
            if (definition.objectReferenceValue == null)
            {
                bool hasLegacyId = !string.IsNullOrWhiteSpace(cellId.stringValue);
                EditorGUILayout.HelpBox(
                    hasLegacyId
                        ? "No MapCellDefinition assigned. The legacy string identity remains active."
                        : "No MapCellDefinition assigned. Assign one before using this instance in a level.",
                    hasLegacyId ? MessageType.Info : MessageType.Warning);
                EditorGUILayout.PropertyField(cellId, new GUIContent("Cell ID"));
                EditorGUILayout.PropertyField(areaId, new GUIContent("Area ID"));
                EditorGUILayout.PropertyField(roomId, new GUIContent("Subzone / Room ID"));
                return;
            }

            showLegacyIdentity = EditorGUILayout.Foldout(
                showLegacyIdentity,
                "Legacy fallback identity",
                true);
            if (!showLegacyIdentity)
                return;

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(cellId, new GUIContent("Cell ID"));
            EditorGUILayout.PropertyField(areaId, new GUIContent("Area ID"));
            EditorGUILayout.PropertyField(roomId, new GUIContent("Subzone / Room ID"));
            EditorGUI.indentLevel--;
        }

        private static void DrawResolvedIdentity(
            MapCellDefinition definition,
            string legacyCellId,
            string legacyAreaId,
            string legacyRoomId)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(
                    "Resolved ID",
                    MapCellIdentity.ResolveCellId(definition, legacyCellId));
                EditorGUILayout.TextField(
                    "Area",
                    MapCellIdentity.ResolveAreaId(definition, legacyAreaId));
                EditorGUILayout.TextField(
                    "Room",
                    MapCellIdentity.ResolveRoomId(definition, legacyRoomId));
            }
        }

        private static void DrawScriptField(MonoBehaviour component)
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour(component), typeof(MonoScript), false);
        }
    }

    [CustomEditor(typeof(MapCellView))]
    public sealed class MapCellViewEditor : Editor
    {
        private bool showLegacyIdentity;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawScriptField((MonoBehaviour)target);

            SerializedProperty definition = serializedObject.FindProperty("cellDefinition");
            SerializedProperty legacyCellId = serializedObject.FindProperty("cellId");
            SerializedProperty legacyRoomId = serializedObject.FindProperty("subzoneId");

            EditorGUILayout.PropertyField(definition, new GUIContent("Cell Definition"));
            MapCellDefinition cellDefinition = definition.objectReferenceValue as MapCellDefinition;
            DrawResolvedIdentity(cellDefinition, legacyCellId.stringValue, legacyRoomId.stringValue);
            DrawLegacyFields(definition, legacyCellId, legacyRoomId);

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("outlineRoot"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("contentRoot"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("showOutlineWhenMapOwned"));

            if (cellDefinition != null && cellDefinition.Secret)
            {
                SerializedProperty secretOverride =
                    serializedObject.FindProperty("overrideSecretOutlineDefault");
                EditorGUILayout.PropertyField(
                    secretOverride,
                    new GUIContent("Override Secret Outline Default"));
                EditorGUILayout.HelpBox(
                    secretOverride.boolValue
                        ? "This secret cell uses the manual Show Outline setting above."
                        : "This secret cell hides its unconfirmed outline by default. Enable the override to use the manual setting above.",
                    MessageType.Info);
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("hideOutlineUntilConfirmed"));

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawLegacyFields(
            SerializedProperty definition,
            SerializedProperty cellId,
            SerializedProperty roomId)
        {
            if (definition.objectReferenceValue == null)
            {
                bool hasLegacyId = !string.IsNullOrWhiteSpace(cellId.stringValue);
                EditorGUILayout.HelpBox(
                    hasLegacyId
                        ? "No MapCellDefinition assigned. The legacy string identity remains active."
                        : "No MapCellDefinition assigned. Assign one before using this instance in a level.",
                    hasLegacyId ? MessageType.Info : MessageType.Warning);
                EditorGUILayout.PropertyField(cellId, new GUIContent("Cell ID"));
                EditorGUILayout.PropertyField(roomId, new GUIContent("Subzone / Room ID"));
                return;
            }

            showLegacyIdentity = EditorGUILayout.Foldout(
                showLegacyIdentity,
                "Legacy fallback identity",
                true);
            if (!showLegacyIdentity)
                return;

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(cellId, new GUIContent("Cell ID"));
            EditorGUILayout.PropertyField(roomId, new GUIContent("Subzone / Room ID"));
            EditorGUI.indentLevel--;
        }

        private static void DrawResolvedIdentity(
            MapCellDefinition definition,
            string legacyCellId,
            string legacyRoomId)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(
                    "Resolved ID",
                    MapCellIdentity.ResolveCellId(definition, legacyCellId));
                EditorGUILayout.TextField(
                    "Area",
                    MapCellIdentity.ResolveAreaId(definition, string.Empty));
                EditorGUILayout.TextField(
                    "Room",
                    MapCellIdentity.ResolveRoomId(definition, legacyRoomId));
            }
        }

        private static void DrawScriptField(MonoBehaviour component)
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour(component), typeof(MonoScript), false);
        }
    }
}

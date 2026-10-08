using Game.Map;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools.MapAuthoring
{
    [CustomEditor(typeof(MapCellDefinition))]
    public sealed class MapCellDefinitionEditor : Editor
    {
        private SerializedProperty cellId;
        private SerializedProperty areaId;
        private SerializedProperty roomId;
        private SerializedProperty displayName;
        private SerializedProperty secret;
        private SerializedProperty notes;

        private void OnEnable()
        {
            cellId = serializedObject.FindProperty("cellId");
            areaId = serializedObject.FindProperty("areaId");
            roomId = serializedObject.FindProperty("roomId");
            displayName = serializedObject.FindProperty("displayName");
            secret = serializedObject.FindProperty("secret");
            notes = serializedObject.FindProperty("notes");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.HelpBox(
                "Cell ID is persistent save data. Do not rename after shipping or using persistent saves.",
                MessageType.Warning);

            EditorGUILayout.PropertyField(cellId, new GUIContent("Cell ID"));
            EditorGUILayout.PropertyField(areaId, new GUIContent("Area"));
            EditorGUILayout.PropertyField(roomId, new GUIContent("Room"));
            EditorGUILayout.PropertyField(displayName, new GUIContent("Display Name"));
            EditorGUILayout.PropertyField(secret, new GUIContent("Secret"));
            EditorGUILayout.PropertyField(notes, new GUIContent("Notes"));

            serializedObject.ApplyModifiedProperties();

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(cellId.stringValue)))
            {
                if (GUILayout.Button("Copy Cell ID"))
                    EditorGUIUtility.systemCopyBuffer = cellId.stringValue.Trim();
            }

            EditorGUILayout.HelpBox(
                "If a persistent cell is split or merged, use unambiguous IDs. Any save migration must be explicit.",
                MessageType.Info);
        }
    }
}

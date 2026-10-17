#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tutorials.Editor
{
    public static class RadioTutorialPilotSetup
    {
        private const string DefinitionPath =
            "Assets/Tutorials/TUT_MUELLE_JUMP_001.asset";
        private const string PrefabPath =
            "Assets/Prefabs/Tutorials/PF_RadioTutorialMarker.prefab";
        private const string ScenePath =
            "Assets/Scenes/Sandbox/MUELLE.unity";
        private const string SceneMarkerName =
            "Tutorial_MUELLE_Jump_001";

        public static void CreateOrUpdatePilotAssets()
        {
            EnsureFolder("Assets", "Tutorials");
            EnsureFolder("Assets/Prefabs", "Tutorials");

            RadioTutorialDefinition definition = CreateOrUpdateDefinition();
            GameObject prefab = CreateOrUpdateMarkerPrefab(definition);
            AddOrUpdateMuelleMarker(prefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[RADIO TUTORIAL] MUELLE jump pilot assets are ready.");
        }

        private static RadioTutorialDefinition CreateOrUpdateDefinition()
        {
            RadioTutorialDefinition definition =
                AssetDatabase.LoadAssetAtPath<RadioTutorialDefinition>(DefinitionPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<RadioTutorialDefinition>();
                AssetDatabase.CreateAsset(definition, DefinitionPath);
            }

            SerializedObject serialized = new SerializedObject(definition);
            serialized.FindProperty("tutorialId").stringValue = "TUT_MUELLE_JUMP_001";
            serialized.FindProperty("speaker").stringValue = "Comunicaciones";
            serialized.FindProperty("openingLine").stringValue =
                "Antes de seguir, comprueba el salto.";
            serialized.FindProperty("instruction").stringValue =
                "Pulsa Espacio o el botón sur del mando para saltar.";
            serialized.FindProperty("successLine").stringValue =
                "Salto confirmado. Continúa.";

            SerializedProperty actions = serialized.FindProperty("requiredActions");
            actions.arraySize = 1;
            actions.GetArrayElementAtIndex(0).intValue =
                (int)RadioTutorialAction.Jump;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static GameObject CreateOrUpdateMarkerPrefab(
            RadioTutorialDefinition definition)
        {
            GameObject root = new GameObject("PF_RadioTutorialMarker");
            try
            {
                BoxCollider2D box = root.AddComponent<BoxCollider2D>();
                box.isTrigger = true;
                box.size = new Vector2(3f, 3f);

                RadioTutorialMarker2D marker =
                    root.AddComponent<RadioTutorialMarker2D>();
                SerializedObject serialized = new SerializedObject(marker);
                serialized.FindProperty("definition").objectReferenceValue = definition;
                serialized.FindProperty("showGizmo").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void AddOrUpdateMuelleMarker(GameObject prefab)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject marker = GameObject.Find(SceneMarkerName);
            if (marker == null)
            {
                marker = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                if (marker == null)
                    throw new System.InvalidOperationException(
                        "Could not instantiate the radio tutorial marker in MUELLE.");
            }

            marker.name = SceneMarkerName;
            marker.transform.SetParent(null);
            marker.transform.position = new Vector3(-232.53f, 5.3f, 0f);
            marker.transform.rotation = Quaternion.identity;
            marker.transform.localScale = Vector3.one;

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new System.InvalidOperationException("Could not save MUELLE.");
        }

        private static void EnsureFolder(string parent, string leaf)
        {
            string path = parent + "/" + leaf;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif

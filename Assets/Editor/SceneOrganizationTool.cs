using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.EditorTools
{
    public static class SceneOrganizationTool
    {
        private static readonly string[] RootPaths =
        {
            "Traversal/LedgeGrabTriggers",
            "Traversal/SwingPoles",
            "Interactables/Barrels",
            "Interactables/Wardrobes",
            "Enemies",
            "Background"
        };

        [MenuItem("Tools/SpaceGame/Scene/Create Organization Parents")]
        public static void CreateOrganizationParents()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                Debug.LogWarning("[SceneOrganization] No active scene found.");
                return;
            }

            foreach (string path in RootPaths)
                EnsurePath(path);

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[SceneOrganization] Ensured {RootPaths.Length} organization paths in scene '{scene.name}'.");
        }

        private static Transform EnsurePath(string path)
        {
            string[] parts = path.Split('/');
            Transform parent = null;

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                Transform child = FindChild(parent, part);
                if (child == null)
                {
                    GameObject go = new GameObject(part);
                    Undo.RegisterCreatedObjectUndo(go, "Create Scene Organization Parent");
                    child = go.transform;
                    child.SetParent(parent);
                    child.localPosition = Vector3.zero;
                    child.localRotation = Quaternion.identity;
                    child.localScale = Vector3.one;
                }

                parent = child;
            }

            return parent;
        }

        private static Transform FindChild(Transform parent, string name)
        {
            if (parent == null)
            {
                GameObject root = GameObject.Find(name);
                return root != null && root.transform.parent == null ? root.transform : null;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == name)
                    return child;
            }

            return null;
        }
    }
}

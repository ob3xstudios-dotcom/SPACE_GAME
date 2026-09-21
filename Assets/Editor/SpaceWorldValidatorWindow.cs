using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Game.EditorTools
{
    public sealed class SpaceWorldValidatorWindow : EditorWindow
    {
        private enum Severity { Pass, Warning, Fail }

        private struct Finding
        {
            public Severity Severity;
            public string Rule;
            public string Path;
            public string Message;
        }

        private readonly List<Finding> findings = new List<Finding>();
        private Vector2 scroll;
        private bool hasRun;

        [MenuItem("SpaceWorld/Tools/Validator")]
        private static void Open()
        {
            GetWindow<SpaceWorldValidatorWindow>("SpaceWorld Validator");
        }

        // Read-only entry point for Unity's -executeMethod scene smoke checks.
        public static void BatchValidateOpenScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            bool dirtyBefore = scene.IsValid() && scene.isDirty;
            SpaceWorldValidatorWindow window = CreateInstance<SpaceWorldValidatorWindow>();
            try
            {
                window.ValidateOpenScene();
                int pass = 0, warning = 0, fail = 0;
                foreach (Finding finding in window.findings)
                {
                    if (finding.Severity == Severity.Pass) pass++;
                    else if (finding.Severity == Severity.Warning) warning++;
                    else fail++;
                    if (finding.Severity != Severity.Pass)
                        Debug.Log("SpaceWorld Validator: " + finding.Severity + " " + finding.Rule +
                            " " + finding.Path + " " + finding.Message);
                }
                Debug.Log("VALIDATOR_SCENE=" + scene.name + " VALIDATOR_PASS=" + pass +
                    " VALIDATOR_WARNING=" + warning + " VALIDATOR_FAIL=" + fail +
                    " DIRTY_BEFORE=" + dirtyBefore + " DIRTY_AFTER=" + (scene.IsValid() && scene.isDirty));
            }
            finally { DestroyImmediate(window); }
        }

        public static void BatchValidateProjectScenes()
        {
            foreach (string path in new[]
            {
                "Assets/Scenes/Sandbox/MUELLE.unity",
                "Assets/Scenes/Sandbox/ADUANAS.unity"
            })
            {
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                BatchValidateOpenScene();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("SpaceWorld Validator", EditorStyles.boldLabel);
            if (GUILayout.Button("Validate Open Scene"))
                ValidateOpenScene();

            if (!hasRun) return;

            int pass = 0, warning = 0, fail = 0;
            foreach (Finding finding in findings)
            {
                if (finding.Severity == Severity.Pass) pass++;
                else if (finding.Severity == Severity.Warning) warning++;
                else fail++;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("PASS=" + pass + "  WARNING=" + warning + "  FAIL=" + fail);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (Finding finding in findings)
            {
                string location = string.IsNullOrEmpty(finding.Path) ? "" : " | " + finding.Path;
                EditorGUILayout.LabelField(finding.Severity.ToString().ToUpperInvariant() + " | " + finding.Rule + location, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(finding.Message, EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space(3);
            }
            EditorGUILayout.EndScrollView();
        }

        private void Add(Severity severity, string rule, string path, string message)
        {
            findings.Add(new Finding { Severity = severity, Rule = rule, Path = path, Message = message });
        }

        private void ValidateOpenScene()
        {
            findings.Clear();
            hasRun = true;
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                Add(Severity.Fail, "SCENE", "", "There is no valid active scene.");
                return;
            }

            bool dirtyBefore = scene.isDirty;
            Add(Severity.Pass, "SCENE", scene.name, "Active scene is valid.");
            Add(scene.isLoaded ? Severity.Pass : Severity.Fail, "SCENE_LOADED", scene.name,
                scene.isLoaded ? "Scene is loaded." : "Scene is not loaded.");
            if (!scene.isLoaded) return;

            Add(string.IsNullOrEmpty(scene.path) ? Severity.Fail : Severity.Pass, "SCENE_SAVED", scene.name,
                string.IsNullOrEmpty(scene.path) ? "Scene has no saved path." : scene.path);
            if (dirtyBefore)
                Add(Severity.Warning, "SCENE_DIRTY", scene.name, "Scene already had unsaved changes before validation.");

            List<GameObject> objects = new List<GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
                Collect(root.transform, objects);

            ValidatePlayers(objects);
            ValidateMissingScripts(objects);
            ValidateCameras(scene, objects);
            ValidateTilemapPhysics(objects);
            ValidateSortingLayers(objects);
            if (scene.name == "MUELLE")
                ValidateMuelle(objects);

            if (scene.isDirty != dirtyBefore)
                Add(Severity.Fail, "READ_ONLY", scene.name, "Scene dirty state changed during validation.");
        }

        private static void Collect(Transform transform, List<GameObject> objects)
        {
            objects.Add(transform.gameObject);
            for (int i = 0; i < transform.childCount; i++)
                Collect(transform.GetChild(i), objects);
        }

        private static string PathOf(GameObject go)
        {
            string path = go.name;
            for (Transform parent = go.transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }

        private static bool HasComponentNamed(GameObject go, string name)
        {
            foreach (Component component in go.GetComponents<Component>())
                if (component != null && component.GetType().Name == name)
                    return true;
            return false;
        }

        private void ValidatePlayers(List<GameObject> objects)
        {
            List<GameObject> players = new List<GameObject>();
            foreach (GameObject go in objects)
                if (go.activeInHierarchy && go.CompareTag("Player")) players.Add(go);

            if (players.Count != 1)
            {
                Add(Severity.Fail, "PLAYER_COUNT", "", "Expected one active Player; found " + players.Count + ".");
                return;
            }

            GameObject player = players[0];
            string path = PathOf(player);
            Add(Severity.Pass, "PLAYER_COUNT", path, "Exactly one active tagged Player.");
            foreach (string name in new[] { "PlayerController", "PlayerHealth", "InputReader" })
            {
                bool present = HasComponentNamed(player, name);
                Add(present ? Severity.Pass : Severity.Fail, "PLAYER_" + name.ToUpperInvariant(), path,
                    present ? name + " is present." : name + " is missing.");
            }

            Rigidbody2D body = player.GetComponent<Rigidbody2D>();
            Add(body != null ? Severity.Pass : Severity.Fail, "PLAYER_RIGIDBODY", path,
                body != null ? "Rigidbody2D is present." : "Rigidbody2D is missing.");
            if (body != null)
                Add(body.bodyType == RigidbodyType2D.Dynamic ? Severity.Pass : Severity.Fail,
                    "PLAYER_BODY_TYPE", path, "Rigidbody2D body type: " + body.bodyType + ".");

            CapsuleCollider2D collider = player.GetComponent<CapsuleCollider2D>();
            Add(collider != null ? Severity.Pass : Severity.Fail, "PLAYER_CAPSULE", path,
                collider != null ? "CapsuleCollider2D is present." : "CapsuleCollider2D is missing.");
            if (collider != null)
            {
                Add(collider.enabled ? Severity.Pass : Severity.Fail, "PLAYER_COLLIDER_ENABLED", path,
                    collider.enabled ? "Collider is enabled." : "Collider is disabled.");
                Add(!collider.isTrigger ? Severity.Pass : Severity.Fail, "PLAYER_COLLIDER_TRIGGER", path,
                    collider.isTrigger ? "Main collider is a trigger." : "Main collider is not a trigger.");
            }
        }

        private void ValidateMissingScripts(List<GameObject> objects)
        {
            int missing = 0;
            foreach (GameObject go in objects)
            {
                int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                if (count == 0) continue;
                missing += count;
                Add(go.activeInHierarchy ? Severity.Fail : Severity.Warning, "MISSING_SCRIPTS", PathOf(go),
                    count + " missing MonoBehaviour script(s) on " +
                    (go.activeInHierarchy ? "an active object." : "an inactive object."));
            }
            if (missing == 0)
                Add(Severity.Pass, "MISSING_SCRIPTS", "", "No missing MonoBehaviour scripts found.");
        }

        private static bool IsVirtualCamera(Component component)
        {
            if (component == null) return false;
            for (System.Type type = component.GetType(); type != null; type = type.BaseType)
                if (type.FullName == "Cinemachine.CinemachineVirtualCameraBase") return true;
            return false;
        }

        private void ValidateCameras(Scene scene, List<GameObject> objects)
        {
            int mainCount = 0;
            List<Component> virtualCameras = new List<Component>();
            foreach (GameObject go in objects)
            {
                if (!go.activeInHierarchy) continue;
                Camera camera = go.GetComponent<Camera>();
                if (camera != null && camera.enabled && go.CompareTag("MainCamera"))
                {
                    mainCount++;
                    bool hasBrain = HasComponentNamed(go, "CinemachineBrain");
                    Add(hasBrain ? Severity.Pass : Severity.Fail, "CINEMACHINE_BRAIN", PathOf(go),
                        hasBrain ? "CinemachineBrain is present." : "CinemachineBrain is missing.");
                }
                foreach (Component component in go.GetComponents<Component>())
                    if (IsVirtualCamera(component) && ((Behaviour)component).enabled)
                        virtualCameras.Add(component);
            }

            Add(mainCount > 0 ? Severity.Pass : Severity.Fail, "MAIN_CAMERA", "",
                mainCount > 0 ? "Active tagged MainCamera found." : "No active tagged MainCamera found.");
            Add(virtualCameras.Count > 0 ? Severity.Pass : Severity.Fail, "VIRTUAL_CAMERA", "",
                virtualCameras.Count + " active Cinemachine virtual camera(s) found.");

            if (scene.name != "MUELLE" && scene.name != "ADUANAS") return;
            if (virtualCameras.Count > 1)
            {
                Add(Severity.Warning, "VIRTUAL_CAMERA_FOLLOW", "",
                    "Multiple virtual cameras are active; the primary Follow target is ambiguous.");
                return;
            }
            if (virtualCameras.Count != 1) return;

            Component virtualCamera = virtualCameras[0];
            SerializedProperty follow = new SerializedObject(virtualCamera).FindProperty("m_Follow");
            if (follow == null)
                Add(Severity.Warning, "VIRTUAL_CAMERA_FOLLOW", PathOf(virtualCamera.gameObject),
                    "Could not inspect the Follow property safely.");
            else
                Add(follow.objectReferenceValue != null ? Severity.Pass : Severity.Fail,
                    "VIRTUAL_CAMERA_FOLLOW", PathOf(virtualCamera.gameObject),
                    follow.objectReferenceValue != null ? "Follow target is assigned." : "Follow target is missing.");
        }

        private void ValidateTilemapPhysics(List<GameObject> objects)
        {
            int checkedCount = 0;
            int ground = LayerMask.NameToLayer("Ground");
            int wall = LayerMask.NameToLayer("Wall");
            foreach (GameObject go in objects)
            {
                if (!go.activeInHierarchy || (go.layer != ground && go.layer != wall)) continue;
                TilemapCollider2D tile = go.GetComponent<TilemapCollider2D>();
                if (tile == null || !tile.enabled || !tile.usedByComposite) continue;
                checkedCount++;
                bool hasComposite = go.GetComponent<CompositeCollider2D>() != null;
                Rigidbody2D body = tile.attachedRigidbody;
                bool valid = hasComposite && body != null && body.bodyType == RigidbodyType2D.Static;
                Add(valid ? Severity.Pass : Severity.Fail, "TILEMAP_COMPOSITE", PathOf(go),
                    valid ? "CompositeCollider2D and Static Rigidbody2D are present." :
                    "Tilemap uses a composite but lacks a matching CompositeCollider2D or Static Rigidbody2D.");
            }
            if (checkedCount == 0)
                Add(Severity.Warning, "TILEMAP_COMPOSITE", "", "No active Ground/Wall tilemap using a composite was found.");
        }

        private void ValidateSortingLayers(List<GameObject> objects)
        {
            int renderers = 0;
            int invalid = 0;
            bool farEnvironmentExists = false;
            foreach (SortingLayer layer in SortingLayer.layers)
                if (layer.name == "Far Environment") farEnvironmentExists = true;

            foreach (GameObject go in objects)
                foreach (Renderer renderer in go.GetComponents<Renderer>())
                {
                    renderers++;
                    if (!SortingLayer.IsValid(renderer.sortingLayerID))
                    {
                        invalid++;
                        Add(Severity.Fail, "SORTING_LAYER", PathOf(go),
                            "Renderer uses an invalid sorting layer ID: " + renderer.sortingLayerID + ".");
                    }
                    if (renderer.sortingLayerName == "Far Environment" && !farEnvironmentExists)
                    {
                        invalid++;
                        Add(Severity.Fail, "FAR_ENVIRONMENT", PathOf(go), "Far Environment sorting layer is missing.");
                    }
                }
            if (invalid == 0)
                Add(Severity.Pass, "SORTING_LAYERS", "", renderers + " renderer(s) use valid sorting layers.");
        }

        private void ValidateMuelle(List<GameObject> objects)
        {
            foreach (string name in new[] { "SceneFallRespawn", "GameManager" })
            {
                bool found = false;
                foreach (GameObject go in objects)
                    if (HasComponentNamed(go, name)) { found = true; break; }
                Add(found ? Severity.Pass : Severity.Warning, "MUELLE_" + name.ToUpperInvariant(), "",
                    found ? name + " is present." : name + " was not found in MUELLE.");
            }
        }
    }
}

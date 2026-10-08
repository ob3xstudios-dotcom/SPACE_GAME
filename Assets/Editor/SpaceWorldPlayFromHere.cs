using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.EditorTools
{
    [InitializeOnLoad]
    public static class SpaceWorldPlayFromHere
    {
        private const string Key = "SpaceWorld.PlayFromHere.";
        private const string PointScene = Key + "PointScene";
        private const string PointX = Key + "PointX";
        private const string PointY = Key + "PointY";
        private const string PointZ = Key + "PointZ";
        private const string RestorePending = Key + "RestorePending";
        private const string RestoreScene = Key + "RestoreScene";
        private const string RestoreHash = Key + "RestoreHash";
        private const string OriginalX = Key + "OriginalX";
        private const string OriginalY = Key + "OriginalY";
        private const string OriginalZ = Key + "OriginalZ";
        private const string TargetX = Key + "TargetX";
        private const string TargetY = Key + "TargetY";
        private const string TargetZ = Key + "TargetZ";
        private const string EnteredPlay = Key + "EnteredPlay";
        private const string ReturnMenuPath = "SpaceWorld/Play From Here/Return To Set Point _F8";
        private const string PlayerControllerTypeName = "Game.Player.PlayerController";
        private const double ReturnDebounceSeconds = 0.15;

        // Unity 2022.3 has this internal Editor API, but no public scene-dirty clear API.
        // Resolve it before moving anything and only invoke it after a verified restore.
        private static readonly MethodInfo ClearSceneDirtiness = typeof(EditorSceneManager).GetMethod(
            "ClearSceneDirtiness", BindingFlags.NonPublic | BindingFlags.Static,
            null, new[] { typeof(Scene) }, null);

        private static bool armed;
        private static string armedScene;
        private static float armedZ;
        private static bool abortedRestoreAttempted;
        private static double lastReturnToSetPointTime = double.NegativeInfinity;

        static SpaceWorldPlayFromHere()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += CheckAbortedStart;
        }

        [MenuItem("SpaceWorld/Play From Here/Set Point")]
        private static void SetPoint()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Warn("Set Point is only available in Edit Mode.");
                return;
            }
            Scene scene = SceneManager.GetActiveScene();
            GameObject player = FindSinglePlayer(scene);
            if (player == null || string.IsNullOrEmpty(scene.path))
            {
                Warn("Open a saved scene with exactly one active tagged Player before choosing a point.");
                return;
            }

            armed = true;
            armedScene = scene.path;
            armedZ = player.transform.position.z;
            SceneView.RepaintAll();
            Debug.Log("SpaceWorld Play From Here: click in Scene View to set a point; Escape cancels.");
        }

        [MenuItem("SpaceWorld/Play From Here/Clear")]
        private static void Clear()
        {
            armed = false;
            SessionState.SetString(PointScene, "");
            SceneView.RepaintAll();
            Debug.Log("SpaceWorld Play From Here: point cleared.");
        }

        [MenuItem("SpaceWorld/Play From Here/Start")]
        private static void Start()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(RestorePending, false))
            {
                Warn("Play is already starting/running, or a previous restore is pending.");
                return;
            }
            if (Application.unityVersion != "2022.3.62f3" || ClearSceneDirtiness == null)
            {
                Warn("This restore strategy requires Unity 2022.3.62f3 and its verified scene-dirty API.");
                return;
            }
            if (EditorSettings.enterPlayModeOptionsEnabled)
            {
                Warn("Custom Enter Play Mode options are enabled; transient scene restoration is not supported.");
                return;
            }
            if (EditorSceneManager.playModeStartScene != null || SceneManager.sceneCount != 1)
            {
                Warn("Use one open scene and disable Play Mode Start Scene for this operation.");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path))
            {
                Warn("The active scene must be loaded and saved.");
                return;
            }
            if (scene.isDirty)
            {
                Warn("Save or discard existing scene changes before Play From Here.");
                return;
            }
            if (SessionState.GetString(PointScene, "") != scene.path)
            {
                Warn("No point is set for this exact scene. Use Set Point first.");
                return;
            }

            GameObject player = FindSinglePlayer(scene);
            Rigidbody2D body = player != null ? player.GetComponent<Rigidbody2D>() : null;
            if (player == null || body == null)
            {
                Warn("The scene needs exactly one active tagged Player with a Rigidbody2D.");
                return;
            }

            Vector3 original = player.transform.position;
            Vector3 target = new Vector3(
                SessionState.GetFloat(PointX, 0f),
                SessionState.GetFloat(PointY, 0f),
                SessionState.GetFloat(PointZ, original.z));
            string sceneHash;
            try { sceneHash = HashSceneFile(scene.path); }
            catch (Exception exception)
            {
                Warn("Could not read the saved scene: " + exception.Message);
                return;
            }

            SessionState.SetString(RestoreScene, scene.path);
            SessionState.SetString(RestoreHash, sceneHash);
            SetVector(OriginalX, OriginalY, OriginalZ, original);
            SetVector(TargetX, TargetY, TargetZ, target);
            SessionState.SetBool(EnteredPlay, false);
            SessionState.SetBool(RestorePending, true);
            abortedRestoreAttempted = false;

            // Only the in-memory Player position changes. There is no Undo or scene save.
            // A prefab instance needs this temporary position override in Unity's Play backup.
            try
            {
                player.transform.position = target;
                if (PrefabUtility.IsPartOfPrefabInstance(player.transform))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception)
            {
                Warn("Could not start Play: " + exception.Message);
                RestoreAfterPlay();
            }
        }

        [MenuItem(ReturnMenuPath)]
        private static void ReturnToSetPoint()
        {
            if (!EditorApplication.isPlaying)
            {
                Warn("Return To Set Point is only available in Play Mode.");
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now - lastReturnToSetPointTime < ReturnDebounceSeconds)
                return;

            lastReturnToSetPointTime = now;

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path))
            {
                Warn("The active Play Mode scene must be loaded and saved.");
                return;
            }

            string pointScene = SessionState.GetString(PointScene, "");
            if (string.IsNullOrEmpty(pointScene))
            {
                Warn("No Set Point exists. Leave Play Mode and use Set Point first.");
                return;
            }
            if (pointScene != scene.path)
            {
                Warn("The Set Point belongs to a different scene; the Player was not moved.");
                return;
            }

            GameObject player = FindSinglePlayer(scene);
            if (player == null)
            {
                Warn("The active scene needs exactly one active tagged Player; the Player was not moved.");
                return;
            }

            Rigidbody2D body = player.GetComponent<Rigidbody2D>();
            if (body == null)
            {
                Warn("The active tagged Player needs a Rigidbody2D on its root object; the Player was not moved.");
                return;
            }

            string resetFailure;
            if (!TryResetPlayerTransientState(player, out resetFailure))
            {
                Warn(resetFailure + " The Player was not moved.");
                return;
            }

            Vector3 target = GetVector(PointX, PointY, PointZ);
            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.position = new Vector2(target.x, target.y);
            player.transform.position = target;

            Debug.Log("SpaceWorld Play From Here: Player returned to the Set Point.");
        }

        [MenuItem(ReturnMenuPath, true)]
        private static bool ValidateReturnToSetPoint()
        {
            return EditorApplication.isPlaying;
        }
        private static void OnSceneGUI(SceneView view)
        {
            Scene scene = SceneManager.GetActiveScene();
            Event current = Event.current;
            if (armed && current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                armed = false;
                current.Use();
                view.Repaint();
            }

            if (armed && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Handles.BeginGUI();
                GUI.Box(new Rect(10, 10, 310, 38), "Click to set Play start. Escape cancels.");
                Handles.EndGUI();
                if (current.type == EventType.Layout)
                    HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                if (current.type == EventType.MouseDown && current.button == 0 && !current.alt &&
                    current.mousePosition.y > 50f)
                {
                    if (!scene.IsValid() || scene.path != armedScene)
                    {
                        armed = false;
                        Warn("The active scene changed; choose a point again.");
                    }
                    else
                    {
                        Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
                        Plane plane = new Plane(Vector3.forward, new Vector3(0f, 0f, armedZ));
                        float distance;
                        if (plane.Raycast(ray, out distance))
                        {
                            Vector3 point = ray.GetPoint(distance);
                            SessionState.SetString(PointScene, scene.path);
                            SetVector(PointX, PointY, PointZ, new Vector3(point.x, point.y, armedZ));
                            armed = false;
                            Debug.Log("SpaceWorld Play From Here: point set for " + scene.path + ".");
                        }
                        else Warn("Could not project that click onto the Player's Z plane.");
                    }
                    current.Use();
                    view.Repaint();
                }
            }

            if (current.type == EventType.Repaint && scene.IsValid() &&
                SessionState.GetString(PointScene, "") == scene.path)
            {
                Vector3 point = GetVector(PointX, PointY, PointZ);
                Handles.color = Color.cyan;
                Handles.DrawWireDisc(point, Vector3.forward, HandleUtility.GetHandleSize(point) * 0.3f);
                Handles.Label(point, "Play From Here");
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(RestorePending, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                SessionState.SetBool(EnteredPlay, true);
                Scene scene = SceneManager.GetActiveScene();
                GameObject player = FindSinglePlayer(scene);
                Vector3 target = GetVector(TargetX, TargetY, TargetZ);
                if (scene.path != SessionState.GetString(RestoreScene, "") || player == null ||
                    player.GetComponent<Rigidbody2D>() == null ||
                    Vector2.Distance(player.transform.position, target) > 0.25f)
                {
                    Warn("Player did not enter Play near the chosen point. Stopping Play to restore the scene.");
                    EditorApplication.isPlaying = false;
                    return;
                }

                Rigidbody2D body = player.GetComponent<Rigidbody2D>();
                body.velocity = Vector2.zero;
                body.angularVelocity = 0f;
                Vector3 position = player.transform.position;
                if (position.z != target.z)
                    player.transform.position = new Vector3(position.x, position.y, target.z);
                Debug.Log("SpaceWorld Play From Here: started at (" + target.x + ", " + target.y + ").");
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
                RestoreAfterPlay();
        }

        private static void CheckAbortedStart()
        {
            if (!SessionState.GetBool(RestorePending, false) ||
                SessionState.GetBool(EnteredPlay, false) ||
                EditorApplication.isPlayingOrWillChangePlaymode || abortedRestoreAttempted) return;
            abortedRestoreAttempted = true;
            Warn("Play did not start; restoring the Player immediately.");
            RestoreAfterPlay();
        }

        private static void RestoreAfterPlay()
        {
            if (!SessionState.GetBool(RestorePending, false)) return;
            Scene scene = SceneManager.GetActiveScene();
            string expectedPath = SessionState.GetString(RestoreScene, "");
            if (!scene.IsValid() || !scene.isLoaded || scene.path != expectedPath)
            {
                Warn("Restore is pending, but the active scene changed. No dirty state was cleared.");
                return;
            }
            GameObject player = FindSinglePlayer(scene);
            if (player == null)
            {
                Warn("Restore is pending, but exactly one active Player was not found. No dirty state was cleared.");
                return;
            }

            Vector3 original = GetVector(OriginalX, OriginalY, OriginalZ);
            try
            {
                player.transform.position = original;
                if (PrefabUtility.IsPartOfPrefabInstance(player.transform))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
                Vector3 restored = player.transform.position;
                if (restored.x != original.x || restored.y != original.y || restored.z != original.z)
                    throw new InvalidOperationException("Player position did not restore exactly.");
                if (HashSceneFile(expectedPath) != SessionState.GetString(RestoreHash, ""))
                    throw new InvalidOperationException("The saved scene changed on disk; dirty state was retained.");
                if (scene.isDirty)
                {
                    if (ClearSceneDirtiness == null)
                        throw new InvalidOperationException("Scene-dirty clear API is unavailable.");
                    ClearSceneDirtiness.Invoke(null, new object[] { scene });
                }
                if (scene.isDirty)
                    throw new InvalidOperationException("The scene remained dirty after restoration.");
                SessionState.SetBool(RestorePending, false);
                SessionState.SetBool(EnteredPlay, false);
                abortedRestoreAttempted = false;
                Debug.Log("SpaceWorld Play From Here: original Player position restored; scene is clean.");
            }
            catch (Exception exception)
            {
                Warn("Restoration needs attention: " + exception.Message + " Restore remains pending; no scene was saved.");
            }
        }

        private static GameObject FindSinglePlayer(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            GameObject found = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (!FindPlayerIn(root.transform, ref found)) return null;
            return found;
        }

        private static bool FindPlayerIn(Transform transform, ref GameObject found)
        {
            GameObject go = transform.gameObject;
            if (go.activeInHierarchy && go.CompareTag("Player"))
            {
                if (found != null) return false;
                found = go;
            }
            for (int i = 0; i < transform.childCount; i++)
                if (!FindPlayerIn(transform.GetChild(i), ref found)) return false;
            return true;
        }

        private static bool TryResetPlayerTransientState(GameObject player, out string failure)
        {
            MonoBehaviour controller = null;
            MonoBehaviour[] behaviours = player.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour.GetType().FullName != PlayerControllerTypeName) continue;
                if (controller != null)
                {
                    failure = "More than one PlayerController was found under the active Player.";
                    return false;
                }
                controller = behaviour;
            }

            if (controller == null)
            {
                failure = "PlayerController was not found under the active Player.";
                return false;
            }

            MethodInfo resetMethod = controller.GetType().GetMethod(
                "ResetTransientState",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            if (resetMethod == null || resetMethod.ReturnType != typeof(void))
            {
                failure = "PlayerController.ResetTransientState() is unavailable.";
                return false;
            }

            try
            {
                resetMethod.Invoke(controller, null);
                failure = null;
                return true;
            }
            catch (TargetInvocationException exception)
            {
                Exception cause = exception.InnerException ?? exception;
                failure = "PlayerController.ResetTransientState() failed: " + cause.Message;
                return false;
            }
            catch (Exception exception)
            {
                failure = "PlayerController.ResetTransientState() failed: " + exception.Message;
                return false;
            }
        }
        private static string HashSceneFile(string scenePath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string fullPath = Path.Combine(projectRoot, scenePath);
            using (FileStream stream = File.OpenRead(fullPath))
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream));
        }

        private static Vector3 GetVector(string x, string y, string z)
        {
            return new Vector3(SessionState.GetFloat(x, 0f), SessionState.GetFloat(y, 0f), SessionState.GetFloat(z, 0f));
        }

        private static void SetVector(string x, string y, string z, Vector3 value)
        {
            SessionState.SetFloat(x, value.x);
            SessionState.SetFloat(y, value.y);
            SessionState.SetFloat(z, value.z);
        }

        private static void Warn(string message)
        {
            Debug.LogWarning("SpaceWorld Play From Here: " + message);
        }
    }
}

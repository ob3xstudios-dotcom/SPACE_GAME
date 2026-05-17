using Cinemachine;
using Game.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Systems
{
    public class SceneFallRespawn : MonoBehaviour
    {
        [Header("Scene Scope")]
        [Tooltip("Empty = active in any scene.")]
        [SerializeField] private string activeSceneName = "";

        [Header("Fall Respawn")]
        [SerializeField] private Transform player;
        [SerializeField] private string playerTag = "Player";
        [SerializeField] private float fallY = -11f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private bool reloading;
        private PlayerController playerController;
        private Rigidbody2D playerRb;

        private void Awake()
        {
            ResolveRefs();
        }

        private void Update()
        {
            if (!IsSceneEnabled()) return;

            ResolveRefs();
            if (player == null) return;
            if (reloading) return;
            if (player.position.y >= fallY) return;

            ReloadActiveScene();
        }

        private bool IsSceneEnabled()
        {
            if (string.IsNullOrWhiteSpace(activeSceneName)) return true;
            return SceneManager.GetActiveScene().name == activeSceneName;
        }

        private void ReloadActiveScene()
        {
            reloading = true;
            if (debugLogs)
                Debug.Log($"[RESPAWN TRACE] SceneFallRespawn.Reload begin t={Time.time:0.000} unscaled={Time.unscaledTime:0.000}");
            HitStopManager.CancelAll();
            FreezePlayer();
            FreezeCamera();

            if (debugLogs)
                Debug.Log($"[FALL RESPAWN] Reload scene {SceneManager.GetActiveScene().name}");

            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private void FreezePlayer()
        {
            if (player == null) return;

            if (playerController == null)
                playerController = player.GetComponent<PlayerController>() ?? player.GetComponentInChildren<PlayerController>(true);

            if (playerRb == null)
                playerRb = player.GetComponent<Rigidbody2D>() ?? player.GetComponentInChildren<Rigidbody2D>(true);

            if (playerController != null)
            {
                playerController.ResetTransientState();
                playerController.enabled = false;
            }

            if (playerRb != null)
            {
                playerRb.velocity = Vector2.zero;
                playerRb.angularVelocity = 0f;
                playerRb.bodyType = RigidbodyType2D.Kinematic;
                playerRb.constraints = RigidbodyConstraints2D.FreezePosition | RigidbodyConstraints2D.FreezeRotation;
            }
        }

        private void FreezeCamera()
        {
            if (debugLogs)
                Debug.Log($"[RESPAWN TRACE] SceneFallRespawn.FreezeCamera t={Time.time:0.000} unscaled={Time.unscaledTime:0.000}");

            DisableCameraMotionEffects();

            CinemachineVirtualCamera[] virtualCameras = FindObjectsOfType<CinemachineVirtualCamera>(true);
            for (int i = 0; i < virtualCameras.Length; i++)
            {
                CinemachineVirtualCamera virtualCamera = virtualCameras[i];
                if (virtualCamera == null) continue;

                virtualCamera.Follow = null;
                virtualCamera.LookAt = null;
                virtualCamera.PreviousStateIsValid = false;
            }

            CinemachineBrain[] brains = FindObjectsOfType<CinemachineBrain>(true);
            for (int i = 0; i < brains.Length; i++)
            {
                if (brains[i] != null)
                    brains[i].enabled = false;
            }
        }

        private void DisableCameraMotionEffects()
        {
            MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null) continue;

                string typeName = behaviour.GetType().Name;
                if (typeName.Contains("Impulse") ||
                    typeName.Contains("Shake") ||
                    typeName.Contains("Trauma") ||
                    typeName.Contains("Noise") ||
                    typeName.Contains("Perlin"))
                {
                    behaviour.enabled = false;
                }
            }
        }

        private void ResolveRefs()
        {
            if (player == null)
            {
                GameObject playerGo = GameObject.FindGameObjectWithTag(playerTag);
                if (playerGo != null)
                    player = playerGo.transform;
            }

            if (player == null) return;

            if (playerController == null)
                playerController = player.GetComponent<PlayerController>() ?? player.GetComponentInChildren<PlayerController>(true);

            if (playerRb == null)
                playerRb = player.GetComponent<Rigidbody2D>() ?? player.GetComponentInChildren<Rigidbody2D>(true);
        }
    }
}

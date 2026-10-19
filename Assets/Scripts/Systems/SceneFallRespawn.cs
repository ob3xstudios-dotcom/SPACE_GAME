using System.Collections;
using Cinemachine;
using Game.Currency;
using Game.Player;
using Game.Progress;
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
        [SerializeField] private bool enableGlobalFallCheck = true;
        [SerializeField] private float fallY = -11f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private bool reloading;
        private bool fallInProgress;
        private PlayerController playerController;
        private PlayerHealth playerHealth;
        private PlayerHealth subscribedPlayerHealth;
        private Rigidbody2D playerRb;
        private Coroutine rearmRoutine;

        private void Awake()
        {
            ResolveRefs();
        }

        private void OnEnable()
        {
            ResolveRefs();
            RefreshRespawnSubscription();
        }

        private void OnDisable()
        {
            UnsubscribeFromPlayerRespawn();

            if (rearmRoutine != null)
            {
                StopCoroutine(rearmRoutine);
                rearmRoutine = null;
            }

            fallInProgress = false;
        }

        private void Update()
        {
            if (!IsSceneEnabled()) return;
            if (!enableGlobalFallCheck) return;

            ResolveRefs();
            if (player == null) return;
            if (reloading) return;
            if (player.position.y >= fallY) return;

            RequestFatalFall(player);
        }

        public bool RequestFatalFall(Transform fallingPlayer)
        {
            if (!isActiveAndEnabled || !IsSceneEnabled()) return false;
            if (fallingPlayer == null || reloading || fallInProgress) return false;

            SetPlayer(fallingPlayer);
            if (player == null) return false;

            fallInProgress = true;
            CurrencyService.Instance.HandleFatalFall(player);

            if (TryRespawnWithGameManager())
                return true;

            ReloadActiveScene();
            return true;
        }

        private bool IsSceneEnabled()
        {
            if (string.IsNullOrWhiteSpace(activeSceneName)) return true;
            return SceneManager.GetActiveScene().name == activeSceneName;
        }

        private bool TryRespawnWithGameManager()
        {
            GameManager gameManager = GameManager.Instance;
            if (gameManager == null) return false;

            if (debugLogs)
                Debug.Log("[FALL RESPAWN] Respawn via GameManager");

            return gameManager.RespawnPlayerAtCheckpoint();
        }

        private void ReloadActiveScene()
        {
            reloading = true;
            GameProgressService.Instance.DiscardPendingExploration();
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
                    SetPlayer(playerGo.transform);
            }

            if (player == null) return;
            SetPlayer(player);
        }

        private void SetPlayer(Transform candidate)
        {
            if (candidate == null) return;

            PlayerHealth candidateHealth =
                candidate.GetComponent<PlayerHealth>() ??
                candidate.GetComponentInParent<PlayerHealth>() ??
                candidate.GetComponentInChildren<PlayerHealth>(true);
            Transform resolvedPlayer = candidateHealth != null
                ? candidateHealth.transform
                : candidate;

            if (player != resolvedPlayer)
            {
                UnsubscribeFromPlayerRespawn();
                player = resolvedPlayer;
                playerController = null;
                playerHealth = candidateHealth;
                playerRb = null;
            }

            ResolvePlayerComponents();
            RefreshRespawnSubscription();
        }

        private void ResolvePlayerComponents()
        {
            if (player == null) return;

            if (playerController == null)
                playerController = player.GetComponent<PlayerController>() ?? player.GetComponentInChildren<PlayerController>(true);

            if (playerHealth == null)
                playerHealth = player.GetComponent<PlayerHealth>() ?? player.GetComponentInChildren<PlayerHealth>(true);

            if (playerRb == null)
                playerRb = player.GetComponent<Rigidbody2D>() ?? player.GetComponentInChildren<Rigidbody2D>(true);
        }

        private void RefreshRespawnSubscription()
        {
            if (!isActiveAndEnabled || subscribedPlayerHealth == playerHealth) return;

            UnsubscribeFromPlayerRespawn();
            if (playerHealth == null) return;

            playerHealth.OnRespawned += HandlePlayerRespawned;
            subscribedPlayerHealth = playerHealth;
        }

        private void UnsubscribeFromPlayerRespawn()
        {
            if (subscribedPlayerHealth != null)
                subscribedPlayerHealth.OnRespawned -= HandlePlayerRespawned;

            subscribedPlayerHealth = null;
        }

        private void HandlePlayerRespawned()
        {
            if (rearmRoutine != null)
                StopCoroutine(rearmRoutine);

            rearmRoutine = StartCoroutine(RearmAfterRespawn());
        }

        private IEnumerator RearmAfterRespawn()
        {
            yield return null;
            fallInProgress = false;
            rearmRoutine = null;
        }
    }
}

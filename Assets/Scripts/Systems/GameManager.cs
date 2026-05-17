using System.Collections;
using Game.Player;
using UnityEngine;

namespace Game.Systems
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Player")]
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private string playerTag = "Player";

        [Header("Respawn")]
        [SerializeField] private Checkpoint startingCheckpoint;
        [SerializeField, Min(0f)] private float respawnDelay = 0.25f;
        [SerializeField] private bool restoreHealthOnRespawn = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private Checkpoint activeCheckpoint;
        private Coroutine respawnRoutine;
        private bool respawning;

        public Checkpoint ActiveCheckpoint => activeCheckpoint;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[GAME MANAGER] Duplicate GameManager found. Disabling duplicate.");
                enabled = false;
                return;
            }

            Instance = this;
            activeCheckpoint = startingCheckpoint;
            ResolvePlayer();
        }

        private void OnEnable()
        {
            ResolvePlayer();
            if (playerHealth == null) return;

            playerHealth.SetExternalDeathHandling(true);
            playerHealth.OnDied += HandlePlayerDied;
        }

        private void OnDisable()
        {
            if (playerHealth != null)
                playerHealth.OnDied -= HandlePlayerDied;

            if (Instance == this)
                Instance = null;
        }

        public void SetActiveCheckpoint(Checkpoint checkpoint)
        {
            if (checkpoint == null) return;
            if (activeCheckpoint == checkpoint) return;

            activeCheckpoint = checkpoint;

            if (debugLogs)
                Debug.Log($"[GAME MANAGER] Checkpoint active: {checkpoint.CheckpointId}");
        }

        private void HandlePlayerDied()
        {
            if (respawning) return;

            if (respawnRoutine != null)
                StopCoroutine(respawnRoutine);

            respawnRoutine = StartCoroutine(RespawnPlayerRoutine());
        }

        private IEnumerator RespawnPlayerRoutine()
        {
            respawning = true;
            HitStopManager.CancelAll();

            if (respawnDelay > 0f)
                yield return new WaitForSeconds(respawnDelay);

            ResolvePlayer();

            Transform respawnTarget = GetRespawnTarget();
            if (playerHealth != null)
                playerHealth.RespawnAt(respawnTarget, restoreHealthOnRespawn);

            respawning = false;
            respawnRoutine = null;
        }

        private Transform GetRespawnTarget()
        {
            if (activeCheckpoint != null)
                return activeCheckpoint.RespawnTransform;

            if (startingCheckpoint != null)
                return startingCheckpoint.RespawnTransform;

            return playerHealth != null ? playerHealth.transform : transform;
        }

        private void ResolvePlayer()
        {
            if (playerHealth != null) return;

            GameObject playerGo = GameObject.FindGameObjectWithTag(playerTag);
            if (playerGo != null)
                playerHealth = playerGo.GetComponent<PlayerHealth>() ?? playerGo.GetComponentInChildren<PlayerHealth>(true);

            if (playerHealth == null)
                playerHealth = FindObjectOfType<PlayerHealth>(true);
        }
    }
}

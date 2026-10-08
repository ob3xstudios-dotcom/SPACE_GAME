using System.Collections.Generic;
using Game.Input;
using Game.Player;
using Game.Progress;
using UnityEngine;

namespace Game.Map
{
    [DefaultExecutionOrder(-400)]
    [DisallowMultipleComponent]
    public sealed class WorldMapUI : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private GameObject mapRoot;
        [SerializeField] private MapRegionView[] regionViews;

        private MapCellView[] cellViews;
        private MapAreaView[] areaViews;

        [Header("Player (optional; resolved once if empty)")]
        [SerializeField] private PlayerController playerController;
        [SerializeField] private InputReader inputReader;
        [SerializeField] private PlayerHealth playerHealth;

        private GameProgressService progress;
        private bool ownsExternalControlLock;

        public bool IsOpen => mapRoot != null && mapRoot.activeSelf;

        private void Awake()
        {
            if (regionViews == null || regionViews.Length == 0)
                regionViews = GetComponentsInChildren<MapRegionView>(true);
            CacheGranularViews();
            WarnAboutDuplicateCellIds();

            ResolvePlayerReferences();

            if (mapRoot != null)
                mapRoot.SetActive(false);
        }

        private void OnEnable()
        {
            progress = GameProgressService.Instance;
            progress.OnMapOwnershipChanged += HandleMapOwnershipChanged;
            progress.OnRegionConfirmed += HandleRegionConfirmed;
            progress.OnMapCellConfirmed += HandleMapCellConfirmed;
            progress.OnProgressLoaded += HandleProgressLoaded;
            SubscribeToHealth();
            RefreshViews();
        }

        private void OnDisable()
        {
            CloseMap();
            UnsubscribeFromHealth();

            if (progress != null)
            {
                progress.OnMapOwnershipChanged -= HandleMapOwnershipChanged;
                progress.OnRegionConfirmed -= HandleRegionConfirmed;
                progress.OnMapCellConfirmed -= HandleMapCellConfirmed;
                progress.OnProgressLoaded -= HandleProgressLoaded;
            }
        }

        private void Update()
        {
            if (inputReader == null || !inputReader.ConsumeMapPressed())
                return;

            LogMapToggleRequest();
            if (IsOpen)
            {
                CloseMap();
                LogMapToggleResult("close", "none");
                return;
            }

            string blockReason = GetOpenBlockReason();
            if (!string.IsNullOrEmpty(blockReason))
            {
                LogMapToggleResult("ignored", blockReason);
                return;
            }

            bool opened = TryOpenMap();
            LogMapToggleResult(opened ? "open" : "ignored", opened ? "none" : "open-failed");
        }

        public bool TryOpenMap()
        {
            if (IsOpen || progress == null || !progress.MapOwned || !CanTakeControl())
                return false;

            DiscardGameplayInputs();
            playerController.SetExternalControlLocked(true);
            ownsExternalControlLock = true;
            mapRoot.SetActive(true);
            RefreshViews();
            return true;
        }

        public void CloseMap()
        {
            if (mapRoot != null)
                mapRoot.SetActive(false);

            if (ownsExternalControlLock && playerController != null)
                playerController.SetExternalControlLocked(false);

            ownsExternalControlLock = false;
            DiscardGameplayInputs();
        }

        private bool CanTakeControl()
        {
            return
                mapRoot != null &&
                inputReader != null &&
                playerController != null &&
                playerController.enabled &&
                !playerController.IsExternalControlLocked &&
                !playerController.IsDashing &&
                !playerController.IsAttacking &&
                !playerController.IsHanging &&
                !playerController.IsSwinging &&
                !playerController.IsOverheadRailHanging &&
                !playerController.IsSliding &&
                !playerController.IsDiving &&
                !playerController.IsPostDiveSliding;
        }

        private string GetOpenBlockReason()
        {
            if (progress == null)
                return "progress-unavailable";
            if (!progress.MapOwned)
                return "map-not-owned";
            if (mapRoot == null)
                return "no-ui";
            if (inputReader == null)
                return "no-input";
            if (playerController == null)
                return "no-player";
            if (!playerController.enabled)
                return "player-disabled";
            if (playerController.IsExternalControlLocked)
                return "external-lock";
            if (playerController.IsDashing || playerController.IsAttacking ||
                playerController.IsHanging || playerController.IsSwinging ||
                playerController.IsOverheadRailHanging || playerController.IsSliding ||
                playerController.IsDiving || playerController.IsPostDiveSliding)
            {
                return "player-state";
            }

            return string.Empty;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogMapToggleRequest()
        {
            Debug.Log("[MAP UI] toggle-request", this);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogMapToggleResult(string result, string reason)
        {
            Debug.Log($"[MAP UI] result={result} reason={reason}", this);
        }

        private void RefreshViews()
        {
            if (regionViews != null)
            {
                for (int i = 0; i < regionViews.Length; i++)
                {
                    if (regionViews[i] != null)
                        regionViews[i].Refresh(progress);
                }
            }

            if (cellViews != null)
            {
                for (int i = 0; i < cellViews.Length; i++)
                {
                    if (cellViews[i] != null)
                        cellViews[i].Refresh(progress);
                }
            }

            if (areaViews != null)
            {
                for (int i = 0; i < areaViews.Length; i++)
                {
                    if (areaViews[i] != null)
                        areaViews[i].Refresh(progress);
                }
            }
        }

        private void CacheGranularViews()
        {
            cellViews = GetComponentsInChildren<MapCellView>(true);
            areaViews = GetComponentsInChildren<MapAreaView>(true);
        }

        private void WarnAboutDuplicateCellIds()
        {
            if (cellViews == null || cellViews.Length == 0)
                return;

            HashSet<string> uniqueIds = new HashSet<string>(System.StringComparer.Ordinal);
            for (int i = 0; i < cellViews.Length; i++)
            {
                MapCellView cellView = cellViews[i];
                if (cellView == null)
                    continue;

                string normalizedId = GameProgressData.NormalizeId(cellView.CellId);
                if (string.IsNullOrEmpty(normalizedId) || uniqueIds.Add(normalizedId))
                    continue;

                Debug.LogWarning(
                    $"[MAP CELL] Duplicate MapCellView cellId='{normalizedId}' under '{name}'.",
                    cellView);
            }
        }

        private void DiscardGameplayInputs()
        {
            if (inputReader == null)
                return;

            inputReader.ConsumeJumpPressed();
            inputReader.ConsumeAttackPressed();
            inputReader.ConsumeDashPressed();
            inputReader.ConsumeParryPressed();
            inputReader.ConsumeInteractPressed();
            inputReader.CancelCrouchGesture("WorldMapUI");
            inputReader.ClearSwingGrabBuffer();
        }

        private void ResolvePlayerReferences()
        {
            if (playerController == null)
                playerController = FindObjectOfType<PlayerController>(true);

            if (playerController == null)
                return;

            if (inputReader == null)
                inputReader = playerController.GetComponent<InputReader>();

            if (playerHealth == null)
                playerHealth = playerController.GetComponent<PlayerHealth>();
        }

        private void SubscribeToHealth()
        {
            if (playerHealth == null)
                return;

            playerHealth.OnDamaged += HandlePlayerDamaged;
            playerHealth.OnDied += CloseMap;
            playerHealth.OnRespawned += CloseMap;
        }

        private void UnsubscribeFromHealth()
        {
            if (playerHealth == null)
                return;

            playerHealth.OnDamaged -= HandlePlayerDamaged;
            playerHealth.OnDied -= CloseMap;
            playerHealth.OnRespawned -= CloseMap;
        }

        private void HandlePlayerDamaged(int damage)
        {
            CloseMap();
        }

        private void HandleMapOwnershipChanged(bool owned)
        {
            if (!owned)
                CloseMap();

            RefreshViews();
        }

        private void HandleRegionConfirmed(string regionId)
        {
            if (progress != null && progress.LogMapDiagnostics)
                Debug.Log($"[MAP][5 MAP EVENT] confirmed region='{regionId}'", this);

            RefreshViews();
        }

        private void HandleMapCellConfirmed(string cellId)
        {
            if (progress != null && progress.LogMapDiagnostics)
                Debug.Log($"[MAP CELL][MAP EVENT] confirmed cell='{cellId}'", this);

            RefreshViews();
        }

        private void HandleProgressLoaded()
        {
            if (progress == null || !progress.MapOwned)
                CloseMap();

            RefreshViews();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            CacheGranularViews();
            WarnAboutDuplicateCellIds();
        }
#endif
    }
}

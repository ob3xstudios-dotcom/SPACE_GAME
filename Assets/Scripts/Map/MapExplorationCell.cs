using System.Collections.Generic;
using Game.Player;
using Game.Progress;
using UnityEngine;

namespace Game.Map
{
    [RequireComponent(typeof(BoxCollider2D))]
    [DisallowMultipleComponent]
    public sealed class MapExplorationCell : MonoBehaviour
    {
        [SerializeField] private MapCellDefinition cellDefinition;
        [SerializeField] private string cellId;
        [SerializeField] private string areaId;
        [SerializeField] private string subzoneId;
        [SerializeField] private Collider2D triggerCollider;
        [SerializeField] private bool logDiagnostics;

        private readonly Dictionary<PlayerController, int> playerContactCounts =
            new Dictionary<PlayerController, int>();
        private bool missingCellIdWarningLogged;

        public MapCellDefinition CellDefinition => cellDefinition;
        public string ResolvedCellId => MapCellIdentity.ResolveCellId(cellDefinition, cellId);
        public string ResolvedAreaId => MapCellIdentity.ResolveAreaId(cellDefinition, areaId);
        public string ResolvedRoomId => MapCellIdentity.ResolveRoomId(cellDefinition, subzoneId);
        public string CellId => ResolvedCellId;
        public string AreaId => ResolvedAreaId;
        public string SubzoneId => ResolvedRoomId;
        public Collider2D TriggerCollider => triggerCollider;

        private void Awake()
        {
            ResolveReferences();
            EnsureTrigger();
            WarnIfCellIdIsMissing();
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
                MapRoomLocator2D.Register(this);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null)
                return;

            playerContactCounts.TryGetValue(player, out int contactCount);
            playerContactCounts[player] = contactCount + 1;
            if (contactCount != 0)
                return;

            string normalizedCellId = ResolvedCellId;
            if (string.IsNullOrEmpty(normalizedCellId))
            {
                WarnIfCellIdIsMissing();
                return;
            }

            GameProgressService progress = GameProgressService.Instance;
            bool diagnosticsEnabled = logDiagnostics || progress.LogMapDiagnostics;
            if (diagnosticsEnabled)
            {
                Debug.Log(
                    $"[MAP CELL][TRIGGER] cell='{normalizedCellId}' " +
                    $"area='{ResolvedAreaId}' subzone='{ResolvedRoomId}' player='{player.name}'",
                    this);
            }

            bool markedPending = progress.MarkMapCellExploredPending(normalizedCellId);
            if (diagnosticsEnabled && (!markedPending || !progress.LogMapDiagnostics))
            {
                string result = markedPending
                    ? "marked_pending"
                    : progress.IsMapCellConfirmed(normalizedCellId)
                        ? "already_confirmed"
                        : progress.IsMapCellPending(normalizedCellId)
                            ? "already_pending"
                            : "rejected";

                Debug.Log(
                    $"[PENDING CELL] cell='{normalizedCellId}' result={result}",
                    this);
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null || !playerContactCounts.TryGetValue(player, out int contactCount))
                return;

            if (contactCount <= 1)
                playerContactCounts.Remove(player);
            else
                playerContactCounts[player] = contactCount - 1;
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
                MapRoomLocator2D.Unregister(this);

            playerContactCounts.Clear();
        }

        private void Reset()
        {
            ResolveReferences();
            EnsureTrigger();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            cellId = GameProgressData.NormalizeId(cellId);
            areaId = GameProgressData.NormalizeId(areaId);
            subzoneId = GameProgressData.NormalizeId(subzoneId);
            ResolveReferences();
        }
#endif

        private void ResolveReferences()
        {
            if (triggerCollider == null)
                triggerCollider = GetComponent<Collider2D>();
        }

        private void EnsureTrigger()
        {
            if (triggerCollider != null)
                triggerCollider.isTrigger = true;
        }

        private void WarnIfCellIdIsMissing()
        {
            if (missingCellIdWarningLogged || !string.IsNullOrEmpty(ResolvedCellId))
                return;

            missingCellIdWarningLogged = true;
            Debug.LogWarning(
                $"[MAP CELL] MapExplorationCell on '{name}' has no cellId and will not register exploration.",
                this);
        }

        private void OnDrawGizmos()
        {
            ResolveReferences();
            if (triggerCollider == null)
                return;

            Bounds bounds = triggerCollider.bounds;
            Gizmos.color = new Color(0.25f, 0.75f, 1f, 0.18f);
            Gizmos.DrawCube(bounds.center, bounds.size);
            Gizmos.color = new Color(0.25f, 0.75f, 1f, 0.9f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}

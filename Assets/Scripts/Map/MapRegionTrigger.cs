using System.Collections.Generic;
using Game.Player;
using Game.Progress;
using UnityEngine;

namespace Game.Map
{
    [RequireComponent(typeof(BoxCollider2D))]
    [DisallowMultipleComponent]
    public sealed class MapRegionTrigger : MonoBehaviour
    {
        [SerializeField] private string regionId;
        [SerializeField] private Color gizmoColor = new Color(0.2f, 0.9f, 0.45f, 0.25f);

        private readonly Dictionary<PlayerController, int> playerContactCounts = new Dictionary<PlayerController, int>();

        public string RegionId => regionId;

        private void Awake()
        {
            EnsureTrigger();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null)
                return;

            playerContactCounts.TryGetValue(player, out int contactCount);
            playerContactCounts[player] = contactCount + 1;
            if (contactCount == 0)
            {
                GameProgressService progress = GameProgressService.Instance;

                if (progress.LogMapDiagnostics)
                {
                    Debug.Log(
                        $"[MAP][1 TRIGGER] region='{regionId}' player='{player.name}'",
                        this);
                }

                bool markedPending = progress.MarkRegionExploredPending(regionId);

                if (progress.LogMapDiagnostics && !markedPending)
                {
                    string result = progress.IsRegionConfirmed(regionId)
                        ? "already_confirmed"
                        : progress.IsRegionPending(regionId)
                            ? "already_pending"
                            : "rejected";

                    Debug.Log(
                        $"[MAP][2 PENDING] region='{regionId}' result={result}",
                        this);
                }
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
            playerContactCounts.Clear();
        }

        private void Reset()
        {
            EnsureTrigger();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            regionId = GameProgressData.NormalizeId(regionId);
            EnsureTrigger();
        }
#endif

        private void EnsureTrigger()
        {
            BoxCollider2D trigger = GetComponent<BoxCollider2D>();
            if (trigger != null)
                trigger.isTrigger = true;
        }

        private void OnDrawGizmos()
        {
            Collider2D trigger = GetComponent<Collider2D>();
            if (trigger == null)
                return;

            Gizmos.color = gizmoColor;
            Bounds bounds = trigger.bounds;
            Gizmos.DrawCube(bounds.center, bounds.size);
            Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.9f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}

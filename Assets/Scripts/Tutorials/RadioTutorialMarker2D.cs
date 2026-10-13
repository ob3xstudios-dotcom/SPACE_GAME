using System.Collections;
using System.Collections.Generic;
using Game.Player;
using UnityEngine;

namespace Game.Tutorials
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class RadioTutorialMarker2D : MonoBehaviour
    {
        [SerializeField] private RadioTutorialDefinition definition;
        [SerializeField] private bool showGizmo = true;

        [Header("Departure")]
        [Tooltip("Delay before an exit can hide the tutorial dialogue.")]
        [SerializeField, Min(0f)] private float exitGraceSeconds = 0.35f;
        [Tooltip("Extra horizontal distance required before an exit counts as moving away.")]
        [SerializeField, Min(0f)] private float horizontalExitMargin = 1f;

        private readonly HashSet<Collider2D> overlappingPlayerColliders =
            new HashSet<Collider2D>();
        private PlayerController trackedPlayer;
        private Coroutine departureCheck;

        public RadioTutorialDefinition Definition => definition;

        private void Reset()
        {
            ConfigureCollider();
        }

        private void OnValidate()
        {
            ConfigureCollider();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other == null)
                return;

            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null || (trackedPlayer != null && trackedPlayer != player))
                return;

            if (definition == null)
            {
                Debug.LogWarning(
                    "[RADIO TUTORIAL] Tutorial marker has no definition.",
                    this);
                return;
            }

            trackedPlayer = player;
            overlappingPlayerColliders.Add(other);
            CancelDepartureCheck();
            RadioTutorialManager.TryStart(definition);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (other == null || trackedPlayer == null ||
                other.GetComponentInParent<PlayerController>() != trackedPlayer)
            {
                return;
            }

            overlappingPlayerColliders.Remove(other);
            overlappingPlayerColliders.RemoveWhere(collider => collider == null);
            if (overlappingPlayerColliders.Count > 0)
                return;

            CancelDepartureCheck();
            departureCheck = StartCoroutine(WaitForRealDeparture(trackedPlayer));
        }

        private void OnDisable()
        {
            CancelDepartureCheck();
            overlappingPlayerColliders.Clear();
            trackedPlayer = null;

            if (Application.isPlaying && definition != null)
                RadioTutorialManager.TryPause(definition);
        }

        private IEnumerator WaitForRealDeparture(PlayerController player)
        {
            float canHideAt = Time.unscaledTime + Mathf.Max(0f, exitGraceSeconds);
            while (trackedPlayer == player && overlappingPlayerColliders.Count == 0)
            {
                if (player == null ||
                    (Time.unscaledTime >= canHideAt && HasMovedAwayHorizontally(player)))
                {
                    CompleteDeparture();
                    yield break;
                }

                yield return null;
            }

            departureCheck = null;
        }

        private bool HasMovedAwayHorizontally(PlayerController player)
        {
            BoxCollider2D box = GetComponent<BoxCollider2D>();
            if (box == null) return true;

            Bounds bounds = box.bounds;
            float margin = Mathf.Max(0f, horizontalExitMargin);
            float playerX = player.transform.position.x;
            return playerX < bounds.min.x - margin ||
                   playerX > bounds.max.x + margin;
        }

        private void CompleteDeparture()
        {
            departureCheck = null;
            overlappingPlayerColliders.Clear();
            trackedPlayer = null;
            RadioTutorialManager.TryPause(definition);
        }

        private void CancelDepartureCheck()
        {
            if (departureCheck == null) return;

            StopCoroutine(departureCheck);
            departureCheck = null;
        }

        private void ConfigureCollider()
        {
            BoxCollider2D box = GetComponent<BoxCollider2D>();
            if (box == null) return;

            box.isTrigger = true;
            if (box.size == Vector2.zero)
                box.size = new Vector2(3f, 3f);
        }

        private void OnDrawGizmos()
        {
            if (!showGizmo) return;

            BoxCollider2D box = GetComponent<BoxCollider2D>();
            if (box == null) return;

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.18f, 0.85f, 1f, 0.22f);
            Gizmos.DrawCube(box.offset, box.size);
            Gizmos.color = new Color(0.18f, 0.85f, 1f, 0.9f);
            Gizmos.DrawWireCube(box.offset, box.size);
            Gizmos.matrix = previousMatrix;
        }
    }
}

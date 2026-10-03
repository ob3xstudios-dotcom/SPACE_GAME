using System.Collections.Generic;
using UnityEngine;

namespace Game.World.Factory.WoodProcessing
{
    /// <summary>
    /// Periodically creates workpieces and tracks only instances it spawned.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WoodLogSpawner : MonoBehaviour
    {
        [Header("Spawn")]
        [SerializeField] private WoodWorkpiece woodPrefab;
        [SerializeField] private Transform spawnPoint;
        [SerializeField, Min(0f)] private float spawnInterval = 4f;
        [SerializeField, Min(0f)] private float initialDelay;
        [SerializeField, Min(1)] private int maxAlive = 4;
        [SerializeField] private Vector2 initialVelocity = new Vector2(0f, -1.5f);

        [Header("Recycling")]
        [Tooltip("A recycle waits while another owned piece is this close to SpawnPoint. Set to 0 to disable the guard.")]
        [SerializeField, Min(0f)] private float spawnClearanceRadius = 2.9f;

        private readonly HashSet<WoodWorkpiece> spawnedPieces = new HashSet<WoodWorkpiece>();
        private readonly Queue<WoodWorkpiece> pendingRecycles = new Queue<WoodWorkpiece>(4);
        private readonly HashSet<WoodWorkpiece> queuedRecycles = new HashSet<WoodWorkpiece>();
        private float nextAllowedSpawnTime;

        public int AliveCount => spawnedPieces.Count;

        private void Start()
        {
            nextAllowedSpawnTime = Time.time + initialDelay;
        }

        private void Update()
        {
            if (Time.time < nextAllowedSpawnTime)
                return;

            if (pendingRecycles.Count > 0)
            {
                TryRecycleNext();
                return;
            }

            if (woodPrefab == null || spawnedPieces.Count >= maxAlive)
                return;

            if (SpawnWorkpiece())
                RecordSpawnTime();
        }

        private void OnValidate()
        {
            spawnInterval = Mathf.Max(0f, spawnInterval);
            initialDelay = Mathf.Max(0f, initialDelay);
            maxAlive = Mathf.Max(1, maxAlive);
            spawnClearanceRadius = Mathf.Max(0f, spawnClearanceRadius);
        }

        private void OnDestroy()
        {
            foreach (WoodWorkpiece piece in spawnedPieces)
            {
                if (piece != null)
                    piece.Released -= HandlePieceReleased;
            }

            spawnedPieces.Clear();
            pendingRecycles.Clear();
            queuedRecycles.Clear();
        }

        public bool TryRecycle(WoodWorkpiece piece)
        {
            if (piece == null || piece.OwnerSpawner != this || !spawnedPieces.Contains(piece))
                return false;

            if (!queuedRecycles.Add(piece))
                return true;

            piece.PrepareForDeferredRecycle();
            pendingRecycles.Enqueue(piece);
            return true;
        }

        private bool SpawnWorkpiece()
        {
            Transform point = spawnPoint != null ? spawnPoint : transform;
            WoodWorkpiece piece = Instantiate(woodPrefab, point.position, point.rotation);
            if (piece == null)
                return false;

            if (spawnedPieces.Add(piece))
                piece.Released += HandlePieceReleased;

            piece.AssignOwner(this);
            piece.ResetForRespawn(point.position, point.rotation, initialVelocity);
            return true;
        }

        private void HandlePieceReleased(WoodWorkpiece piece)
        {
            if (ReferenceEquals(piece, null))
                return;

            spawnedPieces.Remove(piece);
            queuedRecycles.Remove(piece);
            piece.Released -= HandlePieceReleased;
        }

        private void TryRecycleNext()
        {
            while (pendingRecycles.Count > 0)
            {
                WoodWorkpiece piece = pendingRecycles.Peek();
                if (piece == null || piece.OwnerSpawner != this || !spawnedPieces.Contains(piece))
                {
                    pendingRecycles.Dequeue();
                    queuedRecycles.Remove(piece);
                    continue;
                }

                if (!IsSpawnPointClearFor(piece))
                    return;

                pendingRecycles.Dequeue();
                queuedRecycles.Remove(piece);
                RecycleImmediately(piece);
                RecordSpawnTime();
                return;
            }
        }

        private void RecycleImmediately(WoodWorkpiece piece)
        {
            Transform point = spawnPoint != null ? spawnPoint : transform;
            piece.ResetForRespawn(point.position, point.rotation, initialVelocity);
        }

        private void RecordSpawnTime()
        {
            nextAllowedSpawnTime = Time.time + spawnInterval;
        }

        private bool IsSpawnPointClearFor(WoodWorkpiece recyclingPiece)
        {
            if (spawnClearanceRadius <= 0f)
                return true;

            Transform point = spawnPoint != null ? spawnPoint : transform;
            Vector2 spawnPosition = point.position;
            float clearanceSqr = spawnClearanceRadius * spawnClearanceRadius;

            foreach (WoodWorkpiece piece in spawnedPieces)
            {
                if (piece == null || piece == recyclingPiece || queuedRecycles.Contains(piece))
                    continue;

                Vector2 piecePosition = piece.Body != null
                    ? piece.Body.position
                    : (Vector2)piece.transform.position;

                if ((piecePosition - spawnPosition).sqrMagnitude < clearanceSqr)
                    return false;
            }

            return true;
        }

        private void OnDrawGizmosSelected()
        {
            Transform point = spawnPoint != null ? spawnPoint : transform;
            Vector3 position = point.position;

            Gizmos.color = new Color(0.55f, 0.9f, 0.2f, 0.95f);
            Gizmos.DrawWireSphere(position, 0.16f);
            Gizmos.DrawLine(position, position + Vector3.down * 0.65f);
            Gizmos.DrawLine(position + Vector3.down * 0.65f, position + new Vector3(-0.12f, -0.45f, 0f));
            Gizmos.DrawLine(position + Vector3.down * 0.65f, position + new Vector3(0.12f, -0.45f, 0f));
        }
    }
}

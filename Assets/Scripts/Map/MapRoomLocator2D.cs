using System.Collections.Generic;
using UnityEngine;

namespace Game.Map
{
    public struct MapRoomQuery2D
    {
        internal MapExplorationCell Cell;
        internal MapRoomIdentity Room;
        internal Vector2 Position;
        internal int RegistryVersion;
        internal bool HasSample;
        internal bool WasResolved;

        public void Clear()
        {
            this = default;
        }
    }

    public static class MapRoomLocator2D
    {
        private static readonly List<MapExplorationCell> cells =
            new List<MapExplorationCell>();

        private static int registryVersion;

        public static int RegisteredCellCount => cells.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            cells.Clear();
            registryVersion = 0;
        }

        internal static void Register(MapExplorationCell cell)
        {
            if (cell == null || cells.Contains(cell))
                return;

            cells.Add(cell);
            registryVersion++;
        }

        internal static void Unregister(MapExplorationCell cell)
        {
            if (cell == null || !cells.Remove(cell))
                return;

            registryVersion++;
        }

        public static bool TryResolve(
            Vector2 worldPosition,
            ref MapRoomQuery2D query,
            out MapRoomIdentity room)
        {
            if (query.RegistryVersion == registryVersion &&
                query.HasSample &&
                query.Position == worldPosition)
            {
                room = query.Room;
                return query.WasResolved;
            }

            MapExplorationCell previousCell = query.RegistryVersion == registryVersion
                ? query.Cell
                : null;

            if (TryResolvePrevious(previousCell, worldPosition, out room))
            {
                StoreResult(ref query, previousCell, room, worldPosition, true);
                return true;
            }

            MapExplorationCell bestCell = null;
            MapRoomIdentity bestRoom = default;
            float bestArea = float.PositiveInfinity;

            for (int i = cells.Count - 1; i >= 0; i--)
            {
                MapExplorationCell cell = cells[i];
                if (cell == null)
                {
                    cells.RemoveAt(i);
                    registryVersion++;
                    continue;
                }

                if (!TryGetContainingRoom(cell, worldPosition, out MapRoomIdentity candidateRoom))
                    continue;

                float candidateArea = GetWorldBoundsArea(cell.TriggerCollider);
                bool smallerArea = candidateArea < bestArea;
                bool deterministicTie =
                    Mathf.Approximately(candidateArea, bestArea) &&
                    (bestCell == null || cell.GetInstanceID() < bestCell.GetInstanceID());

                if (bestCell != null && !smallerArea && !deterministicTie)
                    continue;

                bestCell = cell;
                bestRoom = candidateRoom;
                bestArea = candidateArea;
            }

            bool resolved = bestCell != null;
            StoreResult(ref query, bestCell, bestRoom, worldPosition, resolved);
            room = bestRoom;
            return resolved;
        }

        public static bool TryResolve(Vector2 worldPosition, out MapRoomIdentity room)
        {
            MapRoomQuery2D query = default;
            return TryResolve(worldPosition, ref query, out room);
        }

        private static bool TryResolvePrevious(
            MapExplorationCell previousCell,
            Vector2 worldPosition,
            out MapRoomIdentity room)
        {
            room = default;
            return previousCell != null &&
                   TryGetContainingRoom(previousCell, worldPosition, out room);
        }

        private static bool TryGetContainingRoom(
            MapExplorationCell cell,
            Vector2 worldPosition,
            out MapRoomIdentity room)
        {
            room = default;
            if (cell == null || !cell.isActiveAndEnabled)
                return false;

            Collider2D cellCollider = cell.TriggerCollider;
            if (cellCollider == null ||
                !cellCollider.enabled ||
                !cellCollider.OverlapPoint(worldPosition))
            {
                return false;
            }

            MapRoomIdentity candidate = new MapRoomIdentity(
                cell.ResolvedAreaId,
                cell.ResolvedRoomId);
            if (!candidate.IsValid)
                return false;

            room = candidate;
            return true;
        }

        private static float GetWorldBoundsArea(Collider2D cellCollider)
        {
            if (cellCollider == null)
                return float.PositiveInfinity;

            Bounds bounds = cellCollider.bounds;
            return Mathf.Abs(bounds.size.x * bounds.size.y);
        }

        private static void StoreResult(
            ref MapRoomQuery2D query,
            MapExplorationCell cell,
            MapRoomIdentity room,
            Vector2 position,
            bool resolved)
        {
            query.Cell = cell;
            query.Room = room;
            query.Position = position;
            query.RegistryVersion = registryVersion;
            query.HasSample = true;
            query.WasResolved = resolved;
        }
    }
}

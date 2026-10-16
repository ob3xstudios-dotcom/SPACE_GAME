using System.Collections.Generic;
using Game.Map;
using UnityEngine;

namespace Game.Audio
{
    internal enum AcousticRoomDecision
    {
        ListenerUnavailable,
        ListenerUncovered,
        SourceUncovered,
        SameRoom,
        Connected,
        Isolated
    }

    internal readonly struct AcousticRoomEvaluation
    {
        public AcousticRoomEvaluation(
            AcousticRoomDecision decision,
            Vector2 listenerPosition,
            MapRoomIdentity listenerRoom,
            MapRoomIdentity sourceRoom,
            float transmission)
        {
            Decision = decision;
            ListenerPosition = listenerPosition;
            ListenerRoom = listenerRoom;
            SourceRoom = sourceRoom;
            Transmission = transmission;
        }

        public AcousticRoomDecision Decision { get; }
        public Vector2 ListenerPosition { get; }
        public MapRoomIdentity ListenerRoom { get; }
        public MapRoomIdentity SourceRoom { get; }
        public float Transmission { get; }
    }

    internal static class AcousticRoomSystem2D
    {
        private const float DefaultIsolatedTransmission = 0f;

        private static readonly List<AcousticRoomConnection2D> connections =
            new List<AcousticRoomConnection2D>();

        private static MapRoomQuery2D listenerQuery;
        private static MapRoomIdentity listenerRoom;
        private static Vector2 listenerPosition;
        private static bool listenerAvailable;
        private static int listenerResolutionFrame = -1;
        private static bool listenerResolutionValid;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            connections.Clear();
            listenerQuery.Clear();
            listenerRoom = default;
            listenerPosition = default;
            listenerAvailable = false;
            listenerResolutionFrame = -1;
            listenerResolutionValid = false;
        }

        internal static void RegisterConnection(AcousticRoomConnection2D connection)
        {
            if (connection != null && !connections.Contains(connection))
                connections.Add(connection);
        }

        internal static void UnregisterConnection(AcousticRoomConnection2D connection)
        {
            if (connection != null)
                connections.Remove(connection);
        }

        internal static float GetTransmission(
            Vector2 sourcePosition,
            ref MapRoomQuery2D sourceQuery)
        {
            return GetTransmission(sourcePosition, ref sourceQuery, out _);
        }

        internal static float GetTransmission(
            Vector2 sourcePosition,
            ref MapRoomQuery2D sourceQuery,
            out AcousticRoomEvaluation evaluation)
        {
            if (!ResolveListenerRoom())
            {
                sourceQuery.Clear();
                evaluation = new AcousticRoomEvaluation(
                    listenerAvailable
                        ? AcousticRoomDecision.ListenerUncovered
                        : AcousticRoomDecision.ListenerUnavailable,
                    listenerPosition,
                    default,
                    default,
                    1f);
                return 1f;
            }

            if (!MapRoomLocator2D.TryResolve(
                    sourcePosition,
                    ref sourceQuery,
                    out MapRoomIdentity sourceRoom))
            {
                evaluation = new AcousticRoomEvaluation(
                    AcousticRoomDecision.SourceUncovered,
                    listenerPosition,
                    listenerRoom,
                    default,
                    1f);
                return 1f;
            }

            if (sourceRoom == listenerRoom)
            {
                evaluation = new AcousticRoomEvaluation(
                    AcousticRoomDecision.SameRoom,
                    listenerPosition,
                    listenerRoom,
                    sourceRoom,
                    1f);
                return 1f;
            }

            float transmission = GetConnectionTransmission(listenerRoom, sourceRoom);
            evaluation = new AcousticRoomEvaluation(
                transmission > 0f
                    ? AcousticRoomDecision.Connected
                    : AcousticRoomDecision.Isolated,
                listenerPosition,
                listenerRoom,
                sourceRoom,
                transmission);
            return transmission;
        }

        private static bool ResolveListenerRoom()
        {
            if (listenerResolutionFrame == Time.frameCount)
                return listenerResolutionValid;

            listenerResolutionFrame = Time.frameCount;
            listenerAvailable = AudioRegionListener2D.TryGetPrimaryPosition(
                out listenerPosition);
            if (!listenerAvailable)
            {
                listenerQuery.Clear();
                listenerRoom = default;
                listenerPosition = default;
                listenerResolutionValid = false;
                return false;
            }

            listenerResolutionValid = MapRoomLocator2D.TryResolve(
                listenerPosition,
                ref listenerQuery,
                out listenerRoom);
            return listenerResolutionValid;
        }

        private static float GetConnectionTransmission(
            MapRoomIdentity first,
            MapRoomIdentity second)
        {
            float bestTransmission = DefaultIsolatedTransmission;
            for (int i = connections.Count - 1; i >= 0; i--)
            {
                AcousticRoomConnection2D connection = connections[i];
                if (connection == null)
                {
                    connections.RemoveAt(i);
                    continue;
                }

                if (connection.Connects(first, second))
                {
                    bestTransmission = Mathf.Max(
                        bestTransmission,
                        connection.Transmission);
                }
            }

            return Mathf.Clamp01(bestTransmission);
        }
    }
}

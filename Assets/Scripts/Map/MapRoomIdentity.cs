using System;

namespace Game.Map
{
    public readonly struct MapRoomIdentity : IEquatable<MapRoomIdentity>
    {
        public MapRoomIdentity(string areaId, string roomId)
        {
            AreaId = Normalize(areaId);
            RoomId = Normalize(roomId);
        }

        public string AreaId { get; }
        public string RoomId { get; }
        public bool IsValid =>
            !string.IsNullOrEmpty(AreaId) &&
            !string.IsNullOrEmpty(RoomId);

        public bool Equals(MapRoomIdentity other)
        {
            return string.Equals(AreaId, other.AreaId, StringComparison.Ordinal) &&
                   string.Equals(RoomId, other.RoomId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is MapRoomIdentity other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int areaHash = AreaId != null ? StringComparer.Ordinal.GetHashCode(AreaId) : 0;
                int roomHash = RoomId != null ? StringComparer.Ordinal.GetHashCode(RoomId) : 0;
                return (areaHash * 397) ^ roomHash;
            }
        }

        public override string ToString()
        {
            return IsValid ? AreaId + "/" + RoomId : string.Empty;
        }

        public static bool operator ==(MapRoomIdentity left, MapRoomIdentity right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(MapRoomIdentity left, MapRoomIdentity right)
        {
            return !left.Equals(right);
        }

        private static string Normalize(string value)
        {
            return value != null ? value.Trim() : string.Empty;
        }
    }
}

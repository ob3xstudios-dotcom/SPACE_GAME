using Game.Map;
using UnityEngine;

namespace Game.Audio
{
    [DisallowMultipleComponent]
    public sealed class AcousticRoomConnection2D : MonoBehaviour
    {
        [Header("Room A")]
        [SerializeField] private string areaAId;
        [SerializeField] private string roomAId;
        [Header("Room B")]
        [SerializeField] private string areaBId;
        [SerializeField] private string roomBId;
        [Tooltip("Audible fraction transmitted between both rooms while this component is enabled.")]
        [SerializeField, Range(0f, 1f)] private float transmission = 0.25f;

        public MapRoomIdentity RoomA => new MapRoomIdentity(areaAId, roomAId);
        public MapRoomIdentity RoomB => new MapRoomIdentity(areaBId, roomBId);
        public float Transmission => Mathf.Clamp01(transmission);

        private void OnEnable()
        {
            if (Application.isPlaying)
                AcousticRoomSystem2D.RegisterConnection(this);
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
                AcousticRoomSystem2D.UnregisterConnection(this);
        }

        public void SetTransmission(float value)
        {
            transmission = Mathf.Clamp01(value);
        }

        public bool Connects(MapRoomIdentity first, MapRoomIdentity second)
        {
            MapRoomIdentity roomA = RoomA;
            MapRoomIdentity roomB = RoomB;
            return isActiveAndEnabled &&
                   roomA.IsValid &&
                   roomB.IsValid &&
                   ((roomA == first && roomB == second) ||
                    (roomA == second && roomB == first));
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            areaAId = Normalize(areaAId);
            roomAId = Normalize(roomAId);
            areaBId = Normalize(areaBId);
            roomBId = Normalize(roomBId);
            transmission = Mathf.Clamp01(transmission);
        }
#endif

        private static string Normalize(string value)
        {
            return value != null ? value.Trim() : string.Empty;
        }
    }
}

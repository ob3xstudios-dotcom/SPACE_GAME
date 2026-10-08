using UnityEngine;

namespace Game.Map
{
    [CreateAssetMenu(
        fileName = "MapCellDefinition",
        menuName = "SpaceWorld/Map/Cell Definition")]
    public sealed class MapCellDefinition : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Persistent save identity. Do not change after this ID has been used in a save.")]
        private string cellId;

        [SerializeField] private string areaId;
        [SerializeField] private string roomId;
        [SerializeField] private string displayName;
        [SerializeField] private bool secret;
        [SerializeField, TextArea] private string notes;

        public string CellId => cellId;
        public string AreaId => areaId;
        public string RoomId => roomId;
        public string DisplayName => displayName;
        public bool Secret => secret;
        public string Notes => notes;

#if UNITY_EDITOR
        private void OnValidate()
        {
            cellId = Normalize(cellId);
            areaId = Normalize(areaId);
            roomId = Normalize(roomId);
            displayName = Normalize(displayName);
            notes = notes != null ? notes.Trim() : string.Empty;
        }
#endif

        private static string Normalize(string value)
        {
            return value != null ? value.Trim() : string.Empty;
        }
    }
}

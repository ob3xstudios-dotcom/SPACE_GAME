using System.Collections.Generic;
using UnityEngine;

namespace Game.Map
{
    [CreateAssetMenu(
        fileName = "MapAreaDefinition",
        menuName = "SpaceWorld/Map/Area Definition")]
    public sealed class MapAreaDefinition : ScriptableObject
    {
        [SerializeField] private string areaId;
        [SerializeField] private string displayName;
        [SerializeField] private List<MapCellDefinition> cells = new List<MapCellDefinition>();

        public string AreaId => areaId;
        public string DisplayName => displayName;
        public IReadOnlyList<MapCellDefinition> Cells => cells;

#if UNITY_EDITOR
        private void OnValidate()
        {
            areaId = areaId != null ? areaId.Trim() : string.Empty;
            displayName = displayName != null ? displayName.Trim() : string.Empty;
        }
#endif
    }
}

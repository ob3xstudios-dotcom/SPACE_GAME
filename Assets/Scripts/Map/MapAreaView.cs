using Game.Progress;
using UnityEngine;

namespace Game.Map
{
    [DisallowMultipleComponent]
    public sealed class MapAreaView : MonoBehaviour
    {
        [SerializeField] private MapAreaDefinition areaDefinition;
        [SerializeField] private string areaId;
        [SerializeField] private string displayName;
        [SerializeField] private GameObject areaRoot;
        [SerializeField] private GameObject labelRoot;
        [SerializeField] private bool showLabelWhenMapOwned = true;
        [SerializeField] private bool showLabelOnlyAfterFirstConfirmedCell;

        private MapCellView[] cellViews;
        private int confirmedCells;

        public MapAreaDefinition AreaDefinition => areaDefinition;
        public string AreaId => areaDefinition != null
            ? areaDefinition.AreaId
            : areaId;
        public string DisplayName => areaDefinition != null
            ? areaDefinition.DisplayName
            : displayName;
        public int TotalCells => cellViews != null ? cellViews.Length : 0;
        public int ConfirmedCells => confirmedCells;
        public float Exploration01 => TotalCells > 0
            ? confirmedCells / (float)TotalCells
            : 0f;

        private void Awake()
        {
            CacheCellViews();
        }

        public void Refresh(GameProgressService progress)
        {
            CacheCellViews();

            bool mapOwned = progress != null && progress.MapOwned;
            confirmedCells = 0;
            for (int i = 0; i < TotalCells; i++)
            {
                MapCellView cellView = cellViews[i];
                if (cellView != null && progress != null &&
                    progress.IsMapCellConfirmed(cellView.CellId))
                {
                    confirmedCells++;
                }
            }

            if (areaRoot != null)
                areaRoot.SetActive(mapOwned);

            bool labelVisible =
                mapOwned &&
                showLabelWhenMapOwned &&
                (!showLabelOnlyAfterFirstConfirmedCell || confirmedCells > 0);
            if (labelRoot != null)
                labelRoot.SetActive(labelVisible);
        }

        private void CacheCellViews()
        {
            cellViews = GetComponentsInChildren<MapCellView>(true);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            areaId = GameProgressData.NormalizeId(areaId);
            displayName = displayName != null ? displayName.Trim() : string.Empty;
            CacheCellViews();
        }
#endif
    }
}

using Game.Progress;
using UnityEngine;

namespace Game.Map
{
    [DisallowMultipleComponent]
    public sealed class MapCellView : MonoBehaviour
    {
        [SerializeField] private MapCellDefinition cellDefinition;
        [SerializeField] private string cellId;
        [SerializeField] private string subzoneId;
        [SerializeField] private GameObject outlineRoot;
        [SerializeField] private GameObject contentRoot;
        [SerializeField] private bool showOutlineWhenMapOwned = true;
        [SerializeField] private bool overrideSecretOutlineDefault;
        [SerializeField] private bool hideOutlineUntilConfirmed;

        private bool hasAppliedState;
        private bool lastMapOwned;
        private bool lastConfirmed;
        private bool missingCellIdWarningLogged;

        public MapCellDefinition CellDefinition => cellDefinition;
        public string ResolvedCellId => MapCellIdentity.ResolveCellId(cellDefinition, cellId);
        public string ResolvedAreaId => MapCellIdentity.ResolveAreaId(cellDefinition, string.Empty);
        public string ResolvedRoomId => MapCellIdentity.ResolveRoomId(cellDefinition, subzoneId);
        public bool Secret => cellDefinition != null && cellDefinition.Secret;
        public string CellId => ResolvedCellId;
        public string SubzoneId => ResolvedRoomId;
        public GameObject OutlineRoot => outlineRoot;
        public GameObject ContentRoot => contentRoot;

        public void Refresh(GameProgressService progress)
        {
            WarnIfCellIdIsMissing();
            bool mapOwned = progress != null && progress.MapOwned;
            bool confirmed = mapOwned && progress.IsMapCellConfirmed(ResolvedCellId);
            ApplyState(mapOwned, confirmed);
        }

        public void ApplyState(bool mapOwned, bool confirmed)
        {
            bool stateChanged =
                !hasAppliedState ||
                lastMapOwned != mapOwned ||
                lastConfirmed != confirmed;

            bool showUnconfirmedOutline =
                showOutlineWhenMapOwned &&
                (!Secret || overrideSecretOutlineDefault);
            bool outlineVisible =
                mapOwned &&
                (confirmed || (showUnconfirmedOutline && !hideOutlineUntilConfirmed));
            bool contentVisible = mapOwned && confirmed;

            if (outlineRoot != null)
                outlineRoot.SetActive(outlineVisible);
            if (contentRoot != null)
                contentRoot.SetActive(contentVisible);

            hasAppliedState = true;
            lastMapOwned = mapOwned;
            lastConfirmed = confirmed;

            if (stateChanged && GameProgressService.Instance.LogMapDiagnostics)
            {
                Debug.Log(
                    $"[MAP CELL][VIEW] cell='{ResolvedCellId}' mapOwned={mapOwned} " +
                    $"confirmed={confirmed} outlineActive={outlineVisible} " +
                    $"contentActive={contentVisible}",
                    this);
            }
        }

        private void WarnIfCellIdIsMissing()
        {
            if (missingCellIdWarningLogged || !string.IsNullOrEmpty(ResolvedCellId))
                return;

            missingCellIdWarningLogged = true;
            Debug.LogWarning(
                $"[MAP CELL] MapCellView on '{name}' has no MapCellDefinition or legacy cellId.",
                this);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            cellId = GameProgressData.NormalizeId(cellId);
            subzoneId = GameProgressData.NormalizeId(subzoneId);
        }
#endif
    }
}

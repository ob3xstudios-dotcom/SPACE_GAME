using Game.Progress;
using UnityEngine;

namespace Game.Map
{
    [DisallowMultipleComponent]
    public sealed class MapRegionView : MonoBehaviour
    {
        [SerializeField] private string regionId;
        [SerializeField] private string displayName;
        [SerializeField] private GameObject outlineRoot;
        [SerializeField] private GameObject contentRoot;

        private bool hasAppliedState;
        private bool lastMapOwned;
        private bool lastConfirmed;

        public string RegionId => regionId;
        public string DisplayName => displayName;

        public void Refresh(GameProgressService progress)
        {
            bool mapOwned = progress != null && progress.MapOwned;
            bool confirmed = mapOwned && progress.IsRegionConfirmed(regionId);
            ApplyState(mapOwned, confirmed);
        }

        public void ApplyState(bool mapOwned, bool confirmed)
        {
            bool stateChanged =
                !hasAppliedState ||
                lastMapOwned != mapOwned ||
                lastConfirmed != confirmed;

            if (outlineRoot != null)
                outlineRoot.SetActive(mapOwned);

            if (contentRoot != null)
                contentRoot.SetActive(mapOwned && confirmed);

            hasAppliedState = true;
            lastMapOwned = mapOwned;
            lastConfirmed = confirmed;

            if (stateChanged && GameProgressService.Instance.LogMapDiagnostics)
            {
                Debug.Log(
                    $"[MAP][6 VIEW] region='{regionId}' mapOwned={mapOwned} " +
                    $"confirmed={confirmed} contentActive={contentRoot != null && contentRoot.activeSelf}",
                    this);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            regionId = GameProgressData.NormalizeId(regionId);
        }
#endif
    }
}

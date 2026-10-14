using UnityEngine;

namespace Game.Audio
{
    [DisallowMultipleComponent]
    public sealed class AudioRegionListener2D : MonoBehaviour
    {
        [Tooltip("Optional point used for region checks. Defaults to this transform.")]
        [SerializeField] private Transform samplePoint;
        [Tooltip("Used only if more than one listener is active. Higher values win.")]
        [SerializeField] private int priority;

        public Vector2 Position
        {
            get
            {
                Transform target = samplePoint != null ? samplePoint : transform;
                return target.position;
            }
        }

        public int Priority => priority;

        private void OnEnable()
        {
            if (Application.isPlaying)
                MusicDirector.RegisterListener(this);
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
                MusicDirector.UnregisterListener(this);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying)
                MusicDirector.RequestRegionRefresh();
        }
#endif
    }
}

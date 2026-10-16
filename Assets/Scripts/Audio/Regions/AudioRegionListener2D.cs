using System.Collections.Generic;
using UnityEngine;

namespace Game.Audio
{
    [DisallowMultipleComponent]
    public sealed class AudioRegionListener2D : MonoBehaviour
    {
        private static readonly List<AudioRegionListener2D> activeListeners =
            new List<AudioRegionListener2D>();

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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            activeListeners.Clear();
        }

        public static bool TryGetPrimaryPosition(out Vector2 position)
        {
            AudioRegionListener2D best = null;
            for (int i = activeListeners.Count - 1; i >= 0; i--)
            {
                AudioRegionListener2D listener = activeListeners[i];
                if (listener == null)
                {
                    activeListeners.RemoveAt(i);
                    continue;
                }

                if (!listener.isActiveAndEnabled)
                    continue;

                if (best == null ||
                    listener.Priority > best.Priority ||
                    (listener.Priority == best.Priority &&
                     listener.GetInstanceID() < best.GetInstanceID()))
                {
                    best = listener;
                }
            }

            if (best != null)
            {
                position = best.Position;
                return true;
            }

            position = default;
            return false;
        }

        private void OnEnable()
        {
            if (!activeListeners.Contains(this))
                activeListeners.Add(this);

            if (Application.isPlaying)
                MusicDirector.RegisterListener(this);
        }

        private void OnDisable()
        {
            activeListeners.Remove(this);

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

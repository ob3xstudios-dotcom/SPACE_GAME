using UnityEngine;

namespace Game.Audio
{
    [DisallowMultipleComponent]
    public sealed class FootstepAudioSurface2D : MonoBehaviour
    {
        [Tooltip("Optional walk event for this surface. The player default is used when empty.")]
        [SerializeField] private AudioEventDefinition walkEvent;

        [Tooltip("Optional run event for this surface. Falls back to the walk event when empty.")]
        [SerializeField] private AudioEventDefinition runEvent;

        public AudioEventDefinition GetEvent(bool running)
        {
            if (running)
                return runEvent != null ? runEvent : walkEvent;

            return walkEvent != null ? walkEvent : runEvent;
        }
    }
}

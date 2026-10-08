using UnityEngine;

namespace Game.Puzzles
{
    /// <summary>
    /// Lightweight identity marker for physical objects used by puzzle receivers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PuzzleObjectMarker : MonoBehaviour
    {
        [SerializeField] private string objectId;

        public string ObjectId => objectId;

        private void Awake()
        {
            objectId = NormalizeId(objectId);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            objectId = NormalizeId(objectId);
        }
#endif

        public static string NormalizeId(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}

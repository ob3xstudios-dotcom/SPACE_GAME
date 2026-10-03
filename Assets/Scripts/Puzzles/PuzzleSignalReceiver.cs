using UnityEngine;

namespace Game.Puzzles
{
    /// <summary>
    /// Base API for anything that consumes a binary puzzle signal.
    /// </summary>
    public abstract class PuzzleSignalReceiver : MonoBehaviour
    {
        public abstract void ApplyPuzzleSignal(bool active);
    }
}

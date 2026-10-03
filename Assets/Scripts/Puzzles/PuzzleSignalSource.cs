using System;
using UnityEngine;

namespace Game.Puzzles
{
    /// <summary>
    /// Canonical binary source for puzzle state. Notifications are edge-triggered.
    /// </summary>
    public abstract class PuzzleSignalSource : MonoBehaviour
    {
        [Header("Debug")]
        [SerializeField] private bool debugLogs;

        public event Action<PuzzleSignalSource, bool> StateChanged;

        public bool IsActive { get; private set; }

        protected bool DebugLogsEnabled => debugLogs;

        protected bool SetActive(bool active)
        {
            if (IsActive == active)
                return false;

            IsActive = active;

            if (debugLogs)
                Debug.Log($"[PUZZLE][SOURCE] name={name} state={IsActive}", this);

            StateChanged?.Invoke(this, IsActive);
            return true;
        }
    }
}

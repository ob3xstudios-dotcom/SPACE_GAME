using System;
using UnityEngine;

namespace Game.Puzzles
{
    /// <summary>
    /// Explicit inspector-wired connection between one source and any number of receivers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PuzzleSignalLink : MonoBehaviour
    {
        [Header("Connection")]
        [SerializeField] private PuzzleSignalSource source;
        [SerializeField] private PuzzleSignalReceiver[] receivers = Array.Empty<PuzzleSignalReceiver>();

        [Header("Options")]
        [SerializeField] private bool invertSignal;
        [SerializeField] private bool pushInitialState = true;
        [SerializeField] private bool debugLogs;

        private PuzzleSignalSource subscribedSource;

        private void OnEnable()
        {
            Subscribe();

            if (pushInitialState && subscribedSource != null)
                Dispatch(subscribedSource.IsActive);
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            Unsubscribe();

            if (source == null)
                return;

            subscribedSource = source;
            subscribedSource.StateChanged += HandleSourceStateChanged;
        }

        private void Unsubscribe()
        {
            if (subscribedSource != null)
                subscribedSource.StateChanged -= HandleSourceStateChanged;

            subscribedSource = null;
        }

        private void HandleSourceStateChanged(PuzzleSignalSource changedSource, bool active)
        {
            if (changedSource != subscribedSource)
                return;

            Dispatch(active);
        }

        private void Dispatch(bool sourceState)
        {
            bool state = invertSignal ? !sourceState : sourceState;
            if (receivers == null)
                return;

            for (int i = 0; i < receivers.Length; i++)
            {
                PuzzleSignalReceiver receiver = receivers[i];
                if (receiver == null)
                    continue;

                receiver.ApplyPuzzleSignal(state);

                if (debugLogs)
                {
                    Debug.Log(
                        $"[PUZZLE][LINK] source={subscribedSource.name} receiver={receiver.name} state={state}",
                        this);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (source == null || receivers == null)
                return;

            Color previousColor = Gizmos.color;
            Gizmos.color = invertSignal
                ? new Color(1f, 0.55f, 0.15f, 0.9f)
                : new Color(0.2f, 0.85f, 1f, 0.9f);

            for (int i = 0; i < receivers.Length; i++)
            {
                PuzzleSignalReceiver receiver = receivers[i];
                if (receiver != null)
                    Gizmos.DrawLine(source.transform.position, receiver.transform.position);
            }

            Gizmos.color = previousColor;
        }
    }
}

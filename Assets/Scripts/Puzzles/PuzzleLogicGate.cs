using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Puzzles
{
    public enum PuzzleLogicMode
    {
        All,
        Any,
        AtLeast
    }

    /// <summary>
    /// Event-driven combinational gate. Null and duplicate input references are ignored.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PuzzleLogicGate : PuzzleSignalSource
    {
        [Header("Logic")]
        [SerializeField] private PuzzleSignalSource[] inputs = Array.Empty<PuzzleSignalSource>();
        [SerializeField] private PuzzleLogicMode mode = PuzzleLogicMode.All;
        [SerializeField, Min(1)] private int atLeastCount = 1;

        private readonly HashSet<PuzzleSignalSource> subscribedInputs =
            new HashSet<PuzzleSignalSource>();

        private void OnEnable()
        {
            SubscribeToInputs();
            Evaluate();
        }

        private void OnDisable()
        {
            UnsubscribeFromInputs();
            SetActive(false);
        }

        private void SubscribeToInputs()
        {
            UnsubscribeFromInputs();

            if (inputs == null)
                return;

            for (int i = 0; i < inputs.Length; i++)
            {
                PuzzleSignalSource input = inputs[i];
                if (input == null || input == this || !subscribedInputs.Add(input))
                    continue;

                input.StateChanged += HandleInputStateChanged;
            }
        }

        private void UnsubscribeFromInputs()
        {
            foreach (PuzzleSignalSource input in subscribedInputs)
            {
                if (input != null)
                    input.StateChanged -= HandleInputStateChanged;
            }

            subscribedInputs.Clear();
        }

        private void HandleInputStateChanged(PuzzleSignalSource input, bool active)
        {
            Evaluate();
        }

        private void Evaluate()
        {
            int validCount = subscribedInputs.Count;
            int activeCount = 0;

            foreach (PuzzleSignalSource input in subscribedInputs)
            {
                if (input != null && input.IsActive)
                    activeCount++;
            }

            bool result;
            switch (mode)
            {
                case PuzzleLogicMode.Any:
                    result = activeCount > 0;
                    break;

                case PuzzleLogicMode.AtLeast:
                    result = validCount > 0 && activeCount >= Mathf.Max(1, atLeastCount);
                    break;

                default:
                    result = validCount > 0 && activeCount == validCount;
                    break;
            }

            SetActive(result);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            atLeastCount = Mathf.Max(1, atLeastCount);
        }
#endif

        private void OnDrawGizmosSelected()
        {
            if (inputs == null)
                return;

            Color previousColor = Gizmos.color;
            Gizmos.color = new Color(0.75f, 0.35f, 1f, 0.9f);

            for (int i = 0; i < inputs.Length; i++)
            {
                PuzzleSignalSource input = inputs[i];
                if (input != null && input != this)
                    Gizmos.DrawLine(input.transform.position, transform.position);
            }

            Gizmos.color = previousColor;
        }
    }
}

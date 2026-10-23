using System;
using UnityEngine;

namespace Game.Events
{
    public enum GameplayNoiseAction
    {
        Footstep,
        Jump,
        WallJump,
        Landing,
        HardLanding,
        Dash,
        Slide,
        Dive,
        AttackImpact,
        Parry
    }

    public readonly struct GameplayNoiseEvent
    {
        public GameplayNoiseEvent(
            Vector2 position,
            float intensity,
            GameplayNoiseAction action,
            GameObject emitter)
            : this(position, intensity, 0f, 0f, action, emitter)
        {
        }

        public GameplayNoiseEvent(
            Vector2 position,
            float intensity,
            float radius,
            float duration,
            GameplayNoiseAction action,
            GameObject emitter)
        {
            Position = position;
            Intensity = Mathf.Clamp01(intensity);
            Radius = Mathf.Max(0f, radius);
            Duration = Mathf.Max(0f, duration);
            Action = action;
            Emitter = emitter;
        }

        public Vector2 Position { get; }
        public float Intensity { get; }
        /// <summary>Maximum world-space propagation radius of this emission.</summary>
        public float Radius { get; }
        /// <summary>Useful lifetime for presentation or stimulus memory.</summary>
        public float Duration { get; }
        public GameplayNoiseAction Action { get; }
        public GameObject Emitter { get; }
    }

    [CreateAssetMenu(
        fileName = "Gameplay Noise Event Channel SO",
        menuName = "Game/Events/Gameplay Noise Channel")]
    public sealed class GameplayNoiseEventChannelSO : ScriptableObject
    {
        private event Action<GameplayNoiseEvent> noiseRaised;

        public void Subscribe(Action<GameplayNoiseEvent> listener)
        {
            if (listener == null)
                return;

            noiseRaised -= listener;
            noiseRaised += listener;
        }

        public void Unsubscribe(Action<GameplayNoiseEvent> listener)
        {
            if (listener != null)
                noiseRaised -= listener;
        }

        public void Raise(
            Vector2 position,
            float intensity,
            GameplayNoiseAction action,
            GameObject emitter)
        {
            Raise(position, intensity, 0f, 0f, action, emitter);
        }

        public void Raise(
            Vector2 position,
            float intensity,
            float radius,
            float duration,
            GameplayNoiseAction action,
            GameObject emitter)
        {
            if (emitter == null)
                return;

            float normalizedIntensity = Mathf.Clamp01(intensity);
            if (normalizedIntensity <= 0f)
                return;

            noiseRaised?.Invoke(
                new GameplayNoiseEvent(
                    position,
                    normalizedIntensity,
                    radius,
                    duration,
                    action,
                    emitter));
        }

        private void OnDisable()
        {
            noiseRaised = null;
        }
    }
}

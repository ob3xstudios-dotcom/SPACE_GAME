using System;
using UnityEngine;

namespace Game.Audio
{
    public readonly struct AudioPlaybackHandle : IEquatable<AudioPlaybackHandle>
    {
        internal AudioPlaybackHandle(int id)
        {
            Id = id;
        }

        internal int Id { get; }

        public bool IsValid => Id != 0 && AudioService.IsPlaying(this);

        public bool Stop(float fadeOutSeconds = 0f)
        {
            return AudioService.Stop(this, fadeOutSeconds);
        }

        public bool SetPosition(Vector3 position)
        {
            return AudioService.SetPosition(this, position);
        }

        public bool SetVolume(float volumeMultiplier)
        {
            return AudioService.SetVolume(this, volumeMultiplier);
        }

        public bool Equals(AudioPlaybackHandle other)
        {
            return Id == other.Id;
        }

        public override bool Equals(object obj)
        {
            return obj is AudioPlaybackHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Id;
        }

        public static bool operator ==(AudioPlaybackHandle left, AudioPlaybackHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(AudioPlaybackHandle left, AudioPlaybackHandle right)
        {
            return !left.Equals(right);
        }
    }
}


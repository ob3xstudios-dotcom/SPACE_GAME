namespace Game.Audio
{
    public enum AudioBus
    {
        Master,
        Music,
        Ambience,
        SFX,
        UI,
        Voice
    }

    public static class AudioMixerParameters
    {
        public const string MasterVolume = "MasterVolume";
        public const string MusicVolume = "MusicVolume";
        public const string AmbienceVolume = "AmbienceVolume";
        public const string SfxVolume = "SFXVolume";
        public const string UiVolume = "UIVolume";
        public const string VoiceVolume = "VoiceVolume";

        public static string GetVolumeParameter(AudioBus bus)
        {
            switch (bus)
            {
                case AudioBus.Music:
                    return MusicVolume;
                case AudioBus.Ambience:
                    return AmbienceVolume;
                case AudioBus.SFX:
                    return SfxVolume;
                case AudioBus.UI:
                    return UiVolume;
                case AudioBus.Voice:
                    return VoiceVolume;
                default:
                    return MasterVolume;
            }
        }
    }
}


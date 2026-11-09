using System;
using UnityEngine;

namespace Game
{
    public enum DisplayMode
    {
        Fullscreen = 0,
        Borderless = 1,
        Windowed = 2
    }

    public enum ShadowQuality
    {
        Off = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }

    /// <summary>
    /// Player settings (graphics, audio, controls, interface), saved per
    /// machine in PlayerPrefs. <see cref="GameSettingsApplier"/> applies
    /// them to the game; menus edit them and call <see cref="Save"/>.
    /// </summary>
    public static class GameSettings
    {
        private const string Prefix = "FR.";

        // Graphics
        public static DisplayMode DisplayMode = DisplayMode.Borderless;
        public static int ResolutionWidth;
        public static int ResolutionHeight;
        public static bool VSync = true;
        public static int MaxFps = 144;          // 0 = unlimited (when VSync is off)
        public static int QualityLevel = -1;     // -1 = project default
        public static int RenderDistance = 8;    // chunks
        public static ShadowQuality Shadows = ShadowQuality.High;
        public static bool PostProcessing = true;
        public static float FieldOfView = 75f;

        // Audio
        public static float MasterVolume = 1f;
        public static float EffectsVolume = 0.8f;

        // Controls
        public static float MouseSensitivity = 1f;
        public static bool InvertY;

        // Interface
        public static bool ShowFps;
        public static bool ShowNetworkStats;

        // Remembered menu input
        public static string LastAddress = "127.0.0.1";
        public static int LastPort = 25565;

        public static readonly int[] FpsOptions = { 30, 60, 120, 144, 165, 240, 0 };

        public const int MinRenderDistance = 4;
        public const int MaxRenderDistance = 16;

        /// <summary>Raised after settings changed (Apply or Load).</summary>
        public static event Action Changed;

        private static bool _loaded;

        public static void Load()
        {
            DisplayMode = (DisplayMode)PlayerPrefs.GetInt(Prefix + "DisplayMode", (int)DisplayMode.Borderless);
            ResolutionWidth = PlayerPrefs.GetInt(Prefix + "ResolutionWidth", 0);
            ResolutionHeight = PlayerPrefs.GetInt(Prefix + "ResolutionHeight", 0);
            VSync = PlayerPrefs.GetInt(Prefix + "VSync", 1) != 0;
            MaxFps = PlayerPrefs.GetInt(Prefix + "MaxFps", 144);
            QualityLevel = PlayerPrefs.GetInt(Prefix + "QualityLevel", -1);
            RenderDistance = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "RenderDistance", 8), MinRenderDistance, MaxRenderDistance);
            Shadows = (ShadowQuality)PlayerPrefs.GetInt(Prefix + "Shadows", (int)ShadowQuality.High);
            PostProcessing = PlayerPrefs.GetInt(Prefix + "PostProcessing", 1) != 0;
            FieldOfView = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "FieldOfView", 75f), 50f, 110f);

            MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "MasterVolume", 1f));
            EffectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "EffectsVolume", 0.8f));

            MouseSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "MouseSensitivity", 1f), 0.1f, 4f);
            InvertY = PlayerPrefs.GetInt(Prefix + "InvertY", 0) != 0;

            ShowFps = PlayerPrefs.GetInt(Prefix + "ShowFps", 0) != 0;
            ShowNetworkStats = PlayerPrefs.GetInt(Prefix + "ShowNetworkStats", 0) != 0;

            LastAddress = PlayerPrefs.GetString(Prefix + "LastAddress", "127.0.0.1");
            LastPort = PlayerPrefs.GetInt(Prefix + "LastPort", 25565);

            _loaded = true;
            Changed?.Invoke();
        }

        public static void EnsureLoaded()
        {
            if (!_loaded)
                Load();
        }

        public static void Save()
        {
            PlayerPrefs.SetInt(Prefix + "DisplayMode", (int)DisplayMode);
            PlayerPrefs.SetInt(Prefix + "ResolutionWidth", ResolutionWidth);
            PlayerPrefs.SetInt(Prefix + "ResolutionHeight", ResolutionHeight);
            PlayerPrefs.SetInt(Prefix + "VSync", VSync ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "MaxFps", MaxFps);
            PlayerPrefs.SetInt(Prefix + "QualityLevel", QualityLevel);
            PlayerPrefs.SetInt(Prefix + "RenderDistance", RenderDistance);
            PlayerPrefs.SetInt(Prefix + "Shadows", (int)Shadows);
            PlayerPrefs.SetInt(Prefix + "PostProcessing", PostProcessing ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "FieldOfView", FieldOfView);

            PlayerPrefs.SetFloat(Prefix + "MasterVolume", MasterVolume);
            PlayerPrefs.SetFloat(Prefix + "EffectsVolume", EffectsVolume);

            PlayerPrefs.SetFloat(Prefix + "MouseSensitivity", MouseSensitivity);
            PlayerPrefs.SetInt(Prefix + "InvertY", InvertY ? 1 : 0);

            PlayerPrefs.SetInt(Prefix + "ShowFps", ShowFps ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "ShowNetworkStats", ShowNetworkStats ? 1 : 0);

            PlayerPrefs.SetString(Prefix + "LastAddress", LastAddress);
            PlayerPrefs.SetInt(Prefix + "LastPort", LastPort);

            PlayerPrefs.Save();
        }

        /// <summary>Notifies listeners that settings changed (applies them).</summary>
        public static void Apply()
        {
            Changed?.Invoke();
        }

        /// <summary>Back to defaults (does not save).</summary>
        public static void ResetToDefaults()
        {
            DisplayMode = DisplayMode.Borderless;
            ResolutionWidth = 0;
            ResolutionHeight = 0;
            VSync = true;
            MaxFps = 144;
            QualityLevel = -1;
            RenderDistance = 8;
            Shadows = ShadowQuality.High;
            PostProcessing = true;
            FieldOfView = 75f;
            MasterVolume = 1f;
            EffectsVolume = 0.8f;
            MouseSensitivity = 1f;
            InvertY = false;
            ShowFps = false;
            ShowNetworkStats = false;
            Changed?.Invoke();
        }

        public static float ShadowDistance(ShadowQuality quality)
        {
            switch (quality)
            {
                case ShadowQuality.Low: return 40f;
                case ShadowQuality.Medium: return 75f;
                case ShadowQuality.High: return 120f;
                default: return 0f;
            }
        }
    }
}

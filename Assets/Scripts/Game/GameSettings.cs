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

    public enum GraphicsPreset
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Ultra = 3,
        Custom = 4
    }

    public enum AntiAliasing
    {
        Off = 0,
        Fxaa = 1,
        Smaa = 2
    }

    /// <summary>Concrete shadow parameters for a <see cref="ShadowQuality"/>.</summary>
    public struct ShadowParameters
    {
        public float Distance;   // metres, 0 = off
        public int Cascades;
        public int Resolution;
        public bool Soft;
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
        public static ShadowQuality Shadows = ShadowQuality.Medium;
        public static bool PostProcessing = true;
        public static float FieldOfView = 75f;
        public static GraphicsPreset Preset = GraphicsPreset.High;
        public static int GrassDistance = 3;     // chunks with grass/flowers, 0 = none
        public static int SimplifyDistance = 5;  // chunks beyond this use simplified meshes, 0 = never
        public static bool Clouds = true;
        public static bool Wind = true;
        public static float RenderScale = 1f;    // 0.5..1 of the screen resolution
        public static AntiAliasing AntiAliasing = AntiAliasing.Smaa;

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
        public static string LastWorld = "World 1";
        public static int LastPort = 25565;

        public static readonly int[] FpsOptions = { 30, 60, 120, 144, 165, 240, 0 };

        public const int MinRenderDistance = 4;
        public const int MaxRenderDistance = 16;
        public const int MaxGrassDistance = 6;

        public static readonly int[] SimplifyOptions = { 0, 3, 4, 5, 6, 8 };

        public static readonly float[] RenderScaleOptions = { 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1f };

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
            Shadows = (ShadowQuality)Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "Shadows", (int)ShadowQuality.Medium), 0, 3);
            PostProcessing = PlayerPrefs.GetInt(Prefix + "PostProcessing", 1) != 0;
            FieldOfView = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "FieldOfView", 75f), 50f, 110f);
            Preset = (GraphicsPreset)Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "Preset", (int)GraphicsPreset.High), 0, 4);
            GrassDistance = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "GrassDistance", 3), 0, MaxGrassDistance);
            SimplifyDistance = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "SimplifyDistance", 5), 0, 32);
            Clouds = PlayerPrefs.GetInt(Prefix + "Clouds", 1) != 0;
            Wind = PlayerPrefs.GetInt(Prefix + "Wind", 1) != 0;
            RenderScale = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "RenderScale", 1f), 0.5f, 1f);
            AntiAliasing = (AntiAliasing)Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "AntiAliasing", (int)AntiAliasing.Smaa), 0, 2);

            // Settings saved by older versions had no preset: start them on
            // the (much cheaper) High preset instead of the old heavy defaults.
            if (!PlayerPrefs.HasKey(Prefix + "Preset"))
                ApplyPresetValues(GraphicsPreset.High);

            MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "MasterVolume", 1f));
            EffectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "EffectsVolume", 0.8f));

            MouseSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "MouseSensitivity", 1f), 0.1f, 4f);
            InvertY = PlayerPrefs.GetInt(Prefix + "InvertY", 0) != 0;

            ShowFps = PlayerPrefs.GetInt(Prefix + "ShowFps", 0) != 0;
            ShowNetworkStats = PlayerPrefs.GetInt(Prefix + "ShowNetworkStats", 0) != 0;

            LastAddress = PlayerPrefs.GetString(Prefix + "LastAddress", "127.0.0.1");
            LastWorld = PlayerPrefs.GetString(Prefix + "LastWorld", "World 1");
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
            PlayerPrefs.SetInt(Prefix + "Preset", (int)Preset);
            PlayerPrefs.SetInt(Prefix + "GrassDistance", GrassDistance);
            PlayerPrefs.SetInt(Prefix + "SimplifyDistance", SimplifyDistance);
            PlayerPrefs.SetInt(Prefix + "Clouds", Clouds ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "Wind", Wind ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "RenderScale", RenderScale);
            PlayerPrefs.SetInt(Prefix + "AntiAliasing", (int)AntiAliasing);

            PlayerPrefs.SetFloat(Prefix + "MasterVolume", MasterVolume);
            PlayerPrefs.SetFloat(Prefix + "EffectsVolume", EffectsVolume);

            PlayerPrefs.SetFloat(Prefix + "MouseSensitivity", MouseSensitivity);
            PlayerPrefs.SetInt(Prefix + "InvertY", InvertY ? 1 : 0);

            PlayerPrefs.SetInt(Prefix + "ShowFps", ShowFps ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "ShowNetworkStats", ShowNetworkStats ? 1 : 0);

            PlayerPrefs.SetString(Prefix + "LastAddress", LastAddress);
            PlayerPrefs.SetString(Prefix + "LastWorld", LastWorld);
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
            FieldOfView = 75f;
            ApplyPresetValues(GraphicsPreset.High);
            MasterVolume = 1f;
            EffectsVolume = 0.8f;
            MouseSensitivity = 1f;
            InvertY = false;
            ShowFps = false;
            ShowNetworkStats = false;
            Changed?.Invoke();
        }

        public static float ShadowDistance(ShadowQuality quality) => GetShadowParameters(quality).Distance;

        /// <summary>
        /// Shadow cost grows with distance (more terrain drawn into the
        /// shadow map), cascades (terrain drawn once per cascade) and
        /// resolution (fill rate and memory).
        /// </summary>
        public static ShadowParameters GetShadowParameters(ShadowQuality quality)
        {
            switch (quality)
            {
                case ShadowQuality.Low: return new ShadowParameters { Distance = 35f, Cascades = 1, Resolution = 1024, Soft = false };
                case ShadowQuality.Medium: return new ShadowParameters { Distance = 60f, Cascades = 2, Resolution = 2048, Soft = true };
                case ShadowQuality.High: return new ShadowParameters { Distance = 100f, Cascades = 4, Resolution = 4096, Soft = true };
                default: return new ShadowParameters { Distance = 0f, Cascades = 1, Resolution = 1024, Soft = false };
            }
        }

        /// <summary>Sets every graphics option of a preset (does not save or apply).</summary>
        public static void ApplyPresetValues(GraphicsPreset preset)
        {
            Preset = preset;

            switch (preset)
            {
                case GraphicsPreset.Low:
                    RenderDistance = 5; Shadows = ShadowQuality.Off; GrassDistance = 1; Clouds = false; Wind = false;
                    PostProcessing = false; RenderScale = 0.8f; AntiAliasing = AntiAliasing.Fxaa; SimplifyDistance = 3;
                    break;
                case GraphicsPreset.Medium:
                    RenderDistance = 6; Shadows = ShadowQuality.Low; GrassDistance = 2; Clouds = true; Wind = true;
                    PostProcessing = true; RenderScale = 1f; AntiAliasing = AntiAliasing.Fxaa; SimplifyDistance = 4;
                    break;
                case GraphicsPreset.High:
                    RenderDistance = 8; Shadows = ShadowQuality.Medium; GrassDistance = 3; Clouds = true; Wind = true;
                    PostProcessing = true; RenderScale = 1f; AntiAliasing = AntiAliasing.Smaa; SimplifyDistance = 5;
                    break;
                case GraphicsPreset.Ultra:
                    RenderDistance = 12; Shadows = ShadowQuality.High; GrassDistance = 5; Clouds = true; Wind = true;
                    PostProcessing = true; RenderScale = 1f; AntiAliasing = AntiAliasing.Smaa; SimplifyDistance = 6;
                    break;
            }
        }

        /// <summary>Called when one option is changed by hand.</summary>
        public static void MarkCustom()
        {
            Preset = GraphicsPreset.Custom;
        }
    }
}

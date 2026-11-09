using GameAudio;
using Networking;
using UnityEngine;
using World.Rendering;
using World.Streaming;

namespace Game
{
    /// <summary>
    /// Applies <see cref="GameSettings"/> to the running game: screen mode,
    /// resolution, frame rate, quality, audio, mouse look, render distance,
    /// shadows, post-processing, field of view and debug overlays. Lives for
    /// the whole session (created by <see cref="GameBootstrap"/>).
    /// </summary>
    public sealed class GameSettingsApplier : MonoBehaviour
    {
        private const float NearClip = 0.05f;

        private int _appliedQuality = int.MinValue;
        private bool _screenApplied;
        private bool _dirty = true;

        private void OnEnable()
        {
            GameSettings.EnsureLoaded();
            GameSettings.Changed += MarkDirty;
            ApplyGlobal();
        }

        private void OnDisable()
        {
            GameSettings.Changed -= MarkDirty;
        }

        private void MarkDirty()
        {
            _dirty = true;
            ApplyGlobal();
        }

        private void ApplyGlobal()
        {
            // Quality first: it may swap the render pipeline asset.
            int quality = GameSettings.QualityLevel;
            if (quality >= 0 && quality < QualitySettings.names.Length && quality != _appliedQuality)
            {
                QualitySettings.SetQualityLevel(quality, true);
                _appliedQuality = quality;
            }

            QualitySettings.vSyncCount = GameSettings.VSync ? 1 : 0;
            Application.targetFrameRate = GameSettings.VSync || GameSettings.MaxFps <= 0 ? -1 : GameSettings.MaxFps;

            ApplyScreen();

            AudioListener.volume = GameSettings.MasterVolume;
            GameSounds.Volume = GameSettings.EffectsVolume;

            PlayerInputReader.SensitivityMultiplier = GameSettings.MouseSensitivity;
            PlayerInputReader.InvertY = GameSettings.InvertY;

            FpsCounter.Visible = GameSettings.ShowFps;
        }

        private void ApplyScreen()
        {
            if (Application.isEditor && _screenApplied)
                return; // the editor's game view ignores most of this

            FullScreenMode mode =
                GameSettings.DisplayMode == DisplayMode.Fullscreen ? FullScreenMode.ExclusiveFullScreen :
                GameSettings.DisplayMode == DisplayMode.Borderless ? FullScreenMode.FullScreenWindow :
                FullScreenMode.Windowed;

            int width = GameSettings.ResolutionWidth;
            int height = GameSettings.ResolutionHeight;

            if (width <= 0 || height <= 0)
            {
                // Default: the monitor's native resolution.
                width = Screen.currentResolution.width;
                height = Screen.currentResolution.height;
            }

            if (Screen.width != width || Screen.height != height || Screen.fullScreenMode != mode)
                Screen.SetResolution(width, height, mode);

            _screenApplied = true;
        }

        private void LateUpdate()
        {
            // Things that live in the scene and may appear later (camera,
            // world, overlays) are re-applied every frame - all cheap.
            Camera camera = Camera.main;
            if (camera != null && !Mathf.Approximately(camera.fieldOfView, GameSettings.FieldOfView))
                camera.fieldOfView = GameSettings.FieldOfView;

            // The player is 0.6 m wide; a near plane of 0.3 m (the prefab
            // value) lets you see into blocks when standing against them.
            if (camera != null && camera.nearClipPlane > NearClip)
                camera.nearClipPlane = NearClip;

            ChunkStreamer streamer = ChunkStreamerCache.Get();
            if (streamer != null && streamer.RenderDistance != GameSettings.RenderDistance)
                streamer.RenderDistance = GameSettings.RenderDistance;

            NetworkRuntime runtime = NetworkRuntime.Current;
            if (runtime != null && runtime.TryGetComponent(out NetworkDebugOverlay overlay) && _dirty)
                overlay.Visible = GameSettings.ShowNetworkStats;

            VoxelAtmosphere atmosphere = AtmosphereCache.Get();
            if (atmosphere != null && (_dirty || atmosphere != _lastAtmosphere))
            {
                atmosphere.SetShadowDistance(GameSettings.ShadowDistance(GameSettings.Shadows));
                atmosphere.SetPostProcessing(GameSettings.PostProcessing);
                _lastAtmosphere = atmosphere;
            }

            _dirty = false;
        }

        private VoxelAtmosphere _lastAtmosphere;

        private static class ChunkStreamerCache
        {
            private static ChunkStreamer _cached;
            public static ChunkStreamer Get() => _cached != null ? _cached : (_cached = FindFirstObjectByType<ChunkStreamer>());
        }

        private static class AtmosphereCache
        {
            private static VoxelAtmosphere _cached;
            public static VoxelAtmosphere Get() => _cached != null ? _cached : (_cached = FindFirstObjectByType<VoxelAtmosphere>());
        }
    }
}

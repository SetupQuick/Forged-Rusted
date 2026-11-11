using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using World;
using World.Rendering;
using World.Streaming;

namespace Game
{
    /// <summary>
    /// Built-in benchmark: flies a camera along the same path over the
    /// offline world once per graphics preset (Low, Medium, High, Ultra)
    /// and measures frame times, so the team can compare machines and
    /// see what each option costs.
    ///
    /// For every preset:
    ///   1. apply the preset and fly to the start of the path;
    ///   2. wait until the chunks around it are generated and meshed;
    ///   3. fly for <see cref="MeasureSeconds"/> at sprint-and-a-half speed
    ///      (new chunks keep streaming in, like real play) while recording
    ///      every frame.
    /// Reported: average FPS, 1% low FPS, frame time (avg / 99th
    /// percentile / worst), CPU main-thread and GPU time when available,
    /// triangles, draw calls, memory, chunk counts. Results are shown on
    /// screen and written to a CSV file in Application.persistentDataPath.
    ///
    /// Started from the main menu ("Benchmark") or with -benchmark on the
    /// command line. The time of day is fixed at noon so runs compare.
    /// The player's own settings are restored afterwards.
    /// </summary>
    public sealed class BenchmarkRunner : MonoBehaviour
    {
        private const float MeasureSeconds = 20f;
        private const float WarmupSeconds = 2f;
        private const float LoadTimeoutSeconds = 30f;
        private const float FlySpeed = 9f;          // m/s (sprint is ~6)
        private const float FlyHeight = 14f;        // above the terrain

        private static readonly GraphicsPreset[] Presets =
        {
            GraphicsPreset.Low, GraphicsPreset.Medium, GraphicsPreset.High, GraphicsPreset.Ultra
        };

        public sealed class Result
        {
            public string Name;
            public int Frames;
            public float AverageFps;
            public float OnePercentLowFps;
            public float AverageMs;
            public float P99Ms;
            public float WorstMs;
            public float CpuMs;
            public float GpuMs;
            public long Triangles;
            public long DrawCalls;
            public long SetPassCalls;
            public float LoadSeconds;
            public int Chunks;
            public int SimplifiedChunks;
            public long SystemMemoryMb;
            public long GfxMemoryMb;
            public string Settings;
        }

        private readonly List<Result> _results = new List<Result>();
        private readonly List<float> _frameMs = new List<float>(4096);

        private Camera _camera;
        private Action<string> _onFinished;
        private bool _cancelled;
        private Vector3 _start;
        private Vector3 _direction;
        private float _smoothedHeight;

        /// <summary>True while a benchmark is running (menus stay hidden).</summary>
        public static bool Running { get; private set; }

        /// <summary>Path of the last CSV written.</summary>
        public static string LastReportPath { get; private set; }

        /// <summary>Starts a run; onFinished gets the text summary.</summary>
        public static BenchmarkRunner Begin(Action<string> onFinished)
        {
            if (Running)
                return null;

            var go = new GameObject("Benchmark");
            var runner = go.AddComponent<BenchmarkRunner>();
            runner._onFinished = onFinished;
            return runner;
        }

        public void Cancel()
        {
            _cancelled = true;
        }

        private void Start()
        {
            Running = true;
            StartCoroutine(Run());
        }

        private void OnDestroy()
        {
            Running = false;

            if (_camera != null)
                Destroy(_camera.gameObject);
        }

        private IEnumerator Run()
        {
            WorldManager world = WorldManager.Active;

            while (world == null || !world.IsReady)
            {
                world = WorldManager.Active;

                // Started from the command line before the menu made a world.
                if (world != null && !world.IsReady)
                    world.InitializeWorld(world.Settings.Seed);

                yield return null;
            }

            // Fixed time of day and the player's settings kept aside.
            float savedTime = WorldClock.TimeOfDay;
            bool savedPaused = WorldClock.Paused;
            WorldClock.SetTimeOfDay(0.45f);
            WorldClock.Paused = true;

            bool savedVSync = GameSettings.VSync;
            int savedMaxFps = GameSettings.MaxFps;

            CreateCamera(world);

            foreach (GraphicsPreset preset in Presets)
            {
                if (_cancelled)
                    break;

                GameSettings.ApplyPresetValues(preset);
                GameSettings.VSync = false;      // measure the real frame rate
                GameSettings.MaxFps = 0;
                GameSettings.Apply();

                yield return Measure(world, preset.ToString());
            }

            // Restore.
            GameSettings.Load();
            GameSettings.VSync = savedVSync;
            GameSettings.MaxFps = savedMaxFps;
            GameSettings.Apply();

            WorldClock.Paused = savedPaused;
            WorldClock.SetTimeOfDay(savedTime);

            WriteCsv();
            string summary = BuildSummary();
            Debug.Log(summary);

            _onFinished?.Invoke(summary);
            Destroy(gameObject);
        }

        private void CreateCamera(WorldManager world)
        {
            var go = new GameObject("Benchmark Camera");
            go.tag = "MainCamera";
            _camera = go.AddComponent<Camera>();
            _camera.farClipPlane = 600f;
            _camera.fieldOfView = GameSettings.FieldOfView;

            _start = world.FindSpawnPosition(Vector3.zero);
            _direction = new Vector3(0.8f, 0f, 0.6f).normalized;
            _smoothedHeight = _start.y + FlyHeight;

            ChunkStreamer streamer = FindFirstObjectByType<ChunkStreamer>();
            if (streamer != null)
                streamer.FallbackTarget = go.transform;
        }

        private void PlaceCamera(WorldManager world, float distance, bool snap)
        {
            Vector3 flat = _start + _direction * distance;
            int ground = world.GetSurfaceHeight(Mathf.FloorToInt(flat.x / world.VoxelSize), Mathf.FloorToInt(flat.z / world.VoxelSize));
            float target = Mathf.Max(ground * world.VoxelSize, (world.Config?.SeaLevel ?? 0) * world.VoxelSize) + FlyHeight;

            _smoothedHeight = snap ? target : Mathf.Lerp(_smoothedHeight, target, 1f - Mathf.Exp(-1.5f * Time.unscaledDeltaTime));

            _camera.transform.position = new Vector3(flat.x, _smoothedHeight, flat.z);
            _camera.transform.rotation = Quaternion.LookRotation(_direction + Vector3.down * 0.25f);
        }

        private IEnumerator Measure(WorldManager world, string name)
        {
            // 1. Load the area at the start of the path.
            PlaceCamera(world, 0f, snap: true);
            float loadStart = Time.realtimeSinceStartup;
            int stableFrames = 0;

            while (Time.realtimeSinceStartup - loadStart < LoadTimeoutSeconds && !_cancelled)
            {
                stableFrames = world.PendingJobCount == 0 ? stableFrames + 1 : 0;
                if (stableFrames > 10)
                    break;

                yield return null;
            }

            float loadSeconds = Time.realtimeSinceStartup - loadStart;

            // 2. Warm up (shader variants, pools), then 3. measure while flying.
            using (ProfilerRecorder triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count"))
            using (ProfilerRecorder drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count"))
            using (ProfilerRecorder setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count"))
            using (ProfilerRecorder mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15))
            using (ProfilerRecorder systemMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory"))
            using (ProfilerRecorder gfxMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory"))
            {
                float elapsed = 0f;
                float distance = 0f;

                while (elapsed < WarmupSeconds + MeasureSeconds && !_cancelled)
                {
                    float dt = Time.unscaledDeltaTime;
                    bool measuring = elapsed >= WarmupSeconds;

                    if (measuring)
                    {
                        distance += FlySpeed * dt;
                        _frameMs.Add(dt * 1000f);
                        _triangleSum += triangles.Valid ? triangles.LastValue : 0;
                        _drawSum += drawCalls.Valid ? drawCalls.LastValue : 0;
                        _setPassSum += setPass.Valid ? setPass.LastValue : 0;
                        _cpuSum += mainThread.Valid ? mainThread.LastValue / 1e6 : 0;
                        _gpuSum += SampleGpuMs();
                        _samples++;
                    }
                    else
                    {
                        FrameTimingManager.CaptureFrameTimings();
                    }

                    PlaceCamera(world, distance, snap: false);
                    elapsed += dt;
                    yield return null;
                }

                if (_frameMs.Count > 0)
                {
                    _results.Add(Summarise(name, loadSeconds, world,
                        systemMemory.Valid ? systemMemory.LastValue : 0,
                        gfxMemory.Valid ? gfxMemory.LastValue : 0));
                }
            }

            _frameMs.Clear();
            _triangleSum = _drawSum = _setPassSum = 0;
            _cpuSum = _gpuSum = 0;
            _samples = 0;
        }

        private long _triangleSum, _drawSum, _setPassSum;
        private double _cpuSum, _gpuSum;
        private int _samples;
        private readonly FrameTiming[] _timings = new FrameTiming[1];

        private double SampleGpuMs()
        {
            // Needs "Frame Timing Stats" in the player settings; 0 otherwise.
            FrameTimingManager.CaptureFrameTimings();
            return FrameTimingManager.GetLatestTimings(1, _timings) > 0 ? _timings[0].gpuFrameTime : 0;
        }

        private Result Summarise(string name, float loadSeconds, WorldManager world, long systemMemory, long gfxMemory)
        {
            var sorted = new List<float>(_frameMs);
            sorted.Sort();

            double total = 0;
            foreach (float ms in _frameMs)
                total += ms;

            float average = (float)(total / _frameMs.Count);
            float p99 = sorted[Mathf.Clamp(Mathf.CeilToInt(sorted.Count * 0.99f) - 1, 0, sorted.Count - 1)];

            // 1% low = average FPS of the slowest 1% of frames.
            int worstCount = Mathf.Max(1, sorted.Count / 100);
            double worstTotal = 0;
            for (int i = sorted.Count - worstCount; i < sorted.Count; i++)
                worstTotal += sorted[i];

            int samples = Mathf.Max(1, _samples);
            ShadowParameters shadows = GameSettings.GetShadowParameters(GameSettings.Shadows);

            return new Result
            {
                Name = name,
                Frames = _frameMs.Count,
                AverageFps = 1000f / average,
                OnePercentLowFps = (float)(1000.0 / (worstTotal / worstCount)),
                AverageMs = average,
                P99Ms = p99,
                WorstMs = sorted[sorted.Count - 1],
                CpuMs = (float)(_cpuSum / samples),
                GpuMs = (float)(_gpuSum / samples),
                Triangles = _triangleSum / samples,
                // Only counted in development builds (0 in release players).
                DrawCalls = _drawSum / samples,
                SetPassCalls = _setPassSum / samples,
                LoadSeconds = loadSeconds,
                Chunks = world.VisibleChunkCount,
                SimplifiedChunks = world.LodChunkCount,
                SystemMemoryMb = systemMemory / (1024 * 1024),
                GfxMemoryMb = gfxMemory / (1024 * 1024),
                Settings = $"view {GameSettings.RenderDistance}, shadows {GameSettings.Shadows} ({shadows.Distance:0} m x{shadows.Cascades} @{shadows.Resolution}), " +
                           $"grass {GameSettings.GrassDistance}, simplified beyond {GameSettings.SimplifyDistance}, clouds {(GameSettings.Clouds ? "on" : "off")}, wind {(GameSettings.Wind ? "on" : "off")}, " +
                           $"post {(GameSettings.PostProcessing ? "on" : "off")}, scale {GameSettings.RenderScale:0.##}, AA {GameSettings.AntiAliasing}"
            };
        }

        private string BuildSummary()
        {
            var text = new StringBuilder();
            text.AppendLine($"{SystemInfo.processorType} ({SystemInfo.processorCount} threads), {SystemInfo.graphicsDeviceName}, {Screen.width}x{Screen.height}");
            text.AppendLine();
            text.AppendLine("Preset    Avg FPS   1% low   Frame ms (avg/99%/worst)   CPU ms   GPU ms   Triangles   Draws   SetPass   Load s");

            foreach (Result r in _results)
            {
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-8} {1,8:0} {2,8:0}   {3,6:0.0} / {4,5:0.0} / {5,5:0.0}        {6,6:0.0}   {7,6}   {8,8:0.00}M   {9,5}   {10,7}   {11,6:0.0}",
                    r.Name, r.AverageFps, r.OnePercentLowFps, r.AverageMs, r.P99Ms, r.WorstMs,
                    r.CpuMs, r.GpuMs > 0 ? r.GpuMs.ToString("0.0", CultureInfo.InvariantCulture) : "n/a",
                    r.Triangles / 1e6, r.DrawCalls > 0 ? r.DrawCalls.ToString(CultureInfo.InvariantCulture) : "n/a",
                    r.SetPassCalls, r.LoadSeconds));
            }

            text.AppendLine();
            foreach (Result r in _results)
                text.AppendLine($"{r.Name}: {r.Settings}; {r.Chunks} chunks ({r.SimplifiedChunks} simplified)");

            if (_cancelled)
                text.AppendLine("(cancelled)");

            if (!string.IsNullOrEmpty(LastReportPath))
                text.AppendLine($"Saved to {LastReportPath}");

            return text.ToString();
        }

        private void WriteCsv()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath,
                    $"benchmark-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

                var csv = new StringBuilder();
                csv.AppendLine("preset,frames,avg_fps,low1_fps,avg_ms,p99_ms,worst_ms,cpu_ms,gpu_ms,triangles,draw_calls,setpass_calls,load_s,chunks,simplified_chunks,system_mb,gfx_mb,settings,cpu,gpu,resolution");

                foreach (Result r in _results)
                {
                    csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2:0.0},{3:0.0},{4:0.00},{5:0.00},{6:0.00},{7:0.00},{8:0.00},{9},{10},{11},{12:0.0},{13},{21},{14},{15},\"{16}\",\"{17}\",\"{18}\",{19}x{20}",
                        r.Name, r.Frames, r.AverageFps, r.OnePercentLowFps, r.AverageMs, r.P99Ms, r.WorstMs, r.CpuMs, r.GpuMs,
                        r.Triangles, r.DrawCalls, r.SetPassCalls, r.LoadSeconds, r.Chunks, r.SystemMemoryMb, r.GfxMemoryMb,
                        r.Settings, SystemInfo.processorType, SystemInfo.graphicsDeviceName, Screen.width, Screen.height, r.SimplifiedChunks));
                }

                File.WriteAllText(path, csv.ToString());
                LastReportPath = path;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not write the benchmark report: {exception.Message}");
            }
        }
    }
}

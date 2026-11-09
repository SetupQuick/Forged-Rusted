using UnityEngine;
using World.Blocks;

namespace World.Rendering
{
    /// <summary>
    /// What the camera sees with its eye under water:
    ///   - dense blue-green fog that gets darker and shorter the deeper
    ///     you are (light is absorbed by the water above you), and follows
    ///     the time of day (black-blue at night);
    ///   - less sunlight, with caustics on the ground (VoxelLit) and an
    ///     opaque surface seen from below (VoxelWater): bright straight up,
    ///     dark at shallow angles - never the sky;
    ///   - the sky itself shows the water colour (VoxelSky).
    /// Normal fog is restored when surfacing. Uses the camera's own
    /// position, so it switches exactly at the water surface.
    ///
    /// Added automatically by the WorldManager on machines that render.
    /// </summary>
    public sealed class UnderwaterEffect : MonoBehaviour
    {
        private static readonly int UnderwaterId = Shader.PropertyToID("_VoxelUnderwater");

        [Tooltip("Fog colour just below the surface at noon.")]
        [SerializeField]
        private Color waterColor = new Color(0.07f, 0.27f, 0.36f, 1f);

        [Tooltip("Fog colour deep down (or at night).")]
        [SerializeField]
        private Color deepColor = new Color(0.01f, 0.05f, 0.09f, 1f);

        [Tooltip("Depth below the surface where it is fully dark (metres).")]
        [SerializeField]
        [Min(1f)]
        private float darkDepth = 18f;

        [Tooltip("Distance where visibility starts to fade (metres).")]
        [SerializeField]
        [Min(0f)]
        private float fogStart = 0.5f;

        [Tooltip("Distance where everything is hidden, near the surface (metres).")]
        [SerializeField]
        [Min(1f)]
        private float fogEnd = 13f;

        [Tooltip("Distance where everything is hidden, deep down (metres).")]
        [SerializeField]
        [Min(1f)]
        private float deepFogEnd = 7f;

        [Tooltip("Sunlight reaching under the surface (0-1).")]
        [SerializeField]
        [Range(0f, 1f)]
        private float sunlight = 0.5f;

        private WorldManager _world;
        private bool _underwater;

        // Above-water settings to restore.
        private bool _savedFog;
        private FogMode _savedMode;
        private Color _savedColor;
        private float _savedStart;
        private float _savedEnd;
        private float _depth01;
        private float _surfaceY;

        /// <summary>True while the main camera is under water.</summary>
        public static bool CameraUnderwater { get; private set; }

        /// <summary>Sunlight multiplier while under water (read by VoxelAtmosphere).</summary>
        public static float SunlightFactor { get; private set; } = 1f;

        /// <summary>0 at the surface, 1 deep down (for audio, UI tint).</summary>
        public static float Depth01 { get; private set; }

        private void Awake()
        {
            _world = GetComponent<WorldManager>();
        }

        private void OnDisable()
        {
            if (_underwater)
                Exit();
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;
            bool underwater = false;

            if (camera != null && _world != null && _world.IsReady &&
                _world.TryGetLoadedBlockAt(camera.transform.position, out BlockId block))
            {
                underwater = BlockRegistry.IsLiquid(block);
            }

            if (underwater && !_underwater)
                Enter();
            else if (!underwater && _underwater)
                Exit();

            if (_underwater)
            {
                MeasureDepth(camera.transform.position);

                // Deeper = darker and murkier; at night the water is dark too.
                float light = Mathf.Lerp(0.15f, 1f, VoxelAtmosphere.Daylight);
                Color color = Color.Lerp(deepColor, waterColor * light, 1f - _depth01);
                color.a = 1f;

                // Keep it applied even if something else touched the fog
                // (e.g. render distance changed while diving).
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = color;
                RenderSettings.fogStartDistance = fogStart;
                RenderSettings.fogEndDistance = Mathf.Lerp(fogEnd, deepFogEnd, _depth01);

                camera.backgroundColor = color;
                SunlightFactor = sunlight * (1f - _depth01 * 0.8f);
                Depth01 = _depth01;
                Shader.SetGlobalVector(UnderwaterId, new Vector4(1f, _depth01, _surfaceY, 0f));
            }
        }

        /// <summary>Finds the water surface above the camera (loaded blocks only).</summary>
        private void MeasureDepth(Vector3 eye)
        {
            float voxel = _world.VoxelSize;
            float surface = eye.y;
            int maxSteps = Mathf.CeilToInt(darkDepth / voxel) + 2;

            for (int i = 1; i <= maxSteps; i++)
            {
                var probe = new Vector3(eye.x, eye.y + i * voxel, eye.z);

                if (!_world.TryGetLoadedBlockAt(probe, out BlockId above) || !BlockRegistry.IsLiquid(above))
                {
                    // Top of the last water voxel.
                    surface = Mathf.Floor((eye.y + (i - 1) * voxel) / voxel) * voxel + voxel;
                    break;
                }

                surface = probe.y;
            }

            _surfaceY = surface;
            float target = Mathf.Clamp01((surface - eye.y) / darkDepth);

            // Smooth so swimming up and down does not flicker.
            _depth01 = Mathf.MoveTowards(_depth01, target, Time.deltaTime * 0.5f);
        }

        private void Enter()
        {
            _underwater = true;
            CameraUnderwater = true;

            _savedFog = RenderSettings.fog;
            _savedMode = RenderSettings.fogMode;
            _savedColor = RenderSettings.fogColor;
            _savedStart = RenderSettings.fogStartDistance;
            _savedEnd = RenderSettings.fogEndDistance;

            SunlightFactor = sunlight;
            _depth01 = 0f;
        }

        private void Exit()
        {
            _underwater = false;
            CameraUnderwater = false;

            RenderSettings.fog = _savedFog;
            RenderSettings.fogMode = _savedMode;
            RenderSettings.fogColor = _savedColor;
            RenderSettings.fogStartDistance = _savedStart;
            RenderSettings.fogEndDistance = _savedEnd;

            SunlightFactor = 1f;
            Depth01 = 0f;
            Shader.SetGlobalVector(UnderwaterId, Vector4.zero);
        }
    }
}

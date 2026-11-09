using UnityEngine;
using World.Blocks;

namespace World.Rendering
{
    /// <summary>
    /// What the camera sees with its eye under water: short, dense,
    /// blue-green fog (things fade out within ~15 m), a slightly darker
    /// sun, and the normal fog restored when surfacing. Uses the camera's
    /// own position, so it switches exactly at the water surface.
    ///
    /// Added automatically by the WorldManager on machines that render.
    /// </summary>
    public sealed class UnderwaterEffect : MonoBehaviour
    {
        [SerializeField]
        private Color waterColor = new Color(0.1f, 0.34f, 0.44f, 1f);

        [Tooltip("Distance where visibility starts to fade (metres).")]
        [SerializeField]
        [Min(0f)]
        private float fogStart = 0.5f;

        [Tooltip("Distance where everything is hidden (metres).")]
        [SerializeField]
        [Min(1f)]
        private float fogEnd = 15f;

        [Tooltip("Sunlight reaching under the surface (0-1).")]
        [SerializeField]
        [Range(0f, 1f)]
        private float sunlight = 0.6f;

        private WorldManager _world;
        private bool _underwater;

        // Above-water settings to restore.
        private bool _savedFog;
        private FogMode _savedMode;
        private Color _savedColor;
        private float _savedStart;
        private float _savedEnd;
        private float _savedSunIntensity;
        private Light _sun;

        /// <summary>True while the main camera is under water.</summary>
        public static bool CameraUnderwater { get; private set; }

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
                // Keep it applied even if something else touched the fog
                // (e.g. render distance changed while diving).
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = waterColor;
                RenderSettings.fogStartDistance = fogStart;
                RenderSettings.fogEndDistance = fogEnd;
            }
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

            _sun = RenderSettings.sun;
            if (_sun != null)
            {
                _savedSunIntensity = _sun.intensity;
                _sun.intensity *= sunlight;
            }

            Camera camera = Camera.main;
            if (camera != null)
                camera.backgroundColor = waterColor;
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

            if (_sun != null)
                _sun.intensity = _savedSunIntensity;
        }
    }
}

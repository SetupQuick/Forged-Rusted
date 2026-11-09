using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace World.Rendering
{
    /// <summary>
    /// Lighting, sky and weather-ish animation for the small-voxel look,
    /// driven by the <see cref="WorldClock"/>:
    ///   - one directional light that is the warm sun by day and a dim,
    ///     cool moon by night (one shadow map at any time, never two);
    ///   - sky/ground hemisphere light for the voxel shaders, matching
    ///     ambient for everything else;
    ///   - the procedural sky (gradient, square sun/moon, stars) and a
    ///     distance-fog colour that matches the horizon;
    ///   - wind (direction slowly turning, gusts) for leaves, grass, water;
    ///   - shadow quality and post-processing from the graphics settings.
    ///
    /// Added automatically by <see cref="WorldManager"/> on machines that
    /// render, so scenes need no setup.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoxelAtmosphere : MonoBehaviour
    {
        private static readonly int SkyColorId = Shader.PropertyToID("_VoxelSkyColor");
        private static readonly int GroundColorId = Shader.PropertyToID("_VoxelGroundColor");
        private static readonly int AoDirectId = Shader.PropertyToID("_VoxelAoDirectStrength");
        private static readonly int SkyZenithId = Shader.PropertyToID("_VoxelSkyZenith");
        private static readonly int SkyHorizonId = Shader.PropertyToID("_VoxelSkyHorizon");
        private static readonly int SunDirectionId = Shader.PropertyToID("_VoxelSunDirection");
        private static readonly int MoonDirectionId = Shader.PropertyToID("_VoxelMoonDirection");
        private static readonly int SunDiscColorId = Shader.PropertyToID("_VoxelSunDiscColor");
        private static readonly int StarsId = Shader.PropertyToID("_VoxelStars");
        private static readonly int WindId = Shader.PropertyToID("_VoxelWind");

        // Colours are authored in sRGB (SetGlobalColor converts them).
        private static readonly Color DayZenith = new Color(0.30f, 0.53f, 0.92f);
        private static readonly Color DayHorizon = new Color(0.72f, 0.83f, 0.95f);
        private static readonly Color DuskZenith = new Color(0.24f, 0.30f, 0.52f);
        private static readonly Color DuskHorizon = new Color(0.98f, 0.58f, 0.36f);
        private static readonly Color NightZenith = new Color(0.012f, 0.018f, 0.045f);
        private static readonly Color NightHorizon = new Color(0.05f, 0.07f, 0.13f);

        private static readonly Color DaySky = new Color(0.62f, 0.74f, 0.98f);
        private static readonly Color DayGround = new Color(0.55f, 0.47f, 0.38f);
        private static readonly Color DuskSky = new Color(0.60f, 0.52f, 0.62f);
        private static readonly Color DuskGround = new Color(0.50f, 0.36f, 0.28f);
        private static readonly Color NightSky = new Color(0.16f, 0.20f, 0.34f);
        private static readonly Color NightGround = new Color(0.10f, 0.10f, 0.13f);

        private static readonly Color NoonSun = new Color(1f, 0.93f, 0.82f);
        private static readonly Color LowSun = new Color(1f, 0.62f, 0.36f);
        private static readonly Color MoonLight = new Color(0.66f, 0.74f, 0.92f);

        [Header("Sun and moon")]
        [SerializeField]
        private bool configureSun = true;

        [SerializeField]
        [Min(0f)]
        private float sunIntensity = 1.6f;

        [SerializeField]
        [Min(0f)]
        private float moonIntensity = 0.32f;

        [Tooltip("Compass direction of the sun's path (degrees).")]
        [SerializeField]
        private float sunAzimuth = -35f;

        [Tooltip("How far the sun's path leans from straight overhead (degrees).")]
        [SerializeField]
        [Range(0f, 60f)]
        private float sunTilt = 28f;

        [Tooltip("The light turns in steps of this many degrees. Moving it " +
                 "every frame makes shadow edges shimmer.")]
        [SerializeField]
        [Range(0f, 2f)]
        private float lightStepDegrees = 0.2f;

        [Header("Sky light")]
        [SerializeField]
        [Min(0f)]
        private float skyIntensity = 0.85f;

        [Tooltip("How much baked ambient occlusion also darkens sunlight (0-1).")]
        [SerializeField]
        [Range(0f, 1f)]
        private float aoOnSunlight = 0.4f;

        [Header("Shadows")]
        [SerializeField]
        private bool configureShadows = true;

        [SerializeField]
        [Min(0f)]
        private float shadowDistance = 100f;

        [Header("Post-processing")]
        [SerializeField]
        private bool postProcessing = true;

        [SerializeField]
        [Range(0f, 2f)]
        private float bloomIntensity = 0.45f;

        [SerializeField]
        [Range(-50f, 50f)]
        private float saturation = 10f;

        [SerializeField]
        [Range(-50f, 50f)]
        private float contrast = 12f;

        [SerializeField]
        [Range(-50f, 50f)]
        private float warmth = 8f;

        private Volume _volume;
        private Camera _configuredCamera;
        private Light _sun;
        private Material _skyMaterial;
        private int _shadowCascades = 4;
        private int _shadowResolution = 2048;
        private bool _softShadows = true;
        private AntialiasingMode _antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        private Vector3 _appliedLightDirection;
        private bool _lightIsMoon;

        /// <summary>0 = no wind animation (graphics setting), 1 = normal.</summary>
        public static float WindStrength { get; set; } = 1f;

        /// <summary>Current wind direction on the ground plane (for clouds, particles).</summary>
        public static Vector2 WindDirection { get; private set; } = new Vector2(0.8f, 0.6f);

        /// <summary>1 at noon, 0 at night (for sounds, mobs, UI).</summary>
        public static float Daylight { get; private set; } = 1f;

        /// <summary>Colour of the horizon right now (fog, clouds).</summary>
        public static Color HorizonColor { get; private set; } = DayHorizon;

        /// <summary>Shadow settings (graphics options). distance 0 = no shadows.</summary>
        public void SetShadows(float distance, int cascades, int resolution, bool soft)
        {
            shadowDistance = Mathf.Max(0f, distance);
            _shadowCascades = Mathf.Clamp(cascades, 1, 4);
            _shadowResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(resolution), 256, 4096);
            _softShadows = soft;

            ApplyShadowType();

            if (configureShadows)
                ConfigureShadows();
        }

        /// <summary>Shadow distance in metres; 0 turns sun shadows off.</summary>
        public void SetShadowDistance(float metres)
        {
            SetShadows(metres, _shadowCascades, _shadowResolution, _softShadows);
        }

        /// <summary>Turns bloom, tone mapping and colour grading on or off (graphics settings).</summary>
        public void SetPostProcessing(bool enabled)
        {
            postProcessing = enabled;

            if (enabled)
                ConfigurePostProcessing();

            if (_volume != null)
                _volume.enabled = enabled;

            if (Camera.main != null)
                ConfigureCamera(Camera.main);
        }

        public void SetAntialiasing(AntialiasingMode mode)
        {
            _antialiasing = mode;

            if (Camera.main != null)
                ConfigureCamera(Camera.main);
        }

        private void OnEnable()
        {
            Shader.SetGlobalFloat(AoDirectId, Mathf.Max(0.001f, aoOnSunlight));

            if (configureSun)
                FindOrCreateSun();

            if (configureShadows)
                ConfigureShadows();

            if (postProcessing)
                ConfigurePostProcessing();

            _skyMaterial = Resources.Load<Material>("World/SkyMaterial");
            if (_skyMaterial != null)
                RenderSettings.skybox = _skyMaterial;

            // Ambient is set from code every frame; no baked environment.
            RenderSettings.ambientMode = AmbientMode.Trilight;

            UpdateAtmosphere(force: true);
        }

        private void OnValidate()
        {
            if (Application.isPlaying && isActiveAndEnabled)
                UpdateAtmosphere(force: true);
        }

        private void Update()
        {
            // The player camera appears when the local player spawns.
            if (Camera.main != null && Camera.main != _configuredCamera)
                ConfigureCamera(Camera.main);

            UpdateAtmosphere(force: false);
        }

        private void OnDestroy()
        {
            if (_volume != null)
                Destroy(_volume.gameObject);

            Shader.SetGlobalColor(SkyColorId, Color.clear);
            Shader.SetGlobalColor(SkyZenithId, Color.clear);
            Shader.SetGlobalColor(SkyHorizonId, Color.clear);

            if (RenderSettings.skybox == _skyMaterial)
                RenderSettings.skybox = null;
        }

        // ------------------------------------------------------------
        // Day / night
        // ------------------------------------------------------------

        /// <summary>Direction towards the sun for a time of day (0..1).</summary>
        public Vector3 SunDirection(float timeOfDay)
        {
            // Rises in the east at 0.25, highest at 0.5, sets at 0.75.
            float angle = (timeOfDay - 0.25f) * Mathf.PI * 2f;
            float tilt = sunTilt * Mathf.Deg2Rad;

            var direction = new Vector3(
                Mathf.Cos(angle),
                Mathf.Sin(angle) * Mathf.Cos(tilt),
                Mathf.Sin(angle) * Mathf.Sin(tilt));

            return Quaternion.Euler(0f, sunAzimuth, 0f) * direction;
        }

        private void UpdateAtmosphere(bool force)
        {
            float time = WorldClock.TimeOfDay;
            Vector3 sun = SunDirection(time);
            Vector3 moon = -sun;

            // 1 when the sun is well up, 0 at night; dusk peaks at sunrise/sunset.
            float day = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, 0.22f, sun.y));
            float dusk = Mathf.Clamp01(1f - Mathf.Abs(sun.y + 0.02f) / 0.22f);
            dusk *= dusk;
            Daylight = day;

            // Sky gradient.
            Color zenith = Color.Lerp(NightZenith, DayZenith, day);
            Color horizon = Color.Lerp(NightHorizon, DayHorizon, day);
            zenith = Color.Lerp(zenith, DuskZenith, dusk * 0.6f);
            horizon = Color.Lerp(horizon, DuskHorizon, dusk * 0.75f);
            zenith.a = horizon.a = 1f;
            HorizonColor = horizon;

            Shader.SetGlobalColor(SkyZenithId, zenith);
            Shader.SetGlobalColor(SkyHorizonId, horizon);
            Shader.SetGlobalVector(SunDirectionId, new Vector4(sun.x, sun.y, sun.z, Mathf.Clamp01(sun.y * 8f + 1f)));
            Shader.SetGlobalVector(MoonDirectionId, new Vector4(moon.x, moon.y, moon.z, Mathf.Clamp01(1f - day * 1.4f)));
            Shader.SetGlobalColor(SunDiscColorId, Color.Lerp(LowSun, NoonSun, Mathf.Clamp01(sun.y * 3f)));
            Shader.SetGlobalFloat(StarsId, Mathf.Clamp01(1f - day * 2.5f));

            // Hemisphere light.
            Color sky = Color.Lerp(NightSky, DaySky, day);
            Color ground = Color.Lerp(NightGround, DayGround, day);
            sky = Color.Lerp(sky, DuskSky, dusk * 0.5f) * skyIntensity;
            ground = Color.Lerp(ground, DuskGround, dusk * 0.5f) * skyIntensity;
            sky.a = ground.a = 1f;

            Shader.SetGlobalColor(SkyColorId, sky);
            Shader.SetGlobalColor(GroundColorId, ground);

            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = Color.Lerp(sky, ground, 0.5f);
            RenderSettings.ambientGroundColor = ground;

            // Distance fog melts into the horizon (the underwater effect
            // replaces it while diving).
            if (!UnderwaterEffect.CameraUnderwater)
                RenderSettings.fogColor = horizon;

            UpdateLight(sun, moon, day, force);
            UpdateWind();
        }

        private void UpdateLight(Vector3 sun, Vector3 moon, float day, bool force)
        {
            if (!configureSun)
                return;

            if (_sun == null)
                FindOrCreateSun();

            // The sun lights the world while it is above the horizon, the
            // moon otherwise. Both are faint near the horizon, so the
            // switch is invisible.
            bool useMoon = sun.y < -0.04f;
            Vector3 direction = useMoon ? moon : sun;
            float elevation = Mathf.Clamp01(direction.y / 0.25f);

            Color color;
            float intensity;

            if (useMoon)
            {
                color = MoonLight;
                intensity = moonIntensity * elevation;
            }
            else
            {
                color = Color.Lerp(LowSun, NoonSun, Mathf.Clamp01(sun.y * 2.5f));
                intensity = sunIntensity * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.04f, 0.18f, sun.y));
            }

            if (UnderwaterEffect.CameraUnderwater)
                intensity *= UnderwaterEffect.SunlightFactor;

            _sun.color = color;
            _sun.intensity = intensity;
            _sun.shadowStrength = Mathf.Lerp(0.5f, 0.85f, day) * Mathf.Clamp01(intensity * 6f);

            // No shadow pass at all while the light is too faint to see them.
            bool shadows = shadowDistance > 0f && intensity > 0.03f;
            LightShadows type = shadows ? (_softShadows ? LightShadows.Soft : LightShadows.Hard) : LightShadows.None;
            if (_sun.shadows != type)
                _sun.shadows = type;

            float step = Mathf.Max(0.001f, lightStepDegrees);
            if (force || useMoon != _lightIsMoon || Vector3.Angle(direction, _appliedLightDirection) >= step)
            {
                _appliedLightDirection = direction;
                _lightIsMoon = useMoon;
                _sun.transform.rotation = Quaternion.LookRotation(-direction, Vector3.up);
            }
        }

        private void UpdateWind()
        {
            float t = (float)(WorldClock.WorldSeconds % 7200.0);

            // Direction wanders slowly; strength breathes over minutes.
            float heading = 0.6f + Mathf.Sin(t * 0.0021f) * 0.9f + Mathf.Sin(t * 0.0057f) * 0.35f;
            WindDirection = new Vector2(Mathf.Cos(heading), Mathf.Sin(heading));
            float strength = WindStrength * (0.7f + 0.3f * Mathf.Sin(t * 0.013f));

            // Time is always sent (caustics and water use it even without wind).
            Shader.SetGlobalVector(WindId, new Vector4(WindDirection.x, WindDirection.y, strength, t));
        }

        // ------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------

        private void FindOrCreateSun()
        {
            Light sun = RenderSettings.sun;

            if (sun == null)
            {
                foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (light.type == LightType.Directional)
                    {
                        sun = light;
                        break;
                    }
                }
            }

            if (sun == null)
            {
                var go = new GameObject("Sun");
                sun = go.AddComponent<Light>();
                sun.type = LightType.Directional;
            }

            RenderSettings.sun = sun;
            _sun = sun;
            ApplyShadowType();
        }

        private void ApplyShadowType()
        {
            if (_sun != null)
                _sun.shadows = shadowDistance > 0f ? (_softShadows ? LightShadows.Soft : LightShadows.Hard) : LightShadows.None;
        }

        private void ConfigureShadows()
        {
            UniversalRenderPipelineAsset urp = RuntimePipeline.Asset;
            if (urp == null)
                return;

            urp.shadowDistance = shadowDistance;
            urp.shadowCascadeCount = _shadowCascades;
            RuntimePipeline.SetMainLightShadowResolution(_shadowResolution);
        }

        private void ConfigurePostProcessing()
        {
            if (_volume == null)
            {
                var go = new GameObject("Voxel Post Processing");
                go.transform.SetParent(transform, false);

                _volume = go.AddComponent<Volume>();
                _volume.isGlobal = true;
                _volume.priority = 10f;
                _volume.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            }

            VolumeProfile profile = _volume.sharedProfile;

            Tonemapping tonemapping = GetOrAdd<Tonemapping>(profile);
            tonemapping.mode.Override(TonemappingMode.ACES);

            Bloom bloom = GetOrAdd<Bloom>(profile);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(bloomIntensity);
            bloom.scatter.Override(0.65f);

            ColorAdjustments color = GetOrAdd<ColorAdjustments>(profile);
            color.postExposure.Override(0.15f);
            color.contrast.Override(contrast);
            color.saturation.Override(saturation);

            WhiteBalance whiteBalance = GetOrAdd<WhiteBalance>(profile);
            whiteBalance.temperature.Override(warmth);

            Vignette vignette = GetOrAdd<Vignette>(profile);
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.5f);

            if (Camera.main != null)
                ConfigureCamera(Camera.main);
        }

        private void ConfigureCamera(Camera camera)
        {
            _configuredCamera = camera;
            camera.clearFlags = CameraClearFlags.Skybox;

            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();

            if (data != null)
            {
                data.renderPostProcessing = postProcessing;
                data.antialiasing = _antialiasing;

                // Nothing in the game reads these; skipping them saves a
                // full-screen copy each frame.
                data.requiresColorOption = CameraOverrideOption.Off;
                data.requiresDepthOption = CameraOverrideOption.Off;
            }
        }

        private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T component))
                component = profile.Add<T>(true);

            component.active = true;
            return component;
        }
    }
}

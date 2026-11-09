using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace World.Rendering
{
    /// <summary>
    /// Lighting and post-processing for the small-voxel look: warm sun
    /// with soft shadows, sky/ground hemisphere light (read by the voxel
    /// shaders), filmic tone mapping, gentle bloom and colour grading.
    ///
    /// Added automatically by <see cref="WorldManager"/> on machines that
    /// render, so scenes need no setup. Every part can be switched off
    /// if a scene wants to do its own lighting.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoxelAtmosphere : MonoBehaviour
    {
        private static readonly int SkyColorId = Shader.PropertyToID("_VoxelSkyColor");
        private static readonly int GroundColorId = Shader.PropertyToID("_VoxelGroundColor");
        private static readonly int AoDirectId = Shader.PropertyToID("_VoxelAoDirectStrength");

        [Header("Sun")]
        [SerializeField]
        private bool configureSun = true;

        [SerializeField]
        private Color sunColor = new Color(1f, 0.9f, 0.76f);

        [SerializeField]
        [Min(0f)]
        private float sunIntensity = 1.6f;

        [Tooltip("Sun elevation and direction (degrees).")]
        [SerializeField]
        private Vector2 sunAngles = new Vector2(42f, -35f);

        [Header("Sky light")]
        [Tooltip("Light from the sky on upward faces.")]
        [SerializeField]
        private Color skyColor = new Color(0.62f, 0.74f, 0.98f);

        [Tooltip("Warm bounce light on downward faces.")]
        [SerializeField]
        private Color groundColor = new Color(0.55f, 0.47f, 0.38f);

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
        [Min(10f)]
        private float shadowDistance = 110f;

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

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            if (Application.isPlaying && isActiveAndEnabled)
                Apply();
        }

        private void Update()
        {
            // The player camera appears when the local player spawns.
            if (postProcessing && Camera.main != null && Camera.main != _configuredCamera)
                ConfigureCamera(Camera.main);
        }

        private void OnDestroy()
        {
            if (_volume != null)
                Destroy(_volume.gameObject);

            Shader.SetGlobalColor(SkyColorId, Color.clear);
        }

        private void Apply()
        {
            Color sky = skyColor * skyIntensity;
            sky.a = 1f;
            Color ground = groundColor * skyIntensity;
            ground.a = 1f;

            Shader.SetGlobalColor(SkyColorId, sky);
            Shader.SetGlobalColor(GroundColorId, ground);
            Shader.SetGlobalFloat(AoDirectId, Mathf.Max(0.001f, aoOnSunlight));

            // Same light for non-voxel objects (players, items).
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = Color.Lerp(sky, ground, 0.5f);
            RenderSettings.ambientGroundColor = ground;

            if (configureSun)
                ConfigureSun();

            if (configureShadows)
                ConfigureShadows();

            if (postProcessing)
                ConfigurePostProcessing();
        }

        private void ConfigureSun()
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
                RenderSettings.sun = sun;
            }

            sun.color = sunColor;
            sun.intensity = sunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.transform.rotation = Quaternion.Euler(sunAngles.x, sunAngles.y, 0f);
        }

        private void ConfigureShadows()
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.shadowDistance = shadowDistance;
                urp.shadowCascadeCount = 4;
            }
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

            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();

            if (data != null)
            {
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
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

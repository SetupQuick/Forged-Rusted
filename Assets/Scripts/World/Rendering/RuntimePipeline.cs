using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace World.Rendering
{
    /// <summary>
    /// A private copy of the active URP asset that graphics settings may
    /// change (shadow distance/cascades/resolution, render scale, MSAA).
    ///
    /// Changing the real asset at runtime would, in the editor, save the
    /// player's options into the project file - so the first change
    /// clones it and makes the clone the active pipeline for the current
    /// quality level. After a quality level switch a new clone is made.
    /// </summary>
    public static class RuntimePipeline
    {
        private static UniversalRenderPipelineAsset _clone;
        private static RenderPipelineAsset _source;

        public static UniversalRenderPipelineAsset Asset
        {
            get
            {
                RenderPipelineAsset current = QualitySettings.renderPipeline != null
                    ? QualitySettings.renderPipeline
                    : GraphicsSettings.defaultRenderPipeline;

                if (_clone != null && current == _clone)
                    return _clone;

                if (!(current is UniversalRenderPipelineAsset urp))
                    return null;

                _source = urp;
                _clone = Object.Instantiate(urp);
                _clone.name = urp.name + " (runtime)";
                QualitySettings.renderPipeline = _clone;

                return _clone;
            }
        }

        private static FieldInfo _shadowResolutionField;

        /// <summary>
        /// Main light shadow map size. URP only exposes a getter, so the
        /// serialized field of the runtime copy is set directly. Returns
        /// false if this URP version stores it differently.
        /// </summary>
        public static bool SetMainLightShadowResolution(int resolution)
        {
            UniversalRenderPipelineAsset urp = Asset;
            if (urp == null)
                return false;

            if (urp.mainLightShadowmapResolution == resolution)
                return true;

            try
            {
                if (_shadowResolutionField == null)
                    _shadowResolutionField = typeof(UniversalRenderPipelineAsset).GetField(
                        "m_MainLightShadowmapResolution", BindingFlags.Instance | BindingFlags.NonPublic);

                if (_shadowResolutionField == null)
                    return false;

                _shadowResolutionField.SetValue(urp, System.Enum.ToObject(_shadowResolutionField.FieldType, resolution));
                return true;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"Could not set the shadow resolution: {exception.Message}");
                return false;
            }
        }

        /// <summary>The project's own asset (unchanged), for reference.</summary>
        public static RenderPipelineAsset Source => _source;
    }
}

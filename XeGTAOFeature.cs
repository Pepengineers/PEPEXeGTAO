using PEPEngineers.Data;
using PEPEngineers.Passes;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PEPEngineers
{
    [DisallowMultipleRendererFeature("XeGTAO")]
    [Tooltip("XeGTAO")]
    [SupportedOnRenderer(typeof(UniversalRendererData))]
    internal sealed class XeGTAOFeature : ScriptableRendererFeature
    {
        [SerializeField] private XeGtaoSettings settings;
        
        private XeGTAOPass pass;

        public override void Create()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                Debug.LogWarning("Device does not support compute shaders. The pass will be skipped.");
                return;
            }

            var resource = GraphicsSettings.GetRenderPipelineSettings<XeGTAORuntimeShaders>();
            // Skip the render pass if the compute shader is null.
            if (resource.DenoiseCS == null || resource.MainPassCS == null || resource.PrefilterDepthsCS == null)
            {
                Debug.LogWarning("Any shader is null. The pass will be skipped.");
                return;
            }

            pass = new XeGTAOPass(settings, RenderPassEvent.AfterRenderingGbuffer);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (pass == null)
            {
                Debug.LogWarning("Pass is null. The pass will be skipped.");
                return;
            }
            
            if (renderingData.cameraData.cameraType != CameraType.Game &&
                renderingData.cameraData.cameraType != CameraType.SceneView) return;
            if (renderingData.cameraData.requiresDepthTexture == false ||
                renderingData.cameraData.requiresOpaqueTexture == false)
            {
                Debug.LogWarning("Depth Texture or Opaque Texture was not enabled");
                return;
            }

            renderer.EnqueuePass(pass);
        }
    }
}
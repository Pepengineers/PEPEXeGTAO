using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

namespace PEPEngineers.Components
{
    [Serializable]
    [VolumeComponentMenu(@"XeGTAO/Occlusion Modifiers")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    internal sealed class XeGtaoModifiersComponent : VolumeComponent
    {
        [FormerlySerializedAs("FinalValuePower")] public ClampedFloatParameter finalValuePower = new(1.0f, 0.0f, 5.0f);
        [FormerlySerializedAs("FalloffRange")] public ClampedFloatParameter falloffRange = new(0.1f, 0.0f, 10.0f);
        [FormerlySerializedAs("Intensity")] public MinFloatParameter intensity = new(1f, 0.0f);
        [FormerlySerializedAs("Radius")] public MinFloatParameter radius = new(0.5f, 0.0f);

        public XeGtaoModifiersComponent()
        {
            displayName = "XeGTAO Modifiers";
        }
    }
}
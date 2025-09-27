using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace PEPEngineers.Data
{
    [Serializable]
    internal sealed class XeGtaoSettings
    {
        [FormerlySerializedAs("Resolution")] public XeGTAOResolution resolution = XeGTAOResolution.Full;
        [FormerlySerializedAs("QualityLevel")] public XeGTAOQualityLevel qualityLevel = XeGTAOQualityLevel.High;
        [FormerlySerializedAs("DenoisingLevel")] public XeGTAODenoisingLevel denoisingLevel = XeGTAODenoisingLevel.Sharp;
        [FormerlySerializedAs("BentNormals")] public bool bentNormals;
        [FormerlySerializedAs("DirectLightingStrength")] [Range(0, 1)] public float directLightingStrength = 0.3f;
    }
}
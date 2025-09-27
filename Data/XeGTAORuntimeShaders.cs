using System;
using UnityEngine;
using UnityEngine.Categorization;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PEPEngineers.Data
{
	[Serializable]
	[SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
	[CategoryInfo(Name = "R: XeGTAO Runtime Shaders", Order = 1500)]
	internal sealed class XeGTAORuntimeShaders : IRenderPipelineResources
	{
		private const string BaseResourcePath = "Shaders/";

		[SerializeField] [HideInInspector] private int currentVersion = 2;

		[SerializeField] [ResourcePath(BaseResourcePath + "XeGTAO_PrefilterDepths16x16.compute")]
		private ComputeShader prefilterDepthsCs;

		[SerializeField] [ResourcePath(BaseResourcePath + "XeGTAO_MainPass.compute")]
		private ComputeShader mainPassCs;

		[SerializeField] [ResourcePath(BaseResourcePath + "XeGTAO_Denoise.compute")]
		private ComputeShader denoiseCs;

		[SerializeField] [ResourcePath(BaseResourcePath + "XeGTAO_Composite.compute")]
		private ComputeShader compositeCs;

		public ref readonly ComputeShader PrefilterDepthsCS => ref prefilterDepthsCs;

		public ref readonly ComputeShader MainPassCS => ref mainPassCs;

		public ref readonly ComputeShader DenoiseCS => ref denoiseCs;

		public ref readonly ComputeShader CompositeCS => ref compositeCs;

		public int version => currentVersion;

		bool IRenderPipelineGraphicsSettings.isAvailableInPlayerBuild => true;
	}
}
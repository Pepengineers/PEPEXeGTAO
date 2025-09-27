using System.Diagnostics.CodeAnalysis;
using PEPEngineers.Components;
using PEPEngineers.Data;
using PEPEngineers.ThirdParty;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PEPEngineers.Passes
{
	public static class RenderExtensions
    {
        public static TextureDesc CreateTextureDesc(string name, RenderTextureDescriptor input)
        {
            return new TextureDesc(input.width, input.height)
            {
                format = input.graphicsFormat,
                dimension = input.dimension,
                slices = input.volumeDepth,
                name = name,
                useMipMap = input.useMipMap,
                enableRandomWrite = input.enableRandomWrite
            };
        }

        public static int AlignUp(int value, int alignment)
        {
            if (alignment == 0) return value;
            return (value + alignment - 1) & -alignment;
        }

        public static int2 AlignUp(int2 value, int2 alignment)
        {
            return math.select(value, (value + alignment - 1) & -alignment, alignment != 0);
        }

        public static int3 AlignUp(int3 value, int3 alignment)
        {
            return math.select(value, (value + alignment - 1) & -alignment, alignment != 0);
        }
    }
	
	internal sealed class XeGTAOPass : ScriptableRenderPass
	{
		private readonly BaseRenderFunc<PassData, UnsafeGraphContext> renderFunc;

		private readonly GlobalKeyword screenSpaceOcclusion = GlobalKeyword.Create(ShaderKeywordStrings.ScreenSpaceOcclusion);

		private readonly XeGtaoSettings settings;
		private readonly XeGTAORuntimeShaders shaders;

		public XeGTAOPass(XeGtaoSettings settings, RenderPassEvent evt)
		{
			this.settings = settings;
			renderPassEvent = evt;

			shaders = GraphicsSettings.GetRenderPipelineSettings<XeGTAORuntimeShaders>();
			renderFunc = Render;
		}

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{
			var resourceData = frameData.Get<UniversalResourceData>();
			var cameraData = frameData.Get<UniversalCameraData>();
			var xeGtaoSettings = settings;

			using var builder = renderGraph.AddUnsafePass("XeGTAO", out PassData passData);
			builder.AllowPassCulling(false);
			builder.AllowGlobalStateModification(true);
			builder.UseAllGlobalTextures(true);

			builder.UseTexture(resourceData.cameraDepthTexture);
			passData.SrcRawDepth = resourceData.cameraDepthTexture;
			builder.UseTexture(resourceData.cameraNormalsTexture);

			var resolutionScale = (int)xeGtaoSettings.resolution;
			var renderResolution = math.int2(cameraData.scaledWidth, cameraData.scaledHeight);
			passData.Resolution = math.max(1, renderResolution / resolutionScale);
			passData.ResolutionScale = math.float4((float2)passData.Resolution / renderResolution, 0, 0);

			{
				var textureDesc = RenderExtensions.CreateTextureDesc(nameof(PassData.WorkingDepths), cameraData.cameraTargetDescriptor);
				textureDesc.clearBuffer = false;
				textureDesc.name = nameof(PassData.WorkingDepths);
				textureDesc.enableRandomWrite = true;
				textureDesc.format = GraphicsFormat.R32_SFloat;
				textureDesc.width = passData.Resolution.x;
				textureDesc.height = passData.Resolution.y;

				for (var mipIndex = 0; mipIndex < ThirdPartyXeGTAO.XE_GTAO_DEPTH_MIP_LEVELS; mipIndex++)
				{
					var mipDesc = textureDesc;
					if (mipIndex == 0) mipDesc.useMipMap = true;
					mipDesc.width >>= mipIndex;
					mipDesc.height >>= mipIndex;
					passData.WorkingDepths[mipIndex] = builder.CreateTransientTexture(mipDesc);
					builder.UseTexture(passData.WorkingDepths[mipIndex], AccessFlags.ReadWrite);
				}
			}

			passData.OutputBentNormals = xeGtaoSettings.bentNormals;

			{
				var textureDesc = RenderExtensions.CreateTextureDesc(nameof(PassData.AOTerm), cameraData.cameraTargetDescriptor);
				textureDesc.clearBuffer = false;
				textureDesc.enableRandomWrite = true;
				textureDesc.format = passData.OutputBentNormals ? GraphicsFormat.R32_UInt : GraphicsFormat.R8_UInt;
				textureDesc.width = passData.Resolution.x;
				textureDesc.height = passData.Resolution.y;
				passData.AOTerm = builder.CreateTransientTexture(textureDesc);
				builder.UseTexture(passData.AOTerm, AccessFlags.ReadWrite);

				textureDesc.name = nameof(PassData.AOTermPong);
				passData.AOTermPong = builder.CreateTransientTexture(textureDesc);
				builder.UseTexture(passData.AOTermPong, AccessFlags.ReadWrite);

				textureDesc.name = nameof(PassData.FinalAOTerm);
				passData.FinalAOTerm = builder.CreateTransientTexture(textureDesc);
				builder.UseTexture(passData.FinalAOTerm, AccessFlags.ReadWrite);

				textureDesc.format = GraphicsFormat.R8_UNorm;
				textureDesc.name = nameof(PassData.Edges);
				passData.Edges = builder.CreateTransientTexture(textureDesc);
				builder.UseTexture(passData.Edges, AccessFlags.ReadWrite);

				textureDesc.name = nameof(ShaderIDs.Global._ScreenSpaceOcclusionTexture);
				passData.AOFinal = renderGraph.CreateTexture(textureDesc);
				builder.UseTexture(passData.AOFinal, AccessFlags.ReadWrite);
				resourceData.ssaoTexture = passData.AOFinal;

				builder.SetGlobalTextureAfterPass(passData.AOFinal, ShaderIDs.Global._ScreenSpaceOcclusionTexture);
			}

			passData.Settings = ThirdPartyXeGTAO.XeGTAOSettings.Default;
			passData.Settings.QualityLevel = (int)xeGtaoSettings.qualityLevel;
			passData.Settings.DenoisePasses = (int)xeGtaoSettings.denoisingLevel;
			passData.Intensity = 1;

			var stack = VolumeManager.instance.stack;
			var gtaoComponent = stack.GetComponent<XeGtaoModifiersComponent>();
			if (gtaoComponent && gtaoComponent.active)
			{
				if (gtaoComponent.finalValuePower.overrideState) passData.Settings.FinalValuePower *= gtaoComponent.finalValuePower.value;
				if (gtaoComponent.falloffRange.overrideState) passData.Settings.FalloffRange *= gtaoComponent.falloffRange.value;
				if (gtaoComponent.intensity.overrideState) passData.Intensity = gtaoComponent.intensity.value;
				if (gtaoComponent.radius.overrideState) passData.Settings.Radius = gtaoComponent.radius.value;
			}

			passData.Constants.DirectLightingStrength = settings.directLightingStrength;

			const bool rowMajor = false;
			const uint frameCounter = 0;

			// Unity view-space Z is negated.
			// Not sure why negative Y is necessary here
			var viewCorrectionMatrix = Matrix4x4.Scale(new Vector3(1, -1, -1));
			var gpuProjectionMatrix = cameraData.GetGPUProjectionMatrix(true, 1);
			ThirdPartyXeGTAO.XeGTAOSettings.UpdateConstants(ref passData.Constants, passData.Resolution.x,
				passData.Resolution.y, passData.Settings,
				gpuProjectionMatrix * viewCorrectionMatrix, rowMajor, frameCounter
			);

			builder.SetRenderFunc(renderFunc);
		}

		private void Render(PassData data, UnsafeGraphContext context)
		{
			var unsafeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
			using (new ProfilingScope(context.cmd, Profiling.PrefilterDepths))
			{
				const int kernelIndex = 0;
				const int threadGroupSizeDim = 16;
				var threadGroupsX = RenderExtensions.AlignUp(data.Resolution.x, threadGroupSizeDim) / threadGroupSizeDim;
				var threadGroupsY = RenderExtensions.AlignUp(data.Resolution.y, threadGroupSizeDim) / threadGroupSizeDim;
				context.cmd.SetComputeTextureParam(shaders.PrefilterDepthsCS, kernelIndex, ShaderIDs.PrefilterDepths.g_srcRawDepth, data.SrcRawDepth);
				context.cmd.SetComputeTextureParam(shaders.PrefilterDepthsCS, kernelIndex, ShaderIDs.PrefilterDepths.g_outWorkingDepthMIP0,
					data.WorkingDepths[0]);
				context.cmd.SetComputeTextureParam(shaders.PrefilterDepthsCS, kernelIndex, ShaderIDs.PrefilterDepths.g_outWorkingDepthMIP1,
					data.WorkingDepths[1]);
				context.cmd.SetComputeTextureParam(shaders.PrefilterDepthsCS, kernelIndex,
					ShaderIDs.PrefilterDepths.g_outWorkingDepthMIP2, data.WorkingDepths[2]);
				context.cmd.SetComputeTextureParam(shaders.PrefilterDepthsCS, kernelIndex,
					ShaderIDs.PrefilterDepths.g_outWorkingDepthMIP3, data.WorkingDepths[3]);
				context.cmd.SetComputeTextureParam(shaders.PrefilterDepthsCS, kernelIndex,
					ShaderIDs.PrefilterDepths.g_outWorkingDepthMIP4, data.WorkingDepths[4]);
				ConstantBuffer.Push(data.Constants, shaders.PrefilterDepthsCS,
					Shader.PropertyToID(nameof(ThirdPartyXeGTAO.XeGTAOConstantsCS)));

				context.cmd.DispatchCompute(shaders.PrefilterDepthsCS, kernelIndex, threadGroupsX, threadGroupsY, 1);

				for (var mipIndex = 1; mipIndex < ThirdPartyXeGTAO.XE_GTAO_DEPTH_MIP_LEVELS; mipIndex++)
					unsafeCmd.CopyTexture(data.WorkingDepths[mipIndex], 0, 0, data.WorkingDepths[0], 0, mipIndex);
			}

			using (new ProfilingScope(context.cmd, Profiling.MainPass))
			{
				CoreUtils.SetKeyword(shaders.MainPassCS, Keywords.XE_GTAO_COMPUTE_BENT_NORMALS, data.OutputBentNormals);

				var kernelIndex = data.Settings.QualityLevel;
				var threadGroupsX = RenderExtensions.AlignUp(data.Resolution.x, ThirdPartyXeGTAO.XE_GTAO_NUMTHREADS_X) /
				                    ThirdPartyXeGTAO.XE_GTAO_NUMTHREADS_X;
				var threadGroupsY = RenderExtensions.AlignUp(data.Resolution.y, ThirdPartyXeGTAO.XE_GTAO_NUMTHREADS_Y) /
				                    ThirdPartyXeGTAO.XE_GTAO_NUMTHREADS_Y;

				context.cmd.SetComputeTextureParam(shaders.MainPassCS, kernelIndex, ShaderIDs.MainPass.g_srcWorkingDepth,
					data.WorkingDepths[0]);
				context.cmd.SetComputeTextureParam(shaders.MainPassCS, kernelIndex, ShaderIDs.MainPass.g_outWorkingAOTerm,
					data.AOTerm);
				context.cmd.SetComputeTextureParam(shaders.MainPassCS, kernelIndex, ShaderIDs.MainPass.g_outWorkingEdges,
					data.Edges);
				ConstantBuffer.Push(data.Constants, shaders.MainPassCS,
					Shader.PropertyToID(nameof(ThirdPartyXeGTAO.XeGTAOConstantsCS)));

				context.cmd.DispatchCompute(shaders.MainPassCS, kernelIndex, threadGroupsX, threadGroupsY, 1);
			}

			using (new ProfilingScope(context.cmd, Profiling.Denoise))
			{
				CoreUtils.SetKeyword(shaders.DenoiseCS, Keywords.XE_GTAO_COMPUTE_BENT_NORMALS, data.OutputBentNormals);

				var passCount = math.max(1, data.Settings.DenoisePasses);
				for (var passIndex = 0; passIndex < passCount; passIndex++)
				{
					var isLastPass = passIndex == passCount - 1;
					var kernelIndex = isLastPass ? 1 : 0;

					var threadGroupsX =
						RenderExtensions.AlignUp(data.Resolution.x, ThirdPartyXeGTAO.XE_GTAO_NUMTHREADS_X * 2) /
						ThirdPartyXeGTAO.XE_GTAO_NUMTHREADS_X;
					var threadGroupsY =
						RenderExtensions.AlignUp(data.Resolution.y, ThirdPartyXeGTAO.XE_GTAO_NUMTHREADS_Y) /
						ThirdPartyXeGTAO.XE_GTAO_NUMTHREADS_Y;

					context.cmd.SetComputeTextureParam(shaders.DenoiseCS, kernelIndex, ShaderIDs.Denoise.g_srcWorkingAOTerm,
						data.AOTerm);
					context.cmd.SetComputeTextureParam(shaders.DenoiseCS, kernelIndex, ShaderIDs.Denoise.g_srcWorkingEdges,
						data.Edges);
					context.cmd.SetComputeTextureParam(shaders.DenoiseCS, kernelIndex, ShaderIDs.Denoise.g_outFinalAOTerm,
						isLastPass ? data.FinalAOTerm : data.AOTermPong);

					ConstantBuffer.Push(data.Constants, shaders.DenoiseCS,
						Shader.PropertyToID(nameof(ThirdPartyXeGTAO.XeGTAOConstantsCS)));

					context.cmd.DispatchCompute(shaders.DenoiseCS, kernelIndex, threadGroupsX, threadGroupsY, 1);
					(data.AOTerm, data.AOTermPong) = (data.AOTermPong, data.AOTerm);
				}
			}

			using (new ProfilingScope(context.cmd, Profiling.Composite))
			{
				CoreUtils.SetKeyword(shaders.CompositeCS, Keywords.XE_GTAO_COMPUTE_BENT_NORMALS, data.OutputBentNormals);
				var threadGroupsX = RenderExtensions.AlignUp(data.Resolution.x, ThirdPartyXeGTAO.XE_GTAO_NUMTHREADS_X) /
				                    ThirdPartyXeGTAO.XE_GTAO_NUMTHREADS_X;
				var threadGroupsY = RenderExtensions.AlignUp(data.Resolution.y, ThirdPartyXeGTAO.XE_GTAO_NUMTHREADS_Y) /
				                    ThirdPartyXeGTAO.XE_GTAO_NUMTHREADS_Y;

				context.cmd.SetComputeTextureParam(shaders.CompositeCS, 0, ShaderIDs.Composite._GTAOTerm,
					data.FinalAOTerm);
				context.cmd.SetComputeTextureParam(shaders.CompositeCS, 0, ShaderIDs.Composite._AOFinal,
					data.AOFinal);
				context.cmd.SetComputeVectorParam(shaders.CompositeCS, ShaderIDs.Composite._GTAOResolutionScale,
					data.ResolutionScale);
				context.cmd.SetComputeFloatParam(shaders.CompositeCS, ShaderIDs.Composite._Intensity, data.Intensity);

				context.cmd.DispatchCompute(shaders.CompositeCS, 0, threadGroupsX, threadGroupsY, 1);
			}

			context.cmd.SetGlobalTexture(ShaderIDs.Global._GTAOTerm, data.FinalAOTerm);
			context.cmd.SetGlobalVector(ShaderIDs.Global._GTAOResolutionScale, data.ResolutionScale);
			context.cmd.SetGlobalTexture(ShaderIDs.Global._ScreenSpaceOcclusionTexture, data.AOFinal);
			context.cmd.SetGlobalVector(ShaderIDs.Global._AmbientOcclusionParam,
				new Vector4(1f, 0f, 0f, data.Constants.DirectLightingStrength));
			context.cmd.SetKeyword(screenSpaceOcclusion, true);
		}

		private static class Profiling
		{
			public static readonly ProfilingSampler PrefilterDepths = new(nameof(PrefilterDepths));
			public static readonly ProfilingSampler MainPass = new(nameof(MainPass));
			public static readonly ProfilingSampler Denoise = new(nameof(Denoise));
			public static readonly ProfilingSampler Composite = new(nameof(Composite));
		}

		[SuppressMessage("ReSharper", "InconsistentNaming")]
		private static class Keywords
		{
			public static readonly string XE_GTAO_COMPUTE_BENT_NORMALS = nameof(XE_GTAO_COMPUTE_BENT_NORMALS);
		}

		[SuppressMessage("ReSharper", "InconsistentNaming")]
		private static class ShaderIDs
		{
			public static class Global
			{
				public static readonly int _ScreenSpaceOcclusionTexture =
					Shader.PropertyToID(nameof(_ScreenSpaceOcclusionTexture));

				public static readonly int _GTAOTerm = Shader.PropertyToID(nameof(_GTAOTerm));
				public static readonly int _GTAOResolutionScale = Shader.PropertyToID(nameof(_GTAOResolutionScale));

				// Statics
				public static readonly int _AmbientOcclusionParam = Shader.PropertyToID(nameof(_AmbientOcclusionParam));
			}

			public static class PrefilterDepths
			{
				public static readonly int g_srcRawDepth = Shader.PropertyToID(nameof(g_srcRawDepth));
				public static readonly int g_outWorkingDepthMIP0 = Shader.PropertyToID(nameof(g_outWorkingDepthMIP0));
				public static readonly int g_outWorkingDepthMIP1 = Shader.PropertyToID(nameof(g_outWorkingDepthMIP1));
				public static readonly int g_outWorkingDepthMIP2 = Shader.PropertyToID(nameof(g_outWorkingDepthMIP2));
				public static readonly int g_outWorkingDepthMIP3 = Shader.PropertyToID(nameof(g_outWorkingDepthMIP3));
				public static readonly int g_outWorkingDepthMIP4 = Shader.PropertyToID(nameof(g_outWorkingDepthMIP4));
			}

			public static class MainPass
			{
				public static readonly int g_srcWorkingDepth = Shader.PropertyToID(nameof(g_srcWorkingDepth));
				public static readonly int g_outWorkingAOTerm = Shader.PropertyToID(nameof(g_outWorkingAOTerm));
				public static readonly int g_outWorkingEdges = Shader.PropertyToID(nameof(g_outWorkingEdges));
			}

			public static class Denoise
			{
				public static readonly int g_srcWorkingAOTerm = Shader.PropertyToID(nameof(g_srcWorkingAOTerm));
				public static readonly int g_srcWorkingEdges = Shader.PropertyToID(nameof(g_srcWorkingEdges));
				public static readonly int g_outFinalAOTerm = Shader.PropertyToID(nameof(g_outFinalAOTerm));
			}

			public static class Composite
			{
				public static readonly int _GTAOTerm = Shader.PropertyToID(nameof(_GTAOTerm));
				public static readonly int _GTAOResolutionScale = Shader.PropertyToID(nameof(_GTAOResolutionScale));
				public static readonly int _AOFinal = Shader.PropertyToID(nameof(_AOFinal));
				public static readonly int _Intensity = Shader.PropertyToID(nameof(_Intensity));
			}
		}

		private sealed class PassData
		{
			public readonly TextureHandle[] WorkingDepths = new TextureHandle[ThirdPartyXeGTAO.XE_GTAO_DEPTH_MIP_LEVELS];
			public TextureHandle AOFinal;
			public TextureHandle AOTerm;
			public TextureHandle AOTermPong;
			public ThirdPartyXeGTAO.XeGTAOConstantsCS Constants;
			public TextureHandle Edges;
			public TextureHandle FinalAOTerm;
			public float Intensity;
			public bool OutputBentNormals;
			public int2 Resolution;
			public float4 ResolutionScale;
			public ThirdPartyXeGTAO.XeGTAOSettings Settings;
			public TextureHandle SrcRawDepth;
		}
	}
}

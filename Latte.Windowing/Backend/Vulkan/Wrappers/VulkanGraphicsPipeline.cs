using Latte.Assets;
using Latte.Windowing.Extensions;
using Latte.Windowing.Options;
using Silk.NET.Vulkan;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanGraphicsPipeline : VulkanWrapper
{
	internal required Pipeline Pipeline { get; init; }
	internal required PipelineLayout Layout { get; init; }

	[SetsRequiredMembers]
	internal VulkanGraphicsPipeline( in Pipeline pipeline, in PipelineLayout layout, LogicalGpu owner ) : base( owner )
	{
		Pipeline = pipeline;
		Layout = layout;
	}

	public unsafe override void Dispose()
	{
		if ( Disposed )
			return;

		Apis.Vk.DestroyPipeline( LogicalGpu!, Pipeline, null );
		Apis.Vk.DestroyPipelineLayout( LogicalGpu!, Layout, null );

		GC.SuppressFinalize( this );
		Disposed = true;
	}

	public static implicit operator Pipeline( VulkanGraphicsPipeline graphicsPipeline )
	{
		if ( graphicsPipeline.Disposed )
			throw new ObjectDisposedException( nameof( VulkanGraphicsPipeline ) );

		return graphicsPipeline.Pipeline;
	}

	internal static unsafe VulkanGraphicsPipeline New( LogicalGpu logicalGpu, IRenderingOptions options, Shader shader, in Extent2D swapchainExtent,
		in VulkanRenderPass renderPass, in ReadOnlySpan<VertexInputBindingDescription> bindingDescriptions,
		in ReadOnlySpan<VertexInputAttributeDescription> attributeDescriptions, in ReadOnlySpan<DynamicState> dynamicStates,
		in ReadOnlySpan<VulkanDescriptorSetLayout> descriptorSetLayouts, in ReadOnlySpan<PushConstantRange> pushConstantRanges )
	{
		using var vertexShaderModule = logicalGpu.CreateShaderModule( shader.VertexShaderCode.Span );
		using var fragmentShaderModule = logicalGpu.CreateShaderModule( shader.FragmentShaderCode.Span );

		var vertShaderStageInfo = new PipelineShaderStageCreateInfo
		{
			SType = StructureType.PipelineShaderStageCreateInfo,
			Stage = ShaderStageFlags.VertexBit,
			Module = vertexShaderModule,
			PName = (byte*)Marshal.StringToHGlobalAnsi( shader.VertexShaderEntryPoint )
		};

		var fragShaderStageInfo = new PipelineShaderStageCreateInfo
		{
			SType = StructureType.PipelineShaderStageCreateInfo,
			Stage = ShaderStageFlags.FragmentBit,
			Module = fragmentShaderModule,
			PName = (byte*)Marshal.StringToHGlobalAnsi( shader.FragmentShaderEntryPoint )
		};

		var shaderStages = stackalloc PipelineShaderStageCreateInfo[]
		{
			vertShaderStageInfo,
			fragShaderStageInfo
		};

		VulkanGraphicsPipeline graphicsPipeline;

		fixed ( VertexInputAttributeDescription* attributeDescriptionsPtr = attributeDescriptions )
		fixed ( VertexInputBindingDescription* bindingDescriptionsPtr = bindingDescriptions )
		fixed ( DynamicState* dynamicStatesPtr = dynamicStates )
		fixed ( PushConstantRange* pushConstantRangesPtr = pushConstantRanges )
		{
			var vertexInputInfo = new PipelineVertexInputStateCreateInfo
			{
				SType = StructureType.PipelineVertexInputStateCreateInfo,
				VertexBindingDescriptionCount = (uint)bindingDescriptions.Length,
				PVertexBindingDescriptions = bindingDescriptionsPtr,
				VertexAttributeDescriptionCount = (uint)attributeDescriptions.Length,
				PVertexAttributeDescriptions = attributeDescriptionsPtr
			};

			var inputAssembly = new PipelineInputAssemblyStateCreateInfo
			{
				SType = StructureType.PipelineInputAssemblyStateCreateInfo,
				Topology = PrimitiveTopology.TriangleList,
				PrimitiveRestartEnable = Vk.False
			};

			var viewport = new Viewport
			{
				X = 0,
				Y = 0,
				Width = swapchainExtent.Width,
				Height = swapchainExtent.Height,
				MinDepth = 0,
				MaxDepth = 0
			};

			var scissor = new Rect2D
			{
				Offset = new Offset2D( 0, 0 ),
				Extent = swapchainExtent
			};

			var dynamicState = new PipelineDynamicStateCreateInfo
			{
				SType = StructureType.PipelineDynamicStateCreateInfo,
				DynamicStateCount = (uint)dynamicStates.Length,
				PDynamicStates = dynamicStatesPtr
			};

			var viewportState = new PipelineViewportStateCreateInfo
			{
				SType = StructureType.PipelineViewportStateCreateInfo,
				ViewportCount = 1,
				PViewports = &viewport,
				ScissorCount = 1,
				PScissors = &scissor
			};

			var rasterizer = new PipelineRasterizationStateCreateInfo
			{
				SType = StructureType.PipelineRasterizationStateCreateInfo,
				DepthClampEnable = Vk.False,
				RasterizerDiscardEnable = Vk.False,
				PolygonMode = options.WireframeEnabled ? PolygonMode.Line : PolygonMode.Fill,
				LineWidth = 1,
				CullMode = CullModeFlags.BackBit,
				FrontFace = FrontFace.CounterClockwise,
				DepthBiasEnable = Vk.False,
			};

			var multisampling = new PipelineMultisampleStateCreateInfo
			{
				SType = StructureType.PipelineMultisampleStateCreateInfo,
				SampleShadingEnable = Vk.True,
				MinSampleShading = 0.2f,
				RasterizationSamples = options.Msaa.ToVulkan()
			};

			var colorBlendAttachment = new PipelineColorBlendAttachmentState
			{
				ColorWriteMask = ColorComponentFlags.RBit |
					ColorComponentFlags.GBit |
					ColorComponentFlags.BBit |
					ColorComponentFlags.ABit,
				BlendEnable = Vk.True,
				SrcColorBlendFactor = BlendFactor.SrcAlpha,
				DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
				ColorBlendOp = BlendOp.Add,
				SrcAlphaBlendFactor = BlendFactor.One,
				DstAlphaBlendFactor = BlendFactor.Zero,
				AlphaBlendOp = BlendOp.Add
			};

			var colorBlending = new PipelineColorBlendStateCreateInfo
			{
				SType = StructureType.PipelineColorBlendStateCreateInfo,
				LogicOpEnable = Vk.False,
				AttachmentCount = 1,
				PAttachments = &colorBlendAttachment
			};

			var depthStencil = new PipelineDepthStencilStateCreateInfo()
			{
				SType = StructureType.PipelineDepthStencilStateCreateInfo,
				DepthTestEnable = Vk.True,
				DepthWriteEnable = Vk.True,
				DepthCompareOp = CompareOp.Less,
				DepthBoundsTestEnable = Vk.False,
				StencilTestEnable = Vk.False
			};

			var descriptorSetLayoutsPtr = stackalloc DescriptorSetLayout[descriptorSetLayouts.Length];
			for ( var i = 0; i < descriptorSetLayouts.Length; i++ )
				descriptorSetLayoutsPtr[i] = descriptorSetLayouts[i].DescriptorSetLayout;

			var pipelineLayoutInfo = new PipelineLayoutCreateInfo
			{
				SType = StructureType.PipelineLayoutCreateInfo,
				SetLayoutCount = (uint)descriptorSetLayouts.Length,
				PSetLayouts = descriptorSetLayoutsPtr,
				PushConstantRangeCount = (uint)pushConstantRanges.Length,
				PPushConstantRanges = pushConstantRangesPtr
			};

			Apis.Vk.CreatePipelineLayout( logicalGpu, pipelineLayoutInfo, null, out var pipelineLayout ).Verify();

			var pipelineInfo = new GraphicsPipelineCreateInfo
			{
				SType = StructureType.GraphicsPipelineCreateInfo,
				StageCount = 2,
				PStages = shaderStages,
				PVertexInputState = &vertexInputInfo,
				PInputAssemblyState = &inputAssembly,
				PViewportState = &viewportState,
				PRasterizationState = &rasterizer,
				PMultisampleState = &multisampling,
				PColorBlendState = &colorBlending,
				PDepthStencilState = &depthStencil,
				PDynamicState = &dynamicState,
				Layout = pipelineLayout,
				RenderPass = renderPass,
				Subpass = 0
			};

			Apis.Vk.CreateGraphicsPipelines( logicalGpu, default, 1, &pipelineInfo, null, out var pipeline ).Verify();
			graphicsPipeline = new VulkanGraphicsPipeline( pipeline, pipelineLayout, logicalGpu );
		}

		Marshal.FreeHGlobal( (nint)vertShaderStageInfo.PName );
		Marshal.FreeHGlobal( (nint)fragShaderStageInfo.PName );

		return graphicsPipeline;
	}
}

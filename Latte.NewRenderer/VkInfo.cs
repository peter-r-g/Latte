using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using System;

namespace Latte.NewRenderer;

internal unsafe static class VkInfo
{
	// FIXME: Is there a better way to do this?
	private static readonly byte* MainStringPtr = (byte*)SilkMarshal.StringToPtr( "main" );

	internal static CommandPoolCreateInfo CommandPool( uint queueFamily, CommandPoolCreateFlags flags = CommandPoolCreateFlags.None )
	{
		return new CommandPoolCreateInfo
		{
			SType = StructureType.CommandPoolCreateInfo,
			PNext = null,
			QueueFamilyIndex = queueFamily,
			Flags = flags
		};
	}

	internal static CommandBufferAllocateInfo AllocateCommandBuffer( CommandPool pool, uint count, CommandBufferLevel level = CommandBufferLevel.Primary )
	{
		return new CommandBufferAllocateInfo
		{
			SType = StructureType.CommandBufferAllocateInfo,
			PNext = null,
			CommandPool = pool,
			CommandBufferCount = count,
			Level = level
		};
	}

	internal static CommandBufferBeginInfo BeginCommandBuffer( CommandBufferUsageFlags flags = CommandBufferUsageFlags.None )
	{
		return new CommandBufferBeginInfo
		{
			SType = StructureType.CommandBufferBeginInfo,
			PNext = null,
			PInheritanceInfo = null,
			Flags = flags
		};
	}

	internal static SubmitInfo SubmitInfo( ReadOnlySpan<CommandBuffer> commandBuffers )
	{
		fixed( CommandBuffer* commandBuffersPtr = commandBuffers )
		{
			return new SubmitInfo
			{
				SType = StructureType.SubmitInfo,
				PNext = null,
				CommandBufferCount = (uint)commandBuffers.Length,
				PCommandBuffers = commandBuffersPtr,
				PWaitDstStageMask = null,
				WaitSemaphoreCount = 0,
				PWaitSemaphores = null,
				SignalSemaphoreCount = 0,
				PSignalSemaphores = null
			};
		}
	}

	internal static PipelineShaderStageCreateInfo PipelineShaderStage( ShaderStageFlags stage, ShaderModule shaderModule )
	{
		return new PipelineShaderStageCreateInfo
		{
			SType = StructureType.PipelineShaderStageCreateInfo,
			PNext = null,
			Stage = stage,
			Module = shaderModule,
			PName = MainStringPtr,
			PSpecializationInfo = null,
			Flags = PipelineShaderStageCreateFlags.None
		};
	}

	internal static PipelineVertexInputStateCreateInfo PipelineVertexInputState( VertexInputDescription inputDescription )
	{
		fixed ( VertexInputAttributeDescription* attributesPtr = inputDescription.Attributes )
		fixed ( VertexInputBindingDescription* bindingsPtr = inputDescription.Bindings )
		{
			return new PipelineVertexInputStateCreateInfo
			{
				SType = StructureType.PipelineVertexInputStateCreateInfo,
				PNext = null,
				VertexAttributeDescriptionCount = (uint)inputDescription.Attributes.Length,
				PVertexAttributeDescriptions = attributesPtr,
				VertexBindingDescriptionCount = (uint)inputDescription.Bindings.Length,
				PVertexBindingDescriptions = bindingsPtr,
				Flags = 0
			};
		}
	}

	internal static PipelineInputAssemblyStateCreateInfo PipelineInputAssemblyState( PrimitiveTopology topology )
	{
		return new PipelineInputAssemblyStateCreateInfo
		{
			SType = StructureType.PipelineInputAssemblyStateCreateInfo,
			PNext = null,
			Topology = topology,
			PrimitiveRestartEnable = Vk.False,
			Flags = 0
		};
	}

	internal static PipelineRasterizationStateCreateInfo PipelineRasterizationState( PolygonMode polygonMode )
	{
		return new PipelineRasterizationStateCreateInfo
		{
			SType = StructureType.PipelineRasterizationStateCreateInfo,
			PNext = null,
			DepthClampEnable = Vk.False,
			RasterizerDiscardEnable = Vk.False,
			PolygonMode = polygonMode,
			LineWidth = 1,
			CullMode = CullModeFlags.None,
			FrontFace = FrontFace.Clockwise,
			DepthBiasEnable = Vk.False,
			DepthBiasConstantFactor = 0,
			DepthBiasClamp = 0,
			DepthBiasSlopeFactor = 0,
			Flags = 0
		};
	}

	internal static PipelineMultisampleStateCreateInfo PipelineMultisamplingState()
	{
		return new PipelineMultisampleStateCreateInfo
		{
			SType = StructureType.PipelineMultisampleStateCreateInfo,
			PNext = null,
			SampleShadingEnable = Vk.False,
			RasterizationSamples = SampleCountFlags.Count1Bit,
			MinSampleShading = 1,
			PSampleMask = null,
			AlphaToCoverageEnable = Vk.False,
			AlphaToOneEnable = Vk.False,
			Flags = 0
		};
	}

	internal static PipelineColorBlendAttachmentState PipelineColorBlendAttachmentState()
	{
		return new PipelineColorBlendAttachmentState
		{
			ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit,
			BlendEnable = Vk.False,
			AlphaBlendOp = Vk.False,
			ColorBlendOp = BlendOp.Add,
			SrcAlphaBlendFactor = BlendFactor.Zero,
			DstAlphaBlendFactor = BlendFactor.Zero,
			SrcColorBlendFactor = BlendFactor.Zero,
			DstColorBlendFactor = BlendFactor.Zero
		};
	}

	internal static PipelineLayoutCreateInfo PipelineLayout( ReadOnlySpan<PushConstantRange> pushConstantRanges, ReadOnlySpan<DescriptorSetLayout> descriptorSetLayouts )
	{
		fixed ( PushConstantRange* pushConstantRangesPtr = pushConstantRanges )
		fixed( DescriptorSetLayout* descriptorSetLayoutsPtr = descriptorSetLayouts )
		{
			return new PipelineLayoutCreateInfo
			{
				SType = StructureType.PipelineLayoutCreateInfo,
				PNext = null,
				Flags = PipelineLayoutCreateFlags.None,
				SetLayoutCount = (uint)descriptorSetLayouts.Length,
				PSetLayouts = descriptorSetLayoutsPtr,
				PushConstantRangeCount = (uint)pushConstantRanges.Length,
				PPushConstantRanges = pushConstantRangesPtr
			};
		}
	}

	internal static RenderPassCreateInfo RenderPass( ReadOnlySpan<AttachmentDescription> attachments, ReadOnlySpan<SubpassDescription> subpassDescriptions,
		ReadOnlySpan<SubpassDependency> subpassDependencies )
	{
		fixed ( AttachmentDescription* attachmentsPtr = attachments )
		fixed ( SubpassDescription* subpassDescriptionsPtr = subpassDescriptions )
		fixed ( SubpassDependency* subpassDependenciesPtr = subpassDependencies )
		{
			return new RenderPassCreateInfo
			{
				SType = StructureType.RenderPassCreateInfo,
				PNext = null,
				AttachmentCount = (uint)attachments.Length,
				PAttachments = attachmentsPtr,
				SubpassCount = (uint)subpassDescriptions.Length,
				PSubpasses = subpassDescriptionsPtr,
				DependencyCount = (uint)subpassDependencies.Length,
				PDependencies = subpassDependenciesPtr,
				Flags = RenderPassCreateFlags.None
			};
		}
	}

	internal static FramebufferCreateInfo Framebuffer( RenderPass renderPass, uint width, uint height, ReadOnlySpan<ImageView> attachments )
	{
		fixed( ImageView* attachmentsPtr = attachments )
		{
			return new FramebufferCreateInfo
			{
				SType = StructureType.FramebufferCreateInfo,
				PNext = null,
				RenderPass = renderPass,
				Width = width,
				Height = height,
				Layers = 1,
				AttachmentCount = (uint)attachments.Length,
				PAttachments = attachmentsPtr,
				Flags = FramebufferCreateFlags.None
			};
		}
	}

	internal static FenceCreateInfo Fence( FenceCreateFlags flags = FenceCreateFlags.None )
	{
		return new FenceCreateInfo
		{
			SType = StructureType.FenceCreateInfo,
			PNext = null,
			Flags = flags
		};
	}

	internal static SemaphoreCreateInfo Semaphore( SemaphoreCreateFlags flags = SemaphoreCreateFlags.None )
	{
		return new SemaphoreCreateInfo
		{
			SType = StructureType.SemaphoreCreateInfo,
			PNext = null,
			Flags = flags
		};
	}

	internal static ShaderModuleCreateInfo ShaderModule( byte[] shaderBytes, ShaderModuleCreateFlags flags = ShaderModuleCreateFlags.None )
	{
		fixed ( byte* shaderBytesPtr = shaderBytes )
		{
			return new ShaderModuleCreateInfo
			{
				SType = StructureType.ShaderModuleCreateInfo,
				PNext = null,
				CodeSize = (nuint)shaderBytes.Length,
				PCode = (uint*)shaderBytesPtr,
				Flags = flags
			};
		}
	}

	internal static BufferCreateInfo Buffer( ulong size, BufferUsageFlags usageFlags, SharingMode sharingMode )
	{
		return new BufferCreateInfo
		{
			SType = StructureType.BufferCreateInfo,
			PNext = null,
			Size = size,
			Usage = usageFlags,
			SharingMode = sharingMode,
			QueueFamilyIndexCount = 0,
			PQueueFamilyIndices = null,
			Flags = BufferCreateFlags.None
		};
	}

	internal static MemoryAllocateInfo AllocateMemory( ulong size, uint memoryTypeIndex )
	{
		return new MemoryAllocateInfo
		{
			SType = StructureType.MemoryAllocateInfo,
			PNext = null,
			AllocationSize = size,
			MemoryTypeIndex = memoryTypeIndex
		};
	}

	internal static ImageCreateInfo Image( Format format, ImageUsageFlags usageFlags, Extent3D extent )
	{
		return new ImageCreateInfo
		{
			SType = StructureType.ImageCreateInfo,
			PNext = null,
			ImageType = ImageType.Type2D,
			Format = format,
			Extent = extent,
			MipLevels = 1,
			ArrayLayers = 1,
			Samples = SampleCountFlags.Count1Bit,
			Tiling = ImageTiling.Optimal,
			Usage = usageFlags,
			Flags = ImageCreateFlags.None
		};
	}

	internal static ImageViewCreateInfo ImageView( Format format, Image image, ImageAspectFlags aspectFlags )
	{
		return new ImageViewCreateInfo
		{
			SType = StructureType.ImageViewCreateInfo,
			PNext = null,
			ViewType = ImageViewType.Type2D,
			Image = image,
			Format = format,
			SubresourceRange = new ImageSubresourceRange
			{
				AspectMask = aspectFlags,
				BaseMipLevel = 0,
				LevelCount = 1,
				BaseArrayLayer = 0,
				LayerCount = 1
			},
			Flags = ImageViewCreateFlags.None
		};
	}

	internal static PipelineDepthStencilStateCreateInfo PipelineDepthStencilState( bool depthTest, bool depthWrite, CompareOp compareOp )
	{
		return new PipelineDepthStencilStateCreateInfo
		{
			SType = StructureType.PipelineDepthStencilStateCreateInfo,
			PNext = null,
			DepthTestEnable = depthTest ? Vk.True : Vk.False,
			DepthWriteEnable = depthWrite ? Vk.True : Vk.False,
			DepthCompareOp = depthTest ? compareOp : CompareOp.Always,
			DepthBoundsTestEnable = Vk.False,
			MinDepthBounds = 0,
			MaxDepthBounds = 1,
			StencilTestEnable = Vk.False,
			Flags = PipelineDepthStencilStateCreateFlags.None
		};
	}

	internal static DescriptorPoolCreateInfo DescriptorPool( uint maxSets, ReadOnlySpan<DescriptorPoolSize> poolSizes,
		DescriptorPoolCreateFlags flags = DescriptorPoolCreateFlags.None )
	{
		fixed( DescriptorPoolSize* poolSizesPtr = poolSizes )
		{
			return new DescriptorPoolCreateInfo
			{
				SType = StructureType.DescriptorPoolCreateInfo,
				PNext = null,
				MaxSets = maxSets,
				PoolSizeCount = (uint)poolSizes.Length,
				PPoolSizes = poolSizesPtr,
				Flags = flags
			};
		}
	}

	internal static DescriptorSetLayoutCreateInfo DescriptorSetLayout( ReadOnlySpan<DescriptorSetLayoutBinding> bindings )
	{
		fixed( DescriptorSetLayoutBinding* bindingsPtr = bindings )
		{
			return new DescriptorSetLayoutCreateInfo
			{
				SType = StructureType.DescriptorSetLayoutCreateInfo,
				PNext = null,
				BindingCount = (uint)bindings.Length,
				PBindings = bindingsPtr,
				Flags = DescriptorSetLayoutCreateFlags.None
			};
		}
	}

	internal static DescriptorSetLayoutBinding DescriptorSetLayoutBinding( DescriptorType type, ShaderStageFlags stageFlags, uint binding )
	{
		return new DescriptorSetLayoutBinding
		{
			Binding = binding,
			DescriptorCount = 1,
			DescriptorType = type,
			StageFlags = stageFlags,
			PImmutableSamplers = null
		};
	}

	internal static DescriptorSetAllocateInfo AllocateDescriptorSet( DescriptorPool descriptorPool, ReadOnlySpan<DescriptorSetLayout> descriptorSetLayouts )
	{
		fixed( DescriptorSetLayout* descriptorSetLayoutsPtr = descriptorSetLayouts )
		{
			return new DescriptorSetAllocateInfo
			{
				SType = StructureType.DescriptorSetAllocateInfo,
				PNext = null,
				DescriptorPool = descriptorPool,
				DescriptorSetCount = (uint)descriptorSetLayouts.Length,
				PSetLayouts = descriptorSetLayoutsPtr
			};
		}
	}

	internal static SamplerCreateInfo Sampler( Filter filters, SamplerAddressMode addressMode = SamplerAddressMode.Repeat )
	{
		return new SamplerCreateInfo
		{
			SType = StructureType.SamplerCreateInfo,
			PNext = null,
			MinFilter = filters,
			MagFilter = filters,
			AddressModeU = addressMode,
			AddressModeV = addressMode,
			AddressModeW = addressMode
		};
	}
}

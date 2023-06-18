using Latte.Windowing.Assets;
using Latte.Windowing.Extensions;
using Latte.Windowing.Options;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class LogicalGpu : IDisposable
{
	internal const int ExtraSwapImages = 1;

	internal Gpu Gpu { get; }
	internal Device LogicalDevice { get; }

	internal Queue GraphicsQueue { get; }
	internal Queue PresentQueue { get; }

	private ConcurrentQueue<Action> DisposeQueue { get; } = new();

	public LogicalGpu( in Device logicalDevice, Gpu gpu, in QueueFamilyIndices familyIndices )
	{
		if ( !familyIndices.IsComplete() )
			throw new ArgumentException( $"Cannot create {nameof( LogicalGpu )} with an incomplete {nameof( QueueFamilyIndices )}", nameof( familyIndices ) );

		LogicalDevice = logicalDevice;
		Gpu = gpu;
		GraphicsQueue = Apis.Vk.GetDeviceQueue( LogicalDevice, familyIndices.GraphicsFamily.Value, 0 );
		PresentQueue = Apis.Vk.GetDeviceQueue( LogicalDevice, familyIndices.PresentFamily.Value, 0 );
	}

	~LogicalGpu()
	{
		Dispose();
	}

	public unsafe void Dispose()
	{
		while ( DisposeQueue.TryDequeue( out var disposeCb ) )
			disposeCb();

		Apis.Vk.DestroyDevice( LogicalDevice, null );
		GC.SuppressFinalize( this );
	}

	internal unsafe VulkanSwapchain CreateSwapchain()
	{
		var instance = Gpu.Instance;
		var swapChainSupport = Gpu.SwapchainSupportDetails;

		var surfaceFormat = ChooseSwapSurfaceFormat( swapChainSupport.Formats );
		var presentMode = ChooseSwapPresentMode( swapChainSupport.PresentModes );
		var extent = ChooseSwapExtent( Gpu.Instance.Window, swapChainSupport.Capabilities );

		var imageCount = swapChainSupport.Capabilities.MinImageCount + ExtraSwapImages;
		if ( swapChainSupport.Capabilities.MaxImageCount > 0 && imageCount > swapChainSupport.Capabilities.MaxImageCount )
			imageCount = swapChainSupport.Capabilities.MaxImageCount;

		var createInfo = new SwapchainCreateInfoKHR
		{
			SType = StructureType.SwapchainCreateInfoKhr,
			Surface = instance.Surface,
			MinImageCount = imageCount,
			ImageFormat = surfaceFormat.Format,
			ImageColorSpace = surfaceFormat.ColorSpace,
			ImageExtent = extent,
			ImageArrayLayers = 1,
			ImageUsage = ImageUsageFlags.ColorAttachmentBit
		};

		var indices = Gpu.GetQueueFamilyIndices();
		if ( !indices.IsComplete() )
			throw new ApplicationException( "Attempted to create a swap chain from indices that are not complete" );

		var queueFamilyIndices = stackalloc uint[]
		{
			indices.GraphicsFamily.Value,
			indices.PresentFamily.Value
		};

		if ( indices.GraphicsFamily != indices.PresentFamily )
		{
			createInfo.ImageSharingMode = SharingMode.Concurrent;
			createInfo.QueueFamilyIndexCount = 2;
			createInfo.PQueueFamilyIndices = queueFamilyIndices;
		}
		else
			createInfo.ImageSharingMode = SharingMode.Exclusive;

		createInfo.PreTransform = swapChainSupport.Capabilities.CurrentTransform;
		createInfo.CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr;
		createInfo.PresentMode = presentMode;
		createInfo.Clipped = Vk.True;

		if ( !Apis.Vk.TryGetDeviceExtension<KhrSwapchain>( instance, LogicalDevice, out var swapchainExtension ) )
			throw new ApplicationException( "Failed to get KHR_swapchain extension" );

		if ( swapchainExtension.CreateSwapchain( LogicalDevice, createInfo, null, out var swapchain ) != Result.Success )
			throw new ApplicationException( "Failed to create swap chain" );

		if ( swapchainExtension.GetSwapchainImages( LogicalDevice, swapchain, &imageCount, null ) != Result.Success )
			throw new ApplicationException( "Failed to get swap chain images (1)" );

		var swapchainImages = new Image[imageCount];
		if ( swapchainExtension.GetSwapchainImages( LogicalDevice, swapchain, &imageCount, swapchainImages ) != Result.Success )
			throw new ApplicationException( "Failed to get swap chain images (2)" );

		var swapchainImageFormat = surfaceFormat.Format;
		var swapchainExtent = extent;

		var swapchainImageViews = new ImageView[imageCount];

		for ( var i = 0; i < swapchainImages.Length; i++ )
			swapchainImageViews[i] = CreateImageView( swapchainImages[i], swapchainImageFormat, ImageAspectFlags.ColorBit, 1 );

		var vulkanSwapchain = new VulkanSwapchain( swapchain, swapchainImages, swapchainImageViews,
			swapchainImageFormat, swapchainExtent, swapchainExtension, this );
		DisposeQueue.Enqueue( vulkanSwapchain.Dispose );
		return vulkanSwapchain;
	}

	internal unsafe GraphicsPipeline CreateGraphicsPipeline( IRenderingOptions options, Shader shader, in Extent2D swapchainExtent, in RenderPass renderPass,
		ReadOnlySpan<VertexInputBindingDescription> bindingDescriptions, ReadOnlySpan<VertexInputAttributeDescription> attributeDescriptions,
		ReadOnlySpan<DynamicState> dynamicStates, ReadOnlySpan<DescriptorSetLayout> descriptorSetLayouts,
		ReadOnlySpan<PushConstantRange> pushConstantRanges )
	{
		var vertShaderStageInfo = new PipelineShaderStageCreateInfo
		{
			SType = StructureType.PipelineShaderStageCreateInfo,
			Stage = ShaderStageFlags.VertexBit,
			Module = shader.VertexShaderModule,
			PName = (byte*)Marshal.StringToHGlobalAnsi( shader.VertexShaderEntryPoint )
		};

		var fragShaderStageInfo = new PipelineShaderStageCreateInfo
		{
			SType = StructureType.PipelineShaderStageCreateInfo,
			Stage = ShaderStageFlags.FragmentBit,
			Module = shader.FragmentShaderModule,
			PName = (byte*)Marshal.StringToHGlobalAnsi( shader.FragmentShaderEntryPoint )
		};

		var shaderStages = stackalloc PipelineShaderStageCreateInfo[]
		{
			vertShaderStageInfo,
			fragShaderStageInfo
		};

		GraphicsPipeline graphicsPipeline;

		fixed ( VertexInputAttributeDescription* attributeDescriptionsPtr = attributeDescriptions )
		fixed( VertexInputBindingDescription* bindingDescriptionsPtr = bindingDescriptions )
		fixed( DynamicState* dynamicStatesPtr = dynamicStates )
		fixed ( DescriptorSetLayout* descriptorSetLayoutsPtr = descriptorSetLayouts )
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

			var pipelineLayoutInfo = new PipelineLayoutCreateInfo
			{
				SType = StructureType.PipelineLayoutCreateInfo,
				SetLayoutCount = (uint)descriptorSetLayouts.Length,
				PSetLayouts = descriptorSetLayoutsPtr,
				PushConstantRangeCount = (uint)pushConstantRanges.Length,
				PPushConstantRanges = pushConstantRangesPtr
			};

			if ( Apis.Vk.CreatePipelineLayout( LogicalDevice, pipelineLayoutInfo, null, out var pipelineLayout ) != Result.Success )
				throw new ApplicationException( "Failed to create Vulkan pipeline layout" );

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

			if ( Apis.Vk.CreateGraphicsPipelines( LogicalDevice, default, 1, &pipelineInfo, null, out var pipeline ) != Result.Success )
				throw new ApplicationException( "Failed to create Vulkan graphics pipeline" );

			graphicsPipeline = new GraphicsPipeline( pipeline, pipelineLayout, this );
		}

		Marshal.FreeHGlobal( (nint)vertShaderStageInfo.PName );
		Marshal.FreeHGlobal( (nint)fragShaderStageInfo.PName );

		DisposeQueue.Enqueue( graphicsPipeline.Dispose );
		return graphicsPipeline;
	}

	internal unsafe DescriptorSetLayout CreateDescriptorSetLayout( ReadOnlySpan<DescriptorSetLayoutBinding> bindings )
	{
		fixed( DescriptorSetLayoutBinding* bindingsPtr = bindings )
		{
			var layoutInfo = new DescriptorSetLayoutCreateInfo()
			{
				SType = StructureType.DescriptorSetLayoutCreateInfo,
				BindingCount = (uint)bindings.Length,
				PBindings = bindingsPtr
			};

			if ( Apis.Vk.CreateDescriptorSetLayout( LogicalDevice, layoutInfo, null, out var descriptorSetLayout ) != Result.Success )
				throw new ApplicationException( "Failed to create Vulkan descriptor set layout" );

			DisposeQueue.Enqueue( () => Apis.Vk.DestroyDescriptorSetLayout( LogicalDevice, descriptorSetLayout, null ) );
			return descriptorSetLayout;
		}
	}

	internal unsafe RenderPass CreateRenderPass( Format swapchainImageFormat, SampleCountFlags msaaSamples )
	{
		var useMsaa = msaaSamples != SampleCountFlags.Count1Bit;
		var colorAttachment = new AttachmentDescription()
		{
			Format = swapchainImageFormat,
			Samples = msaaSamples,
			LoadOp = AttachmentLoadOp.Clear,
			StoreOp = AttachmentStoreOp.Store,
			InitialLayout = ImageLayout.Undefined,
			FinalLayout = useMsaa
				? ImageLayout.ColorAttachmentOptimal
				: ImageLayout.PresentSrcKhr
		};

		var colorAttachmentRef = new AttachmentReference
		{
			Attachment = 0,
			Layout = ImageLayout.AttachmentOptimal
		};

		var depthAttachment = new AttachmentDescription()
		{
			Format = FindDepthFormat(),
			Samples = msaaSamples,
			LoadOp = AttachmentLoadOp.Clear,
			StoreOp = AttachmentStoreOp.DontCare,
			StencilLoadOp = AttachmentLoadOp.DontCare,
			StencilStoreOp = AttachmentStoreOp.DontCare,
			InitialLayout = ImageLayout.Undefined,
			FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
		};

		var depthAttachmentRef = new AttachmentReference()
		{
			Attachment = 1,
			Layout = ImageLayout.DepthStencilAttachmentOptimal
		};

		var colorAttachmentResolve = new AttachmentDescription()
		{
			Format = swapchainImageFormat,
			Samples = SampleCountFlags.Count1Bit,
			LoadOp = AttachmentLoadOp.DontCare,
			StoreOp = AttachmentStoreOp.DontCare,
			StencilLoadOp = AttachmentLoadOp.DontCare,
			StencilStoreOp = AttachmentStoreOp.DontCare,
			InitialLayout = ImageLayout.Undefined,
			FinalLayout = ImageLayout.PresentSrcKhr
		};

		var colorAttachmentResolveRef = new AttachmentReference()
		{
			Attachment = 2,
			Layout = ImageLayout.ColorAttachmentOptimal
		};

		var subpassDescription = new SubpassDescription
		{
			PipelineBindPoint = PipelineBindPoint.Graphics,
			ColorAttachmentCount = 1,
			PColorAttachments = &colorAttachmentRef,
			PDepthStencilAttachment = &depthAttachmentRef,
			PResolveAttachments = useMsaa
				? &colorAttachmentResolveRef
				: null
		};

		var subpassDependency = new SubpassDependency()
		{
			SrcSubpass = Vk.SubpassExternal,
			DstSubpass = 0,
			SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
			SrcAccessMask = 0,
			DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
			DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit
		};

		var attachments = stackalloc AttachmentDescription[useMsaa ? 3 : 2];
		attachments[0] = colorAttachment;
		attachments[1] = depthAttachment;
		if ( useMsaa )
			attachments[2] = colorAttachmentResolve;

		var renderPassInfo = new RenderPassCreateInfo
		{
			SType = StructureType.RenderPassCreateInfo,
			AttachmentCount = (uint)(useMsaa ? 3 : 2),
			PAttachments = attachments,
			SubpassCount = 1,
			PSubpasses = &subpassDescription,
			DependencyCount = 1,
			PDependencies = &subpassDependency
		};

		if ( Apis.Vk.CreateRenderPass( LogicalDevice, renderPassInfo, null, out var renderPass ) != Result.Success )
			throw new ApplicationException( "Failed to create Vulkan render pass" );

		DisposeQueue.Enqueue( () => Apis.Vk.DestroyRenderPass( LogicalDevice, renderPass, null ) );
		return renderPass;
	}

	internal unsafe CommandPool CreateCommandPool( uint queueFamilyIndex )
	{
		var poolInfo = new CommandPoolCreateInfo
		{
			SType = StructureType.CommandPoolCreateInfo,
			Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
			QueueFamilyIndex = queueFamilyIndex
		};

		if ( Apis.Vk.CreateCommandPool( LogicalDevice, poolInfo, null, out var commandPool ) != Result.Success )
			throw new ApplicationException( "Failed to create Vulkan command pool" );

		DisposeQueue.Enqueue( () => Apis.Vk.DestroyCommandPool( LogicalDevice, commandPool, null ) );
		return commandPool;
	}

	internal unsafe VulkanBuffer CreateBuffer( ulong size, BufferUsageFlags usageFlags, MemoryPropertyFlags memoryPropertyFlags )
	{
		var bufferInfo = new BufferCreateInfo
		{
			SType = StructureType.BufferCreateInfo,
			Size = size,
			Usage = usageFlags,
			SharingMode = SharingMode.Exclusive
		};

		if ( Apis.Vk.CreateBuffer( LogicalDevice, bufferInfo, null, out var buffer ) != Result.Success )
			throw new ApplicationException( "Failed to create Vulkan buffer" );

		var requirements = Apis.Vk.GetBufferMemoryRequirements( LogicalDevice, buffer );

		var allocateInfo = new MemoryAllocateInfo
		{
			SType = StructureType.MemoryAllocateInfo,
			AllocationSize = requirements.Size,
			MemoryTypeIndex = FindMemoryType( requirements.MemoryTypeBits, memoryPropertyFlags )
		};

		if ( Apis.Vk.AllocateMemory( LogicalDevice, allocateInfo, null, out var bufferMemory ) != Result.Success )
			throw new ApplicationException( "Failed to allocate Vulkan buffer memory" );

		if ( Apis.Vk.BindBufferMemory( LogicalDevice, buffer, bufferMemory, 0 ) != Result.Success )
			throw new ApplicationException( "Failed to bind buffer memory to buffer" );

		var vulkanBuffer = new VulkanBuffer( buffer, bufferMemory, this );
		DisposeQueue.Enqueue( vulkanBuffer.Dispose );
		return vulkanBuffer;
	}

	internal unsafe VulkanImage CreateImage( uint width, uint height, uint mipLevels, SampleCountFlags numSamples,
		Format format, ImageTiling tiling, ImageUsageFlags usageFlags, MemoryPropertyFlags memoryPropertyFlags, ImageAspectFlags aspectFlags )
	{
		CreateImage( width, height, mipLevels, numSamples,
			format, tiling, usageFlags, memoryPropertyFlags,
			out var image, out var imageMemory );

		var imageView = CreateImageView( image, format, aspectFlags, 1 );
		var vulkanImage = new VulkanImage( image, imageMemory, imageView, this );

		DisposeQueue.Enqueue( vulkanImage.Dispose );
		return vulkanImage;
	}

	private unsafe void CreateImage( uint width, uint height, uint mipLevels, SampleCountFlags numSamples,
		Format format, ImageTiling tiling, ImageUsageFlags usageFlags, MemoryPropertyFlags memoryPropertyFlags,
		out Image image, out DeviceMemory imageMemory )
	{
		var imageInfo = new ImageCreateInfo()
		{
			SType = StructureType.ImageCreateInfo,
			ImageType = ImageType.Type2D,
			Extent =
			{
				Width = width,
				Height = height,
				Depth = 1
			},
			MipLevels = mipLevels,
			ArrayLayers = 1,
			Format = format,
			Tiling = tiling,
			InitialLayout = ImageLayout.Undefined,
			Usage = usageFlags,
			SharingMode = SharingMode.Exclusive,
			Samples = numSamples
		};

		if ( Apis.Vk.CreateImage( LogicalDevice, imageInfo, null, out image ) != Result.Success )
			throw new ApplicationException( "Failed to create image" );

		var requirements = Apis.Vk.GetImageMemoryRequirements( LogicalDevice, image );

		var allocateInfo = new MemoryAllocateInfo()
		{
			SType = StructureType.MemoryAllocateInfo,
			AllocationSize = requirements.Size,
			MemoryTypeIndex = FindMemoryType( requirements.MemoryTypeBits, memoryPropertyFlags )
		};

		if ( Apis.Vk.AllocateMemory( LogicalDevice, allocateInfo, null, out imageMemory ) != Result.Success )
			throw new ApplicationException( "Failed to allocate image memory" );

		if ( Apis.Vk.BindImageMemory( LogicalDevice, image, imageMemory, 0 ) != Result.Success )
			throw new ApplicationException( "Failed to bind image memory" );
	}

	private unsafe ImageView CreateImageView( in Image image, Format format, ImageAspectFlags aspectFlags, uint mipLevels )
	{
		var viewInfo = new ImageViewCreateInfo()
		{
			SType = StructureType.ImageViewCreateInfo,
			Image = image,
			ViewType = ImageViewType.Type2D,
			Format = format,
			SubresourceRange =
			{
				AspectMask = aspectFlags,
				BaseMipLevel = 0,
				LevelCount = mipLevels,
				BaseArrayLayer = 0,
				LayerCount = 1
			}
		};

		if ( Apis.Vk.CreateImageView( LogicalDevice, viewInfo, null, out var imageView ) != Result.Success )
			throw new ApplicationException( "Failed to create Vulkan texture image view" );

		return imageView;
	}

	private uint FindMemoryType( uint typeFilter, MemoryPropertyFlags properties )
	{
		var memoryProperties = Gpu.MemoryProperties;
		for ( var i = 0; i < memoryProperties.MemoryTypeCount; i++ )
		{
			if ( (typeFilter & (1 << i)) != 0 && (memoryProperties.MemoryTypes[i].PropertyFlags & properties) == properties )
				return (uint)i;
		}

		throw new ApplicationException( "Failed to find suitable memory type" );
	}

	private Format FindSupportedFormat( IEnumerable<Format> candidates, ImageTiling tiling, FormatFeatureFlags features )
	{
		foreach ( var format in candidates )
		{
			var properties = Gpu.GetFormatProperties( format );

			if ( tiling == ImageTiling.Linear && (properties.LinearTilingFeatures & features) == features )
				return format;
			else if ( tiling == ImageTiling.Optimal && (properties.OptimalTilingFeatures & features) == features )
				return format;
		}

		throw new ApplicationException( "Failed to find a suitable format" );
	}

	private Format FindDepthFormat()
	{
		var formats = new Format[]
		{
			Format.D32Sfloat,
			Format.D32SfloatS8Uint,
			Format.D24UnormS8Uint
		};

		return FindSupportedFormat( formats, ImageTiling.Optimal, FormatFeatureFlags.DepthStencilAttachmentBit );
	}

	private static SurfaceFormatKHR ChooseSwapSurfaceFormat( IEnumerable<SurfaceFormatKHR> formats )
	{
		if ( !formats.Any() )
			throw new ArgumentException( "No formats were provided", nameof( formats ) );

		foreach ( var format in formats )
		{
			if ( format.Format != Format.B8G8R8A8Srgb )
				continue;

			if ( format.ColorSpace != ColorSpaceKHR.SpaceSrgbNonlinearKhr )
				continue;

			return format;
		}

		return formats.First();
	}

	private static PresentModeKHR ChooseSwapPresentMode( IEnumerable<PresentModeKHR> presentModes )
	{
		foreach ( var presentMode in presentModes )
		{
			if ( presentMode == PresentModeKHR.MailboxKhr )
				return presentMode;
		}

		return PresentModeKHR.FifoKhr;
	}

	private static Extent2D ChooseSwapExtent( IWindow window, in SurfaceCapabilitiesKHR capabilities )
	{
		if ( capabilities.CurrentExtent.Width != uint.MaxValue )
			return capabilities.CurrentExtent;

		var frameBufferSize = window.FramebufferSize;
		var extent = new Extent2D( (uint)frameBufferSize.X, (uint)frameBufferSize.Y );
		extent.Width = Math.Clamp( extent.Width, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width );
		extent.Height = Math.Clamp( extent.Height, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height );

		return extent;
	}


	public static implicit operator Device( LogicalGpu logicalGpu ) => logicalGpu.LogicalDevice;
}

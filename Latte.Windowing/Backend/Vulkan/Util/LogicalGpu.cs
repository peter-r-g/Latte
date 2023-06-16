using Latte.Windowing.Options;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using System;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class LogicalGpu : IDisposable
{
	internal const int ExtraSwapImages = 1;

	internal Gpu Gpu { get; }
	internal Device LogicalDevice { get; }

	internal Queue GraphicsQueue { get; }
	internal Queue PresentQueue { get; }

	internal SwapchainKHR Swapchain { get; private set; }
	internal Image[] SwapchainImages { get; private set; } = Array.Empty<Image>();
	internal ImageView[] SwapchainImageViews { get; private set; } = Array.Empty<ImageView>();
	internal Format SwapchainImageFormat { get; private set; }
	internal Extent2D SwapchainExtent { get; private set; }
	internal KhrSwapchain SwapchainExtension { get; private set; } = null!;

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
		foreach ( var imageView in SwapchainImageViews )
			Apis.Vk.DestroyImageView( LogicalDevice, imageView, null );

		SwapchainExtension.DestroySwapchain( LogicalDevice, Swapchain, null );
		Apis.Vk.DestroyDevice( LogicalDevice, null );
		GC.SuppressFinalize( this );
	}

	internal unsafe void CreateSwapchain()
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

		SwapchainExtension = swapchainExtension;

		if ( swapchainExtension.CreateSwapchain( LogicalDevice, createInfo, null, out var swapchain ) != Result.Success )
			throw new ApplicationException( "Failed to create swap chain" );

		Swapchain = swapchain;

		if ( swapchainExtension.GetSwapchainImages( LogicalDevice, Swapchain, &imageCount, null ) != Result.Success )
			throw new ApplicationException( "Failed to get swap chain images (1)" );

		SwapchainImages = new Image[imageCount];
		if ( swapchainExtension.GetSwapchainImages( LogicalDevice, Swapchain, &imageCount, SwapchainImages ) != Result.Success )
			throw new ApplicationException( "Failed to get swap chain images (2)" );

		SwapchainImageFormat = surfaceFormat.Format;
		SwapchainExtent = extent;

		SwapchainImageViews = new ImageView[imageCount];

		for ( var i = 0; i < SwapchainImages.Length; i++ )
			SwapchainImageViews[i] = CreateImageView( SwapchainImages[i], SwapchainImageFormat, ImageAspectFlags.ColorBit, 1 );
	}

	internal unsafe RenderPass CreateRenderPass( SampleCountFlags msaaSamples )
	{
		var useMsaa = msaaSamples != SampleCountFlags.Count1Bit;
		var colorAttachment = new AttachmentDescription()
		{
			Format = SwapchainImageFormat,
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
			Format = SwapchainImageFormat,
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

		return renderPass;
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

	private static SurfaceFormatKHR ChooseSwapSurfaceFormat( SurfaceFormatKHR[] formats )
	{
		if ( formats.Length == 0 )
			throw new ArgumentException( "No formats were provided", nameof( formats ) );

		foreach ( var format in formats )
		{
			if ( format.Format != Format.B8G8R8A8Srgb )
				continue;

			if ( format.ColorSpace != ColorSpaceKHR.SpaceSrgbNonlinearKhr )
				continue;

			return format;
		}

		return formats[0];
	}

	private static PresentModeKHR ChooseSwapPresentMode( PresentModeKHR[] presentModes )
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

using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Vulkan;
using System;
using Latte.Windowing.Extensions;
using Silk.NET.Windowing;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Immutable;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanSwapchain : VulkanWrapper
{
	internal required SwapchainKHR Swapchain { get; init; }
	internal required ImmutableArray<Image> Images { get; init; }
	internal required ImmutableArray<ImageView> ImageViews { get; init; }
	internal required Format ImageFormat { get; init; }
	internal required Extent2D Extent { get; init; }
	internal required KhrSwapchain Extension { get; init; }

	internal ImmutableArray<Framebuffer> FrameBuffers { get; private set; }

	[SetsRequiredMembers]
	internal VulkanSwapchain( in SwapchainKHR swapchain, in ImmutableArray<Image> images, in ImmutableArray<ImageView> imageViews,
		Format imageFormat, in Extent2D extent, KhrSwapchain extension, LogicalGpu owner ) : base( owner )
	{
		Swapchain = swapchain;
		Images = images;
		ImageViews = imageViews;
		ImageFormat = imageFormat;
		Extent = extent;
		Extension = extension;
	}

	public unsafe override void Dispose()
	{
		if ( Disposed )
			return;

		foreach ( var frameBuffer in FrameBuffers )
			Apis.Vk.DestroyFramebuffer( LogicalGpu!, frameBuffer, null );

		foreach ( var imageView in ImageViews )
			Apis.Vk.DestroyImageView( LogicalGpu!, imageView, null );

		Extension.DestroySwapchain( LogicalGpu!, Swapchain, null );

		GC.SuppressFinalize( this );
		Disposed = true;
	}

	internal unsafe void CreateFrameBuffers( in RenderPass renderPass, in ReadOnlySpan<ImageView> attachments,
		Action<int>? frameBufferCreateCb = null )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( VulkanSwapchain ) );

		var frameBuffers = ImmutableArray.CreateBuilder<Framebuffer>( ImageViews.Length );

		fixed( ImageView* attachmentsPtr = attachments )
		{
			for ( var i = 0; i < ImageViews.Length; i++ )
			{
				if ( frameBufferCreateCb is not null )
					frameBufferCreateCb( i );

				var frameBufferInfo = new FramebufferCreateInfo
				{
					SType = StructureType.FramebufferCreateInfo,
					RenderPass = renderPass,
					AttachmentCount = (uint)attachments.Length,
					PAttachments = attachmentsPtr,
					Width = Extent.Width,
					Height = Extent.Height,
					Layers = 1
				};

				Apis.Vk.CreateFramebuffer( LogicalGpu!, frameBufferInfo, null, out var frameBuffer ).Verify();
				frameBuffers.Add( frameBuffer );
			}
		}

		FrameBuffers = frameBuffers.MoveToImmutable();
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

	public static implicit operator SwapchainKHR( VulkanSwapchain vulkanSwapchain )
	{
		if ( vulkanSwapchain.Disposed )
			throw new ObjectDisposedException( nameof( VulkanSwapchain ) );

		return vulkanSwapchain.Swapchain;
	}

	internal static unsafe VulkanSwapchain New( LogicalGpu logicalGpu )
	{
		var gpu = logicalGpu.Gpu!;
		var instance = gpu.Instance!;
		var swapChainSupport = gpu.SwapchainSupportDetails;

		var surfaceFormat = ChooseSwapSurfaceFormat( swapChainSupport.Formats );
		var presentMode = ChooseSwapPresentMode( swapChainSupport.PresentModes );
		var extent = ChooseSwapExtent( instance.Window, swapChainSupport.Capabilities );

		var imageCount = swapChainSupport.Capabilities.MinImageCount + VulkanBackend.ExtraSwapImages;
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

		var indices = gpu.GetQueueFamilyIndices();
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

		if ( !Apis.Vk.TryGetDeviceExtension<KhrSwapchain>( instance, logicalGpu, out var swapchainExtension ) )
			throw new ApplicationException( "Failed to get KHR_swapchain extension" );

		swapchainExtension.CreateSwapchain( logicalGpu, createInfo, null, out var swapchain ).Verify();
		swapchainExtension.GetSwapchainImages( logicalGpu, swapchain, &imageCount, null ).Verify();

		var swapchainImages = new Image[imageCount];
		swapchainExtension.GetSwapchainImages( logicalGpu, swapchain, &imageCount, swapchainImages ).Verify();

		var swapchainImageFormat = surfaceFormat.Format;
		var swapchainExtent = extent;

		var swapchainImageViews = new ImageView[imageCount];
		for ( var i = 0; i < swapchainImages.Length; i++ )
			swapchainImageViews[i] = logicalGpu.CreateImageView( swapchainImages[i], swapchainImageFormat, ImageAspectFlags.ColorBit, 1 );

		return new VulkanSwapchain( swapchain, swapchainImages.ToImmutableArray(), swapchainImageViews.ToImmutableArray(),
			swapchainImageFormat, swapchainExtent, swapchainExtension, logicalGpu );
	}
}

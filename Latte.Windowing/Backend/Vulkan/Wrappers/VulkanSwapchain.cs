using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Vulkan;
using System;
using Latte.Windowing.Extensions;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanSwapchain : VulkanWrapper
{
	internal SwapchainKHR Swapchain { get; }
	internal Image[] Images { get; }
	internal ImageView[] ImageViews { get; }
	internal Format ImageFormat { get; }
	internal Extent2D Extent { get; }
	internal KhrSwapchain Extension { get; } = null!;

	internal Framebuffer[] FrameBuffers { get; private set; } = Array.Empty<Framebuffer>();

	internal VulkanSwapchain( in SwapchainKHR swapchain, Image[] images, ImageView[] imageViews,
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

		FrameBuffers = new Framebuffer[ImageViews.Length];

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
				FrameBuffers[i] = frameBuffer;
			}
		}
	}

	public static implicit operator SwapchainKHR( VulkanSwapchain vulkanSwapchain )
	{
		if ( vulkanSwapchain.Disposed )
			throw new ObjectDisposedException( nameof( VulkanSwapchain ) );

		return vulkanSwapchain.Swapchain;
	}
}

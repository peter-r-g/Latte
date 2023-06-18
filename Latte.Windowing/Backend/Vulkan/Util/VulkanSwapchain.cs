using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Vulkan;
using System;
using Latte.Windowing.Options;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanSwapchain : IDisposable
{
	internal LogicalGpu Owner { get; }

	internal SwapchainKHR Swapchain { get; }
	internal Image[] Images { get; }
	internal ImageView[] ImageViews { get; }
	internal Format ImageFormat { get; }
	internal Extent2D Extent { get; }
	internal KhrSwapchain Extension { get; } = null!;

	internal Framebuffer[] FrameBuffers { get; private set; } = Array.Empty<Framebuffer>();

	private bool disposed;

	internal VulkanSwapchain( in SwapchainKHR swapchain, Image[] images, ImageView[] imageViews,
		Format imageFormat, in Extent2D extent, KhrSwapchain extension, LogicalGpu owner )
	{
		Swapchain = swapchain;
		Images = images;
		ImageViews = imageViews;
		ImageFormat = imageFormat;
		Extent = extent;
		Extension = extension;
		Owner = owner;
	}

	~VulkanSwapchain()
	{
		Dispose();
	}

	public unsafe void Dispose()
	{
		if ( disposed )
			return;

		disposed = true;
		foreach ( var frameBuffer in FrameBuffers )
			Apis.Vk.DestroyFramebuffer( Owner, frameBuffer, null );

		foreach ( var imageView in ImageViews )
			Apis.Vk.DestroyImageView( Owner, imageView, null );

		Extension.DestroySwapchain( Owner, Swapchain, null );

		GC.SuppressFinalize( this );
	}

	internal unsafe void CreateFrameBuffers( in RenderPass renderPass, in ReadOnlySpan<ImageView> attachments,
		Action<int>? frameBufferCreateCb = null )
	{
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

				if ( Apis.Vk.CreateFramebuffer( Owner, frameBufferInfo, null, out var frameBuffer ) != Result.Success )
					throw new ApplicationException( $"Failed to create Vulkan frame buffer [{i}]" );

				FrameBuffers[i] = frameBuffer;
			}
		}
	}

	public static implicit operator SwapchainKHR( VulkanSwapchain vulkanSwapchain ) => vulkanSwapchain.Swapchain;
}

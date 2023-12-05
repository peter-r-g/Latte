using Latte.NewRenderer.Exceptions;
using Latte.NewRenderer.Extensions;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using System;
using System.Collections.Immutable;

namespace Latte.NewRenderer.Builders;

internal sealed class VkSwapchainBuilder
{
	private readonly Instance instance;
	private readonly PhysicalDevice physicalDevice;
	private readonly Device logicalDevice;
	private SurfaceKHR surface;
	private KhrSurface? surfaceExtension;
	private VkQueueFamilyIndices queueFamilyIndices;
	private Format swapchainFormat;
	private PresentModeKHR presentMode;
	private Extent2D extent;

	internal VkSwapchainBuilder( Instance instance, PhysicalDevice physicalDevice, Device logicalDevice )
	{
		this.instance = instance;
		this.physicalDevice = physicalDevice;
		this.logicalDevice = logicalDevice;
	}

	internal VkSwapchainBuilder WithSurface( SurfaceKHR surface, KhrSurface? surfaceExtension )
	{
		this.surface = surface;
		this.surfaceExtension = surfaceExtension;
		return this;
	}

	internal VkSwapchainBuilder WithQueueFamilyIndices( VkQueueFamilyIndices queueFamilyIndices )
	{
		this.queueFamilyIndices = queueFamilyIndices;
		return this;
	}

	internal VkSwapchainBuilder UseDefaultFormat()
	{
		swapchainFormat = Format.R8G8B8A8Srgb;
		return this;
	}

	internal VkSwapchainBuilder WithFormat( Format swapchainFormat )
	{
		this.swapchainFormat = swapchainFormat;
		return this;
	}

	internal VkSwapchainBuilder SetPresentMode( PresentModeKHR presentMode )
	{
		this.presentMode = presentMode;
		return this;
	}

	internal VkSwapchainBuilder SetExtent( Extent2D extent )
	{
		this.extent = extent;
		return this;
	}

	internal VkSwapchainBuilder SetExtent( uint width, uint height )
	{
		extent = new Extent2D( width, height );
		return this;
	}

	internal unsafe VkSwapchainBuilderResult Build()
	{
		ArgumentNullException.ThrowIfNull( surfaceExtension, nameof( surfaceExtension ) );

		surfaceExtension.GetPhysicalDeviceSurfaceCapabilities( physicalDevice, surface, out var capabilities ).Verify();
		if ( extent.Width < capabilities.MinImageExtent.Width || extent.Height < capabilities.MinImageExtent.Height )
			throw new InvalidOperationException( $"The chosen physical device does not support the extent \"{extent}\"" );

		if ( extent.Width > capabilities.MaxImageExtent.Width || extent.Height > capabilities.MaxImageExtent.Height )
			throw new InvalidOperationException( $"The chosen physical device does not support the extent \"{extent}\"" );

		uint formatCount;
		surfaceExtension.GetPhysicalDeviceSurfaceFormats( physicalDevice, surface, &formatCount, null ).Verify();
		var formats = stackalloc SurfaceFormatKHR[(int)formatCount];
		surfaceExtension.GetPhysicalDeviceSurfaceFormats( physicalDevice, surface, &formatCount, formats ).Verify();

		var hasFormat = false;
		SurfaceFormatKHR surfaceFormat = default;
		for ( var i = 0; i < formatCount; i++ )
		{
			if ( formats[i].Format != swapchainFormat )
				continue;

			hasFormat = true;
			surfaceFormat = formats[i];
			break;
		}

		if ( !hasFormat )
			throw new InvalidOperationException( $"The chosen physical device does not support the format \"{swapchainFormat}\"" );
		var surfaceImageFormat = surfaceFormat.Format;

		uint presentModeCount;
		surfaceExtension.GetPhysicalDeviceSurfacePresentModes( physicalDevice, surface, &presentModeCount, null ).Verify();
		var presentModes = stackalloc PresentModeKHR[(int)presentModeCount];
		surfaceExtension.GetPhysicalDeviceSurfacePresentModes( physicalDevice, surface, &presentModeCount, presentModes ).Verify();

		var hasPresentMode = false;
		for ( var i = 0; i < presentModeCount; i++ )
		{
			if ( presentModes[i] != presentMode )
				continue;

			hasPresentMode = true;
			break;
		}

		if ( !hasPresentMode )
			throw new InvalidOperationException( $"The chosen physical device does not support the present mode \"{presentMode}\"" );

		// FIXME: Get proper image count.
		var imageCount = capabilities.MinImageCount;

		var createInfo = new SwapchainCreateInfoKHR
		{
			SType = StructureType.SwapchainCreateInfoKhr,
			Surface = surface,
			MinImageCount = imageCount,
			ImageFormat = surfaceImageFormat,
			ImageColorSpace = surfaceFormat.ColorSpace,
			ImageExtent = extent,
			ImageArrayLayers = 1,
			ImageUsage = ImageUsageFlags.ColorAttachmentBit
		};

		var indicesArray = stackalloc uint[]
		{
			queueFamilyIndices.GraphicsQueue,
			queueFamilyIndices.PresentQueue
		};

		if ( queueFamilyIndices.GraphicsQueue != queueFamilyIndices.PresentQueue )
		{
			createInfo.ImageSharingMode = SharingMode.Concurrent;
			createInfo.QueueFamilyIndexCount = 2;
		}
		else
		{
			createInfo.ImageSharingMode = SharingMode.Exclusive;
			createInfo.QueueFamilyIndexCount = 1;
		}

		createInfo.PQueueFamilyIndices = indicesArray;
		createInfo.PreTransform = capabilities.CurrentTransform;
		createInfo.CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr;
		createInfo.PresentMode = presentMode;
		createInfo.Clipped = Vk.True;

		if ( !Apis.Vk.TryGetDeviceExtension<KhrSwapchain>( instance, logicalDevice, out var swapchainExtension ) )
			throw new VkException( "Failed to get KHR_swapchain extension" );

		swapchainExtension.CreateSwapchain( logicalDevice, createInfo, null, out var swapchain ).Verify();
		swapchainExtension.GetSwapchainImages( logicalDevice, swapchain, &imageCount, null ).Verify();

		Span<Image> swapchainImages = stackalloc Image[(int)imageCount];
		swapchainExtension.GetSwapchainImages( logicalDevice, swapchain, &imageCount, swapchainImages ).Verify();

		Span<ImageView> swapchainImageViews = stackalloc ImageView[(int)imageCount];
		for ( var i = 0; i < imageCount; i++ )
			swapchainImageViews[i] = CreateImageView( swapchainImages[i], surfaceImageFormat, ImageAspectFlags.ColorBit, 1 );

		return new VkSwapchainBuilderResult( swapchain, swapchainExtension,
			swapchainImages.ToImmutableArray(), swapchainImageViews.ToImmutableArray(), surfaceImageFormat );
	}

	private unsafe ImageView CreateImageView( Image image, Format format, ImageAspectFlags aspectFlags, uint mipLevels )
	{
		var viewInfo = new ImageViewCreateInfo
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

		Apis.Vk.CreateImageView( logicalDevice, viewInfo, null, out var imageView ).Verify();
		return imageView;
	}
}

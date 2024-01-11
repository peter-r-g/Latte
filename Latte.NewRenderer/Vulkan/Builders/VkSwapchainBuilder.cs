using Latte.NewRenderer.Vulkan.Exceptions;
using Latte.NewRenderer.Vulkan.Extensions;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using System;
using System.Collections.Immutable;

namespace Latte.NewRenderer.Vulkan.Builders;

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
		VkInvalidHandleException.ThrowIfInvalid( instance );
		VkInvalidHandleException.ThrowIfInvalid( physicalDevice );
		VkInvalidHandleException.ThrowIfInvalid( logicalDevice );

		this.instance = instance;
		this.physicalDevice = physicalDevice;
		this.logicalDevice = logicalDevice;
	}

	internal VkSwapchainBuilder WithSurface( SurfaceKHR surface, KhrSurface? surfaceExtension )
	{
		VkInvalidHandleException.ThrowIfInvalid( surface );

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
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		ArgumentNullException.ThrowIfNull( surfaceExtension, nameof( surfaceExtension ) );

		surfaceExtension.GetPhysicalDeviceSurfaceCapabilities( physicalDevice, surface, out var capabilities ).AssertSuccess();
		if ( extent.Width < capabilities.MinImageExtent.Width || extent.Height < capabilities.MinImageExtent.Height )
			throw new VkException( $"The chosen physical device does not support the extent \"{extent}\"" );

		if ( extent.Width > capabilities.MaxImageExtent.Width || extent.Height > capabilities.MaxImageExtent.Height )
			throw new VkException( $"The chosen physical device does not support the extent \"{extent}\"" );

		uint formatCount;
		surfaceExtension.GetPhysicalDeviceSurfaceFormats( physicalDevice, surface, &formatCount, null ).AssertSuccess();
		var formats = stackalloc SurfaceFormatKHR[(int)formatCount];
		surfaceExtension.GetPhysicalDeviceSurfaceFormats( physicalDevice, surface, &formatCount, formats ).AssertSuccess();

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
			throw new VkException( $"The chosen physical device does not support the format \"{swapchainFormat}\"" );
		var surfaceImageFormat = surfaceFormat.Format;

		uint presentModeCount;
		surfaceExtension.GetPhysicalDeviceSurfacePresentModes( physicalDevice, surface, &presentModeCount, null ).AssertSuccess();
		var presentModes = stackalloc PresentModeKHR[(int)presentModeCount];
		surfaceExtension.GetPhysicalDeviceSurfacePresentModes( physicalDevice, surface, &presentModeCount, presentModes ).AssertSuccess();

		var hasPresentMode = false;
		for ( var i = 0; i < presentModeCount; i++ )
		{
			if ( presentModes[i] != presentMode )
				continue;

			hasPresentMode = true;
			break;
		}

		if ( !hasPresentMode )
			throw new VkException( $"The chosen physical device does not support the present mode \"{presentMode}\"" );

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

		if ( !VkContext.Extensions.TryGetExtension<KhrSwapchain>( out var swapchainExtension ) )
			throw new VkException( $"Failed to get {KhrSwapchain.ExtensionName} extension" );

		swapchainExtension.CreateSwapchain( logicalDevice, createInfo, null, out var swapchain ).AssertSuccess();
		swapchainExtension.GetSwapchainImages( logicalDevice, swapchain, &imageCount, null ).AssertSuccess();

		Span<Image> swapchainImages = stackalloc Image[(int)imageCount];
		swapchainExtension.GetSwapchainImages( logicalDevice, swapchain, &imageCount, swapchainImages ).AssertSuccess();

		Span<ImageView> swapchainImageViews = stackalloc ImageView[(int)imageCount];
		for ( var i = 0; i < imageCount; i++ )
			swapchainImageViews[i] = CreateImageView( swapchainImages[i], surfaceImageFormat, ImageAspectFlags.ColorBit, 1 );

		return new VkSwapchainBuilderResult( swapchain,
			swapchainImages.ToImmutableArray(),
			swapchainImageViews.ToImmutableArray(),
			surfaceImageFormat );
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

		Apis.Vk.CreateImageView( logicalDevice, viewInfo, null, out var imageView ).AssertSuccess();
		return imageView;
	}
}

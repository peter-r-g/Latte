using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using System.Collections.Immutable;

namespace Latte.NewRenderer.Builders;

internal readonly struct VkSwapchainBuilderResult
{
	internal readonly SwapchainKHR Swapchain;
	internal readonly KhrSwapchain SwapchainExtension;
	internal readonly ImmutableArray<Image> SwapchainImages;
	internal readonly ImmutableArray<ImageView> SwapchainImageViews;
	internal readonly Format SwapchainImageFormat;

	internal VkSwapchainBuilderResult( SwapchainKHR swapchain, KhrSwapchain swapchainExtension,
		ImmutableArray<Image> swapchainImages, ImmutableArray<ImageView> swapchainImageViews, Format swapchainImageFormat )
	{
		Swapchain = swapchain;
		SwapchainExtension = swapchainExtension;
		SwapchainImages = swapchainImages;
		SwapchainImageViews = swapchainImageViews;
		SwapchainImageFormat = swapchainImageFormat;
	}
}

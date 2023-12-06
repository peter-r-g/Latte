using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Latte.NewRenderer.Builders;

[method: SetsRequiredMembers]
internal struct VkSwapchainBuilderResult( SwapchainKHR swapchain, KhrSwapchain swapchainExtension,
	ImmutableArray<Image> swapchainImages, ImmutableArray<ImageView> swapchainImageViews, Format swapchainImageFormat )
{
	internal required SwapchainKHR Swapchain = swapchain;
	internal required KhrSwapchain SwapchainExtension = swapchainExtension;
	internal required ImmutableArray<Image> SwapchainImages = swapchainImages;
	internal required ImmutableArray<ImageView> SwapchainImageViews = swapchainImageViews;
	internal required Format SwapchainImageFormat = swapchainImageFormat;
}

using Silk.NET.Vulkan;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Latte.NewRenderer.Builders;

[method: SetsRequiredMembers]
internal readonly struct VkSwapchainBuilderResult(
	SwapchainKHR swapchain,
	ImmutableArray<Image> swapchainImages,
	ImmutableArray<ImageView> swapchainImageViews,
	Format swapchainImageFormat )
{
	internal required SwapchainKHR Swapchain { get; init; } = swapchain;
	internal required ImmutableArray<Image> SwapchainImages { get; init; } = swapchainImages;
	internal required ImmutableArray<ImageView> SwapchainImageViews { get; init; } = swapchainImageViews;
	internal required Format SwapchainImageFormat { get; init; } = swapchainImageFormat;
}

using Silk.NET.Vulkan;
using System.Diagnostics.CodeAnalysis;

namespace Latte.NewRenderer.Renderer.Vulkan.Builders;

[method: SetsRequiredMembers]
internal readonly struct VkLogicalDeviceBuilderResult(
	Device logicalDevice,
	Queue graphicsQueue,
	uint graphicsQueueFamily,
	Queue presentQueue,
	uint presentQueueFamily,
	Queue transferQueue,
	uint transferQueueFamily )
{
	internal required Device LogicalDevice { get; init; } = logicalDevice;
	internal required Queue GraphicsQueue { get; init; } = graphicsQueue;
	internal required uint GraphicsQueueFamily { get; init; } = graphicsQueueFamily;
	internal required Queue PresentQueue { get; init; } = presentQueue;
	internal required uint PresentQueueFamily { get; init; } = presentQueueFamily;
	internal required Queue TransferQueue { get; init; } = transferQueue;
	internal required uint TransferQueueFamily { get; init; } = transferQueueFamily;
}

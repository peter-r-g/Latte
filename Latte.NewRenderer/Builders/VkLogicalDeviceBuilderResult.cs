using Silk.NET.Vulkan;
using System.Diagnostics.CodeAnalysis;

namespace Latte.NewRenderer.Builders;

[method: SetsRequiredMembers]
internal struct VkLogicalDeviceBuilderResult( Device logicalDevice, Queue graphicsQueue, uint graphicsQueueFamily,
	Queue presentQueue, uint presentQueueFamily, Queue transferQueue, uint transferQueueFamily )
{
	internal required Device LogicalDevice = logicalDevice;
	internal required Queue GraphicsQueue = graphicsQueue;
	internal required uint GraphicsQueueFamily = graphicsQueueFamily;
	internal required Queue PresentQueue = presentQueue;
	internal required uint PresentQueueFamily = presentQueueFamily;
	internal required Queue TransferQueue = transferQueue;
	internal required uint TransferQueueFamily = transferQueueFamily;
}

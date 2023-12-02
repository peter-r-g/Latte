using Silk.NET.Vulkan;

namespace Latte.NewRenderer.Builders;

internal readonly struct VkLogicalDeviceBuilderResult
{
	internal readonly Device LogicalDevice;
	internal readonly Queue GraphicsQueue;
	internal readonly uint GraphicsQueueFamily;
	internal readonly Queue PresentQueue;
	internal readonly uint PresentQueueFamily;

	internal VkLogicalDeviceBuilderResult( Device logicalDevice, Queue graphicsQueue, uint graphicsQueueFamily,
		Queue presentQueue, uint presentQueueFamily )
	{
		LogicalDevice = logicalDevice;
		GraphicsQueue = graphicsQueue;
		GraphicsQueueFamily = graphicsQueueFamily;
		PresentQueue = presentQueue;
		PresentQueueFamily = presentQueueFamily;
	}
}

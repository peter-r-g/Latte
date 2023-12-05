using Silk.NET.Vulkan;

namespace Latte.NewRenderer.Builders;

internal readonly struct VkPhysicalDeviceSelectorResult
{
	internal readonly PhysicalDevice PhysicalDevice;
	internal readonly VkQueueFamilyIndices QueueFamilyIndices;

	internal VkPhysicalDeviceSelectorResult( PhysicalDevice physicalDevice, VkQueueFamilyIndices queueFamilyIndices )
	{
		PhysicalDevice = physicalDevice;
		QueueFamilyIndices = queueFamilyIndices;
	}
}

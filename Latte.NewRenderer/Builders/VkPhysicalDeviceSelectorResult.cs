using Silk.NET.Vulkan;
using System.Diagnostics.CodeAnalysis;

namespace Latte.NewRenderer.Builders;

[method: SetsRequiredMembers]
internal struct VkPhysicalDeviceSelectorResult( PhysicalDevice physicalDevice, VkQueueFamilyIndices queueFamilyIndices )
{
	internal required PhysicalDevice PhysicalDevice = physicalDevice;
	internal required VkQueueFamilyIndices QueueFamilyIndices = queueFamilyIndices;
}

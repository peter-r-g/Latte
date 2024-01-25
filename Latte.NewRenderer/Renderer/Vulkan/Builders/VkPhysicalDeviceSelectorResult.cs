using Silk.NET.Vulkan;
using System.Diagnostics.CodeAnalysis;

namespace Latte.NewRenderer.Renderer.Vulkan.Builders;

[method: SetsRequiredMembers]
internal readonly struct VkPhysicalDeviceSelectorResult( PhysicalDevice physicalDevice, VkQueueFamilyIndices queueFamilyIndices )
{
	internal required PhysicalDevice PhysicalDevice { get; init; } = physicalDevice;
	internal required VkQueueFamilyIndices QueueFamilyIndices { get; init; } = queueFamilyIndices;
}

using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Vulkan;
using Latte.NewRenderer.Extensions;

namespace Latte.NewRenderer.Builders;

internal readonly struct VkQueueFamilyIndices
{
	internal readonly uint GraphicsQueue;
	internal readonly uint PresentQueue;

	internal VkQueueFamilyIndices( uint graphicsQueue, uint presentQueue )
	{
		GraphicsQueue = graphicsQueue;
		PresentQueue = presentQueue;
	}

	internal uint[] ToUnique()
	{
		if ( GraphicsQueue == PresentQueue )
			return [GraphicsQueue];
		else
			return [GraphicsQueue, PresentQueue];
	}

	internal static unsafe VkQueueFamilyIndices Get( PhysicalDevice physicalDevice, SurfaceKHR surface, KhrSurface? surfaceExtension,
		bool requireUniqueGraphicsQueue, bool requireUniquePresentQueue )
	{
		uint queueFamilyCount;
		Apis.Vk.GetPhysicalDeviceQueueFamilyProperties( physicalDevice, &queueFamilyCount, null );

		var queueFamilies = stackalloc QueueFamilyProperties[(int)queueFamilyCount];
		Apis.Vk.GetPhysicalDeviceQueueFamilyProperties( physicalDevice, &queueFamilyCount, queueFamilies );

		var graphicsQueueIndex = uint.MaxValue;
		var presentQueueIndex = uint.MaxValue;
		for ( uint i = 0; i < queueFamilyCount; i++ )
		{
			var queueFamily = queueFamilies[i];
			if ( queueFamily.QueueFlags.HasFlag( QueueFlags.GraphicsBit ) )
			{
				graphicsQueueIndex = i;
				if ( requireUniqueGraphicsQueue )
					continue;
			}

			if ( surfaceExtension is not null )
			{
				if ( requireUniquePresentQueue && graphicsQueueIndex == i )
					continue;

				surfaceExtension.GetPhysicalDeviceSurfaceSupport( physicalDevice, i, surface, out var presentSupported ).Verify();
				if ( presentSupported )
				{
					presentQueueIndex = i;
					continue;
				}
			}
		}

		return new VkQueueFamilyIndices( graphicsQueueIndex, presentQueueIndex );
	}
}

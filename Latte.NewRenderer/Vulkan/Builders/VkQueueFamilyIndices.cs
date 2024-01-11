using Latte.NewRenderer.Vulkan.Extensions;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Latte.NewRenderer.Vulkan.Builders;

internal readonly struct VkQueueFamilyIndices
{
	internal required uint GraphicsQueue { get; init; }
	internal required uint PresentQueue { get; init; }
	internal required uint TransferQueue { get; init; }

	internal required ReadOnlyMemory<uint> UniqueQueues { get; init; }

	[SetsRequiredMembers]
	internal VkQueueFamilyIndices( uint graphicsQueue, uint presentQueue, uint transferQueue )
	{
		GraphicsQueue = graphicsQueue;
		PresentQueue = presentQueue;
		TransferQueue = transferQueue;

		var uniqueQueues = new HashSet<uint>
		{
			GraphicsQueue,
			PresentQueue,
			TransferQueue
		};
		UniqueQueues = uniqueQueues.ToArray();
	}

	internal static unsafe VkQueueFamilyIndices Get( PhysicalDevice physicalDevice, SurfaceKHR surface, KhrSurface? surfaceExtension,
		bool requireUniqueGraphicsQueue, bool requireUniquePresentQueue, bool requireUniqueTransferQueue )
	{
		ArgumentNullException.ThrowIfNull( surfaceExtension, nameof( surfaceExtension ) );

		uint queueFamilyCount;
		Apis.Vk.GetPhysicalDeviceQueueFamilyProperties( physicalDevice, &queueFamilyCount, null );

		var queueFamilies = stackalloc QueueFamilyProperties[(int)queueFamilyCount];
		Apis.Vk.GetPhysicalDeviceQueueFamilyProperties( physicalDevice, &queueFamilyCount, queueFamilies );

		var graphicsQueueIndex = uint.MaxValue;
		var presentQueueIndex = uint.MaxValue;
		var transferQueueIndex = uint.MaxValue;

		// Graphics queue.
		for ( uint i = 0; i < queueFamilyCount; i++ )
		{
			var queueFamily = queueFamilies[i];
			if ( !queueFamily.QueueFlags.HasFlag( QueueFlags.GraphicsBit ) )
				continue;

			graphicsQueueIndex = i;
			break;
		}

		// Present queue.
		for ( uint i = 0; i < queueFamilyCount; i++ )
		{
			if ( (requireUniqueGraphicsQueue || requireUniquePresentQueue) && graphicsQueueIndex == i )
				continue;

			surfaceExtension.GetPhysicalDeviceSurfaceSupport( physicalDevice, i, surface, out var presentSupported ).Verify();
			if ( !presentSupported )
				continue;

			presentQueueIndex = i;
			break;
		}

		// Transfer queue.
		for ( uint i = 0; i < queueFamilyCount; i++ )
		{
			if ( (requireUniqueGraphicsQueue || requireUniqueTransferQueue) && graphicsQueueIndex == i )
				continue;

			if ( (requireUniquePresentQueue || requireUniqueTransferQueue) && presentQueueIndex == i )
				continue;

			var queueFamily = queueFamilies[i];
			if ( !queueFamily.QueueFlags.HasFlag( QueueFlags.TransferBit ) )
				continue;

			transferQueueIndex = i;
			break;
		}

		return new VkQueueFamilyIndices( graphicsQueueIndex, presentQueueIndex, transferQueueIndex );
	}
}

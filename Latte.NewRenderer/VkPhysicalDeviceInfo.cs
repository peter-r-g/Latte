using Latte.NewRenderer.Exceptions;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using System.Diagnostics.CodeAnalysis;

namespace Latte.NewRenderer;

internal readonly struct VkPhysicalDeviceInfo
{
	internal required string Name { get; init; }
	internal required PhysicalDeviceFeatures Features { get; init; }
	internal required PhysicalDeviceProperties Properties { get; init; }
	internal required PhysicalDeviceMemoryProperties MemoryProperties { get; init; }

	[SetsRequiredMembers]
	internal unsafe VkPhysicalDeviceInfo( PhysicalDevice physicalDevice )
	{
		Features = Apis.Vk.GetPhysicalDeviceFeatures( physicalDevice );
		var properties = Apis.Vk.GetPhysicalDeviceProperties( physicalDevice );
		Properties = properties;
		MemoryProperties = Apis.Vk.GetPhysicalDeviceMemoryProperties( physicalDevice );

		var nameStr = SilkMarshal.PtrToString( (nint)properties.DeviceName );
		if ( nameStr is null )
			throw new VkException( "Failed to get name of physical device" );

		Name = nameStr;
	}
}

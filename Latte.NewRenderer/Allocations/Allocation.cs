using Silk.NET.Vulkan;

namespace Latte.NewRenderer.Allocations;

internal readonly struct Allocation
{
	internal readonly DeviceMemory Memory;
	internal readonly ulong Offset;

	internal Allocation( DeviceMemory memory, ulong offset )
	{
		Memory = memory;
		Offset = offset;
	}
}

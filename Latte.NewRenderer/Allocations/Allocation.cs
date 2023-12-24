using Silk.NET.Vulkan;
using System.Diagnostics.CodeAnalysis;

namespace Latte.NewRenderer.Allocations;

[method: SetsRequiredMembers]
internal struct Allocation( DeviceMemory memory, ulong offset, ulong size )
{
	internal required DeviceMemory Memory = memory;
	internal required ulong Offset = offset;
	internal required ulong Size = size;
}

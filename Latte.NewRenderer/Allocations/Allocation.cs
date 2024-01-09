using Silk.NET.Vulkan;
using System.Diagnostics.CodeAnalysis;

namespace Latte.NewRenderer.Allocations;

[method: SetsRequiredMembers]
internal struct Allocation( DeviceMemory memory, uint memoryTypeBits, ulong offset, ulong size )
{
	internal required DeviceMemory Memory = memory;
	internal required uint MemoryTypeBits = memoryTypeBits;
	internal required ulong Offset = offset;
	internal required ulong Size = size;
}

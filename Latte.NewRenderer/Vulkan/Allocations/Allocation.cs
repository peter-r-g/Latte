using Silk.NET.Vulkan;
using System.Diagnostics.CodeAnalysis;

namespace Latte.NewRenderer.Vulkan.Allocations;

[method: SetsRequiredMembers]
internal readonly struct Allocation( int id, DeviceMemory memory, uint memoryType, ulong offset, ulong size )
{
	internal required int Id { get; init; } = id;
	internal required DeviceMemory Memory { get; init; } = memory;
	internal required uint MemoryType { get; init; } = memoryType;
	internal required ulong Offset { get; init; } = offset;
	internal required ulong Size { get; init; } = size;
}

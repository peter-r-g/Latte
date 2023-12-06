using Silk.NET.Vulkan;
using System.Diagnostics.CodeAnalysis;

namespace Latte.NewRenderer.Allocations;

[method: SetsRequiredMembers]
internal struct AllocatedBuffer( Buffer buffer, Allocation allocation )
{
	internal required Buffer Buffer = buffer;
	internal required Allocation Allocation = allocation;
}

using Silk.NET.Vulkan;
using System.Diagnostics.CodeAnalysis;

namespace Latte.Windowing.Renderer.Vulkan.Allocations;

[method: SetsRequiredMembers]
internal readonly struct PoolSizeRatio( DescriptorType type, float ratio )
{
	internal required DescriptorType Type { get; init; } = type;
	internal required float Ratio { get; init; } = ratio;
}

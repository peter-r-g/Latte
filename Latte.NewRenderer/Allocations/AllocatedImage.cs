using Silk.NET.Vulkan;
using System.Diagnostics.CodeAnalysis;

namespace Latte.NewRenderer.Allocations;

[method: SetsRequiredMembers]
internal struct AllocatedImage( Image image, Allocation allocation )
{
	internal required Image Image = image;
	internal required Allocation Allocation = allocation;
}

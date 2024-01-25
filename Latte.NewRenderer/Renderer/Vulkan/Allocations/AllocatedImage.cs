using Silk.NET.Vulkan;
using System.Diagnostics.CodeAnalysis;
using VMASharp;

namespace Latte.Windowing.Renderer.Vulkan.Allocations;

[method: SetsRequiredMembers]
internal struct AllocatedImage( Image image, Allocation allocation )
{
	internal required Image Image = image;
	internal required Allocation Allocation = allocation;
}

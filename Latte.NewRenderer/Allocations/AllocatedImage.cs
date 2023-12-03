using Silk.NET.Vulkan;

namespace Latte.NewRenderer.Allocations;

internal readonly struct AllocatedImage
{
	internal readonly Image Image;
	internal readonly Allocation Allocation;

	internal AllocatedImage( Image image, Allocation allocation )
	{
		Image = image;
		Allocation = allocation;
	}
}

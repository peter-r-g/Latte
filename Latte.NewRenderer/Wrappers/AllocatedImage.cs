using Silk.NET.Vulkan;

namespace Latte.NewRenderer.Wrappers;

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

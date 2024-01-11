using Latte.NewRenderer.Vulkan.Allocations;
using Silk.NET.Vulkan;
using SixLabors.ImageSharp.PixelFormats;
using System;

namespace Latte.NewRenderer.Vulkan.Temp;

internal sealed class Texture
{
	internal uint Width { get; }
	internal uint Height { get; }
	internal uint BytesPerPixel { get; }
	internal ReadOnlyMemory<Rgba32> PixelData { get; }

	internal AllocatedImage GpuTexture { get; set; }
	internal ImageView TextureView { get; set; }

	internal Texture( uint width, uint height, uint bytesPerPixel, ReadOnlyMemory<Rgba32> pixelData )
	{
		Width = width;
		Height = height;
		BytesPerPixel = bytesPerPixel;
		PixelData = pixelData;
	}
}

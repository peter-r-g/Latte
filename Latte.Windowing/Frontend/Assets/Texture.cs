using Latte.Windowing.Backend.Vulkan;
using Silk.NET.Vulkan;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;

namespace Latte.Windowing.Assets;

internal sealed class Texture
{
	public ReadOnlyMemory<Rgba32> PixelData { get; }
	public int BytesPerPixel { get; }
	public int Width { get; }
	public int Height { get; }
	public uint MipLevels { get; }

	internal VulkanImage? VulkanImage { get; private set; }

	/// <summary>
	/// Flags that represent what renderers have initialized this textures renderer specific data.
	/// </summary>
	internal RenderingBackend InitializedFlags { get; private set; }

	private Texture( Image<Rgba32> image )
	{
		BytesPerPixel = image.PixelType.BitsPerPixel / 8;
		var pixelData = new Rgba32[image.Width * image.Height];
		image.CopyPixelDataTo( pixelData );
		PixelData = pixelData;
		Width = image.Width;
		Height = image.Height;
		MipLevels = (uint)MathF.Floor( MathF.Log2( Math.Max( image.Width, image.Height ) ) ) + 1;
	}

	/// <summary>
	/// Initializes renderer specific data on a texture.
	/// </summary>
	/// <param name="backend">The renderer to initialize for.</param>
	public void Initialize( IRenderingBackend backend )
	{
		if ( backend is not VulkanBackend vulkanBackend )
			return;

		if ( InitializedFlags.HasFlag( RenderingBackend.Vulkan ) )
			throw new InvalidOperationException( "This texture has already been initialized for usage in Vulkan" );

		using var stagingBuffer = vulkanBackend.GetCPUBuffer( (ulong)(Width * Height), BufferUsageFlags.TransferSrcBit );
		stagingBuffer.SetMemory( PixelData.Span );

		VulkanImage = vulkanBackend.CreateImage( (uint)Width, (uint)Height, MipLevels );
		var commandBuffer = vulkanBackend.BeginOneTimeCommands();
		VulkanImage.TransitionImageLayout( commandBuffer, Format.R8G8B8A8Srgb, ImageLayout.Undefined, ImageLayout.TransferDstOptimal, MipLevels );
		vulkanBackend.EndOneTimeCommands( commandBuffer );

		InitializedFlags |= RenderingBackend.Vulkan;
	}

	/// <summary>
	/// Returns a new texture that is loaded from disk.
	/// </summary>
	/// <param name="modelPath">The path to the texture.</param>
	/// <returns>The parsed texture from disk.</returns>
	public static Texture FromPath( string modelPath )
	{
		using var image = SixLabors.ImageSharp.Image.Load<Rgba32>( modelPath );
		return new Texture( image );
	}
}

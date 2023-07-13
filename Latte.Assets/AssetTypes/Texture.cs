using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Diagnostics.CodeAnalysis;
using Zio;

namespace Latte.Assets;

public sealed class Texture
{
	public required ReadOnlyMemory<Rgba32> PixelData { get; init; }
	public required int BytesPerPixel { get; init; }
	public required int Width { get; init; }
	public required int Height { get; init; }
	public required uint MipLevels { get; init; }

	[SetsRequiredMembers]
	private Texture( Image<Rgba32> image )
	{
		var pixelData = new Rgba32[image.Width * image.Height];
		image.CopyPixelDataTo( pixelData );

		BytesPerPixel = image.PixelType.BitsPerPixel / 8;
		PixelData = pixelData;
		Width = image.Width;
		Height = image.Height;
		MipLevels = (uint)MathF.Floor( MathF.Log2( Math.Max( image.Width, image.Height ) ) ) + 1;
	}

	/// <summary>
	/// Returns a new texture that is loaded from disk.
	/// </summary>
	/// <param name="path">The path to the texture.</param>
	/// <returns>The parsed texture from disk.</returns>
	public static Texture FromPath( in UPath path )
	{
		var absolutePath = FileSystems.Assets.ConvertPathToInternal( path );
		using var image = Image.Load<Rgba32>( absolutePath );
		return new Texture( image );
	}
}

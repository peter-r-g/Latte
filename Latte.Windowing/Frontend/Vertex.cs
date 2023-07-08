using Silk.NET.Vulkan;
using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Latte.Windowing;

/// <summary>
/// Represents a point that can be rendered among other vertices.
/// </summary>
public struct Vertex : IEquatable<Vertex>
{
	public Vector3 Position;
	public Vector3 Color;
	public Vector2 TextureCoordinates;

	public Vertex( in Vector3 position, in Vector3 color, in Vector2 textureCoordinates )
	{
		Position = position;
		Color = color;
		TextureCoordinates = textureCoordinates;
	}

	/// <inheritdoc/>
	public override readonly int GetHashCode()
	{
		return HashCode.Combine( Position, Color, TextureCoordinates );
	}

	/// <inheritdoc/>
	public override readonly bool Equals( object? obj )
	{
		return obj is Vertex other && Equals( other );
	}

	/// <inheritdoc/>
	public readonly bool Equals( Vertex other )
	{
		return Position == other.Position &&
			Color == other.Color &&
			TextureCoordinates == other.TextureCoordinates;
	}

	public static bool operator ==( in Vertex left, in Vertex right ) => left.Equals( right );
	public static bool operator !=( in Vertex left, in Vertex right ) => !(left == right);

	/// <summary>
	/// Returns all binding descriptions of the <see cref="Vertex"/>.
	/// </summary>
	/// <returns>The binding descriptions of the <see cref="Vertex"/>.</returns>
	internal static unsafe VertexInputBindingDescription[] GetBindingDescriptions()
	{
		return new VertexInputBindingDescription[]
		{
			new VertexInputBindingDescription
			{
				Binding = 0,
			Stride = (uint)sizeof( Vertex ),
			InputRate = VertexInputRate.Vertex
			}
		};
	}

	/// <summary>
	/// Returns all attribute descriptions of the <see cref="Vertex"/>.
	/// </summary>
	/// <returns>All attribute descriptions of the <see cref="Vertex"/>.</returns>
	internal static VertexInputAttributeDescription[] GetAttributeDescriptions()
	{
		return new VertexInputAttributeDescription[]
		{
			new VertexInputAttributeDescription()
			{
				Binding = 0,
				Location = 0,
				Format = Format.R32G32B32Sfloat,
				Offset = (uint)Marshal.OffsetOf<Vertex>( nameof( Position ) )
			},
			new VertexInputAttributeDescription()
			{
				Binding = 0,
				Location = 1,
				Format = Format.R32G32B32Sfloat,
				Offset = (uint)Marshal.OffsetOf<Vertex>( nameof( Color ) )
			},
			new VertexInputAttributeDescription()
			{
				Binding = 0,
				Location = 2,
				Format = Format.R32G32Sfloat,
				Offset = (uint)Marshal.OffsetOf<Vertex>( nameof( TextureCoordinates ) )
			}
		};
	}
}

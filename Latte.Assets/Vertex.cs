using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Latte;

/// <summary>
/// Represents a point that can be rendered among other vertices.
/// </summary>
public struct Vertex : IEquatable<Vertex>
{
	public required Vector3 Position;
	public required Vector3 Normal;
	public required Vector3 Color;
	public required Vector2 TextureCoordinates;

	[SetsRequiredMembers]
	public Vertex( in Vector3 position, in Vector3 normal, in Vector3 color, in Vector2 textureCoordinates )
	{
		Position = position;
		Normal = normal;
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
}

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Latte.Assets;

/// <summary>
/// Represents a mesh of a 3D model.
/// </summary>
public sealed class Mesh
{
	/// <summary>
	/// The vertices of the mesh.
	/// </summary>
	public required ImmutableArray<Vertex> Vertices { get; init; }
	/// <summary>
	/// The indices of the mesh.
	/// </summary>
	public required ImmutableArray<uint> Indices { get; init; }

	/// <summary>
	/// Initializes a new instance of <see cref="Mesh"/>.
	/// </summary>
	/// <param name="vertices">The vertices of the mesh.</param>
	/// <param name="indices">The indices of the mesh.</param>
	[SetsRequiredMembers]
	public Mesh( in ImmutableArray<Vertex> vertices, in ImmutableArray<uint> indices )
	{
		Vertices = vertices;
		Indices = indices;
	}

	/// <summary>
	/// Initializes a new instance of <see cref="Mesh"/>.
	/// </summary>
	/// <param name="vertices">The vertices of the mesh.</param>
	[SetsRequiredMembers]
	public Mesh( in ImmutableArray<Vertex> vertices )
	{
		Vertices = vertices;
		Indices = ImmutableArray<uint>.Empty;
	}
}

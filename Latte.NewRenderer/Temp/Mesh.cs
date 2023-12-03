using Latte.NewRenderer.Allocations;
using System.Collections.Immutable;

namespace Latte.NewRenderer.Temp;

internal sealed class Mesh
{
	internal readonly ImmutableArray<Vertex> Vertices = [];
	internal readonly ImmutableArray<uint> Indices = [];
	internal AllocatedBuffer VertexBuffer;
	internal AllocatedBuffer IndexBuffer;

	internal Mesh( ImmutableArray<Vertex> vertices, ImmutableArray<uint> indices )
	{
		Vertices = vertices;
		Indices = indices;
	}
}

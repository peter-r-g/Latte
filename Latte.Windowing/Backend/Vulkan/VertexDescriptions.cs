using Silk.NET.Vulkan;
using System.Runtime.InteropServices;

namespace Latte.Windowing.Backend.Vulkan;

internal static class VertexDescriptions
{
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
				Offset = (uint)Marshal.OffsetOf<Vertex>( nameof( Vertex.Position ) )
			},
			new VertexInputAttributeDescription()
			{
				Binding = 0,
				Location = 1,
				Format = Format.R32G32B32Sfloat,
				Offset = (uint)Marshal.OffsetOf<Vertex>( nameof( Vertex.Color ) )
			},
			new VertexInputAttributeDescription()
			{
				Binding = 0,
				Location = 2,
				Format = Format.R32G32Sfloat,
				Offset = (uint)Marshal.OffsetOf<Vertex>( nameof( Vertex.TextureCoordinates ) )
			}
		};
	}
}

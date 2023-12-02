using Silk.NET.Vulkan;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Latte.NewRenderer;

internal struct VertexInputDescription
{
	internal VertexInputAttributeDescription[] Attributes = [];
	internal VertexInputBindingDescription[] Bindings = [];

	public VertexInputDescription()
	{
	}

	internal static VertexInputDescription GetVertexDescription()
	{
		var mainBinding = new VertexInputBindingDescription
		{
			Binding = 0,
			Stride = (uint)Unsafe.SizeOf<Vertex>(),
			InputRate = VertexInputRate.Vertex
		};

		var positionAttribute = new VertexInputAttributeDescription
		{
			Binding = 0,
			Location = 0,
			Format = Format.R32G32B32Sfloat,
			Offset = (uint)Marshal.OffsetOf<Vertex>( nameof( Vertex.Position ) )
		};

		var normalAttribute = new VertexInputAttributeDescription
		{
			Binding = 0,
			Location = 1,
			Format = Format.R32G32B32Sfloat,
			Offset = (uint)Marshal.OffsetOf<Vertex>( nameof( Vertex.Normal ) )
		};

		var colorAttribute = new VertexInputAttributeDescription
		{
			Binding = 0,
			Location = 2,
			Format = Format.R32G32B32Sfloat,
			Offset = (uint)Marshal.OffsetOf<Vertex>( nameof( Vertex.Color ) )
		};

		return new VertexInputDescription
		{
			Attributes = [positionAttribute, normalAttribute, colorAttribute],
			Bindings = [mainBinding]
		};
	}
}

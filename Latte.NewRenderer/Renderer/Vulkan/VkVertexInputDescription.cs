using ImGuiNET;
using Silk.NET.Vulkan;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Latte.Windowing.Renderer.Vulkan;

[method: SetsRequiredMembers]
internal readonly struct VkVertexInputDescription( VertexInputAttributeDescription[] attributes, VertexInputBindingDescription[] bindings )
{
	internal required VertexInputAttributeDescription[] Attributes { get; init; } = attributes;
	internal required VertexInputBindingDescription[] Bindings { get; init; } = bindings;

	internal static VkVertexInputDescription Empty()
	{
		return new VkVertexInputDescription( [], [] );
	}

	internal static VkVertexInputDescription GetLatteVertexDescription()
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

		var textureCoordinatesAttribute = new VertexInputAttributeDescription
		{
			Binding = 0,
			Location = 3,
			Format = Format.R32G32Sfloat,
			Offset = (uint)Marshal.OffsetOf<Vertex>( nameof( Vertex.TextureCoordinates ) )
		};

		return new VkVertexInputDescription( [positionAttribute, normalAttribute, colorAttribute, textureCoordinatesAttribute], [mainBinding] );
	}

	internal static VkVertexInputDescription GetImGuiVertexDescription()
	{
		var mainBinding = new VertexInputBindingDescription
		{
			Binding = 0,
			Stride = (uint)Unsafe.SizeOf<ImDrawVert>(),
			InputRate = VertexInputRate.Vertex
		};

		var positionAttribute = new VertexInputAttributeDescription
		{
			Binding = 0,
			Location = 0,
			Format = Format.R32G32Sfloat,
			Offset = (uint)Marshal.OffsetOf<ImDrawVert>( nameof( ImDrawVert.pos ) )
		};

		var textureCoordinatesAttribute = new VertexInputAttributeDescription
		{
			Binding = 0,
			Location = 1,
			Format = Format.R32G32Sfloat,
			Offset = (uint)Marshal.OffsetOf<ImDrawVert>( nameof( ImDrawVert.uv ) )
		};

		var colorAttribute = new VertexInputAttributeDescription
		{
			Binding = 0,
			Location = 2,
			Format = Format.R8G8B8A8Unorm,
			Offset = (uint)Marshal.OffsetOf<ImDrawVert>( nameof( ImDrawVert.col ) )
		};

		return new VkVertexInputDescription( [positionAttribute, textureCoordinatesAttribute, colorAttribute], [mainBinding] );
	}
}

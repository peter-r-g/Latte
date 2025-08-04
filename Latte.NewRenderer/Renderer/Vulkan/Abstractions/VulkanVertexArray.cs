using Latte.Windowing.Renderer.Abstractions;
using System.Collections.Generic;

namespace Latte.Windowing.Renderer.Vulkan.Abstractions;

internal sealed class VulkanVertexArray : VertexArray
{
	internal override IEnumerable<VertexBuffer> VertexBuffers => vertexBuffers;
	private readonly List<VertexBuffer> vertexBuffers = [];

	internal override IndexBuffer? IndexBuffer { get; set; }

	internal override void AddVertexBuffer( VertexBuffer vbo )
	{
		vertexBuffers.Add( vbo );
	}

	internal override void Bind()
	{
		foreach ( var vertexBuffer in vertexBuffers )
			vertexBuffer.Bind();

		IndexBuffer?.Bind();
	}

	internal override void Unbind()
	{
		// Don't do anything for Vulkan.
	}
}

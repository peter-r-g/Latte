using System;
using System.Collections.Generic;

namespace Latte.NewRenderer.Renderer.Abstractions;

internal abstract class VertexArray
{
	internal abstract IEnumerable<VertexBuffer> VertexBuffers { get; }
	internal abstract IndexBuffer IndexBuffer { get; set; }

	internal abstract void Bind();
	internal abstract void Unbind();

	internal abstract void AddVertexBuffer( VertexBuffer vbo );

	internal static VertexArray Create() => RendererApi.Current.Api switch
	{
		RenderApi.None => throw new NotImplementedException(),
		RenderApi.Vulkan => throw new NotImplementedException(),
		_ => throw new InvalidOperationException( "" )
	};
}

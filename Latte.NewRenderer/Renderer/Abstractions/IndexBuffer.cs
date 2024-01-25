using System;

namespace Latte.Windowing.Renderer.Abstractions;

internal abstract class IndexBuffer
{
	internal abstract uint Count { get; }

	internal abstract void Bind();
	internal abstract void Unbind();

	internal static IndexBuffer Create( RendererApi rendererApi ) => rendererApi.Api switch
	{
		RenderApi.None => throw new NotImplementedException(),
		RenderApi.Vulkan => throw new NotImplementedException(),
		_ => throw new InvalidOperationException( "" )
	};
}

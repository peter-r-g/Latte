namespace Latte.NewRenderer.Renderer.Abstractions;

internal abstract class VertexBuffer
{
	internal abstract uint Count { get; }

	internal abstract void Bind();
	internal abstract void Unbind();
}

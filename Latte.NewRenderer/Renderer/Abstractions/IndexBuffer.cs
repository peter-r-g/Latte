namespace Latte.Windowing.Renderer.Abstractions;

internal abstract class IndexBuffer
{
	internal abstract uint Count { get; }

	internal abstract void Bind();
	internal abstract void Unbind();
}

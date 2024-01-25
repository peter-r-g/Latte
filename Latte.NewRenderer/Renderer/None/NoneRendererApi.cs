using System.Numerics;
using Latte.Windowing.Renderer.Abstractions;

namespace Latte.Windowing.Renderer.None;

internal sealed class NoneRendererApi : RendererApi
{
	internal override RenderApi Api => RenderApi.None;
	internal override RenderMode Mode { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

	internal override Vector4 ClearColor { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
	internal override Vector4 Viewport { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

	internal override void Clear()
	{
		throw new System.NotImplementedException();
	}

	internal override void DrawIndexed( VertexArray vao, uint indexCount )
	{
		throw new System.NotImplementedException();
	}

	internal override void Initialize()
	{
		throw new System.NotImplementedException();
	}
}

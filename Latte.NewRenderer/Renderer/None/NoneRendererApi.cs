using Latte.Windowing.Renderer.Abstractions;

namespace Latte.Windowing.Renderer.None;

internal sealed class NoneRendererApi : RendererApi
{
	internal override RenderApi Api => RenderApi.None;
	internal override RenderMode Mode { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

	internal override Viewport Viewport { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

	internal override void StartFrame( Camera camera )
	{
		throw new System.NotImplementedException();
	}

	internal override void EndFrame()
	{
		throw new System.NotImplementedException();
	}

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

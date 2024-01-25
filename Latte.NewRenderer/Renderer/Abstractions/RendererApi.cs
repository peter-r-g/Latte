using Latte.Windowing.Renderer.None;
using System;
using System.Numerics;

namespace Latte.Windowing.Renderer.Abstractions;

internal abstract class RendererApi : IDisposable
{
	internal static RendererApi Current { get; set; } = new NoneRendererApi();

	internal abstract RenderApi Api { get; }
	internal abstract RenderMode Mode { get; set; }
	internal abstract Vector4 ClearColor { get; set; }
	internal abstract Viewport Viewport { get; set; }

	internal abstract void Initialize();

	internal abstract void Clear();

	internal abstract void DrawIndexed( VertexArray vao, uint indexCount );

	protected virtual void Dispose( bool disposing )
	{
	}

	public void Dispose()
	{
		Dispose( disposing: true );
		GC.SuppressFinalize( this );
	}
}

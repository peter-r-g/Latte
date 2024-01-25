using System;

namespace Latte.Windowing.Renderer.Abstractions;

internal abstract class RendererApi : IDisposable
{
	internal abstract RenderApi Api { get; }
	internal abstract RenderMode Mode { get; set; }
	internal abstract Viewport Viewport { get; set; }

	internal abstract void Initialize();

	internal abstract void StartFrame( Camera camera );
	internal abstract void EndFrame();

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

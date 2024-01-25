namespace Latte.NewRenderer.Renderer.Abstractions;

internal static class RenderCommand
{
	internal static void Clear() => RendererApi.Current.Clear();
	internal static void DrawIndexed( VertexArray vao, uint indexCount ) => RendererApi.Current.DrawIndexed( vao, indexCount );
}

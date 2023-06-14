namespace Latte.Windowing.Backend;

internal interface IInternalRenderingBackend : IRenderingBackend
{
	delegate void RenderHandler( double dt );
	event RenderHandler? Render;

	void Inititalize();
	void Cleanup();
	void DrawFrame( double dt );
	void WaitForIdle();
}

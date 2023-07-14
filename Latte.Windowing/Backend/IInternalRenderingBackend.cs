namespace Latte.Windowing.Backend;

internal interface IInternalRenderingBackend : IRenderingBackend
{
	void Inititalize();
	void Cleanup();
	void BeginFrame();
	void EndFrame();
	void WaitForIdle();
}

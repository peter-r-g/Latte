namespace Latte.Windowing.Renderer.Abstractions;

internal abstract class RenderCommandBuffer
{
	internal abstract void Begin();
	internal abstract void End();
	internal abstract void Submit();
}

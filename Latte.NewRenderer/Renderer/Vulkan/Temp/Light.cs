using System.Numerics;

namespace Latte.NewRenderer.Renderer.Vulkan.Temp;

internal sealed class Light
{
	internal Vector3 Position { get; set; }
	internal Vector4 Color { get; set; } // W is intensity.
}

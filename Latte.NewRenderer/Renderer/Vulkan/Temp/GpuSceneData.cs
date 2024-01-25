using System.Numerics;

namespace Latte.Windowing.Renderer.Vulkan.Temp;

internal struct GpuSceneData
{
	internal Vector4 AmbientLightColor; // W for intensity.
	internal int LightCount;
}

using System.Numerics;

namespace Latte.NewRenderer.Temp;

internal struct GpuSceneData
{
	internal Vector4 AmbientLightColor; // W for intensity.
	internal int LightCount;
}

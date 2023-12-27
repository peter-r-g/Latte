using System.Numerics;

namespace Latte.NewRenderer.Temp;

internal struct GpuSceneData
{
	internal Vector4 AmbientLightColor; // W for intensity.
	internal Vector4 SunPosition; // W is ignored.
	internal Vector4 SunLightColor; // W for intensity.
}

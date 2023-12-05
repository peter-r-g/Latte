using System.Numerics;

namespace Latte.NewRenderer.Temp;

internal struct GpuSceneData
{
	internal Vector4 FogColor; // w is for exponent.
	internal Vector4 FogDistances; // x for min, y for max, z and w unused.
	internal Vector4 AmbientColor;
	internal Vector4 SunlightDirection; // W for sun intensity.
	internal Vector4 SunlighColor;
}

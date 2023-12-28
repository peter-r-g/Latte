using System.Numerics;

namespace Latte.NewRenderer.Temp;

internal struct GpuLightData
{
	internal Vector4 LightPosition; // W is ignored.
	internal Vector4 LightColor; // W is intensity.

	internal GpuLightData( Vector3 lightPosition, Vector4 lightColor )
	{
		LightPosition = new Vector4( lightPosition, 0 );
		LightColor = lightColor;
	}
}

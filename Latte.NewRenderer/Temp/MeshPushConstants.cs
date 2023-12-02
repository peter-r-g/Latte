using System.Numerics;

namespace Latte.NewRenderer.Temp;

internal struct MeshPushConstants
{
	internal Vector4 Data;
	internal Matrix4x4 RenderMatrix;

	internal MeshPushConstants( Vector4 data, Matrix4x4 renderMatrix )
	{
		Data = data;
		RenderMatrix = renderMatrix;
	}
}

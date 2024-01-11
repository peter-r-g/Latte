using System.Numerics;

namespace Latte.NewRenderer.Vulkan.Temp;

internal struct GpuCameraData
{
	internal Matrix4x4 View;
	internal Matrix4x4 Projection;
	internal Matrix4x4 ViewProjection;
}

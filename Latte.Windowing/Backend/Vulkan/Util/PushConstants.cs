using System.Numerics;

namespace Latte.Windowing.Backend.Vulkan;

internal readonly struct PushConstants
{
	internal readonly Matrix4x4 Model;

	internal PushConstants( Matrix4x4 model )
	{
		Model = model;
	}
}

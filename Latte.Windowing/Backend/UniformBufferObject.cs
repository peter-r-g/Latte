using System.Numerics;

namespace Latte.Windowing.Backend;

internal readonly struct UniformBufferObject
{
	internal readonly Matrix4x4 View;
	internal readonly Matrix4x4 Projection;

	internal UniformBufferObject( in Matrix4x4 view, in Matrix4x4 projection, bool flipY = true )
	{
		View = view;
		Projection = projection;
		
		// Vulkan needs the Y value flipped when compared to other solutions.
		if ( flipY )
			Projection.M22 *= -1;
	}
}

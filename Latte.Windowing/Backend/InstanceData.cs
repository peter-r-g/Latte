using System.Numerics;

namespace Latte.Windowing.Backend;

internal readonly struct InstanceData
{
	internal readonly Vector3 Position;
	internal readonly Vector3 Rotation;
	internal readonly float Scale;

	internal InstanceData( in Vector3 position, in Vector3 rotation, float scale )
	{
		Position = position;
		Rotation = rotation;
		Scale = scale;
	}
}

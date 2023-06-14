using System.Numerics;

namespace Latte.Windowing;

/// <summary>
/// Contains data to modify the current view into the scene.
/// </summary>
public static class Camera
{
	public static Vector3 Position { get; set; } = new(0, 0, 3);
	public static Vector3 Direction { get; set; } = Vector3.Zero;
	public static Vector3 Front => Vector3.Normalize( Direction );
	public static Vector3 Up => Vector3.UnitY;

	public static float Yaw { get; set; } = -90;
	public static float Pitch { get; set; } = 0;
	public static float Zoom { get; set; } = 90;

	public static float ZNear { get; set; } = 0.1f;
	public static float ZFar { get; set; } = 10000;
}

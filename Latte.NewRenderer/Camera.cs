
using System;
using System.Numerics;

namespace Latte.NewRenderer;

/// <summary>
/// Contains data to modify the current view into the scene.
/// </summary>
public class Camera
{
	public static Camera Main
	{
		get => main;
		set
		{
			ArgumentNullException.ThrowIfNull( value, nameof( value ) );
			main = value;
		}
	}
	private static Camera main = new();

	public Vector3 Position { get; set; } = Vector3.Zero;
	public Vector3 Direction { get; set; } = Vector3.Zero;
	public Vector3 Front => Vector3.Normalize( Direction );
	public Vector3 Up => Vector3.UnitY;

	public float Yaw { get; set; } = 0;
	public float Pitch { get; set; } = 0;
	public float Zoom { get; set; } = 90;

	public float ZNear { get; set; } = 0.1f;
	public float ZFar { get; set; } = 10000;

	public Vector3 ClearColor { get; set; } = Vector3.Zero;
}

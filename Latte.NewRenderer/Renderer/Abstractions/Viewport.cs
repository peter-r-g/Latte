using System.Numerics;

namespace Latte.Windowing.Renderer.Abstractions;

internal struct Viewport
{
	internal Vector2 Position { get; set; }
	internal Vector2 Size { get; set; }
	internal Vector2 Depth { get; set; }
}

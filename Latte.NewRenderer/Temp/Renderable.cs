using System.Numerics;

namespace Latte.NewRenderer.Temp;

internal sealed class Renderable
{
	internal Mesh Mesh { get; }
	internal Material Material { get; }
	internal Matrix4x4 Transform { get; set; }

	internal Renderable( Mesh mesh, Material material )
	{
		Mesh = mesh;
		Material = material;
		Transform = Matrix4x4.Identity;
	}
}

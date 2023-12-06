using System.Numerics;

namespace Latte.NewRenderer.Temp;

internal sealed class Renderable
{
	internal string MeshName { get; }
	internal string MaterialName { get; }
	internal Matrix4x4 Transform { get; set; }

	internal Renderable( string meshName, string materialName )
	{
		MeshName = meshName;
		MaterialName = materialName;
		Transform = Matrix4x4.Identity;
	}
}

using Latte.Windowing.Backend;
using System.Collections.Immutable;

namespace Latte.Windowing.Assets;

/// <summary>
/// Represents a 3D model.
/// </summary>
public sealed class Model
{
	/// <summary>
	/// All of the meshes that are a part of the model.
	/// </summary>
	public ImmutableArray<Mesh> Meshes { get; }

	/// <summary>
	/// Initializes a new instance of <see cref="Model"/>.
	/// </summary>
	/// <param name="meshes">The meshes of the model.</param>
	public Model( in ImmutableArray<Mesh> meshes )
	{
		Meshes = meshes;
	}

	/// <summary>
	/// Initializes renderer specific data on a model.
	/// </summary>
	/// <param name="backend">The renderer to initialize for.</param>
	public void Initialize( IRenderingBackend backend )
	{
		foreach ( var mesh in Meshes )
			mesh.Initialize( backend );
	}

	/// <summary>
	/// Returns a new model that is loaded from disk.
	/// </summary>
	/// <param name="modelPath">The path to the model.</param>
	/// <returns>The parsed model from disk.</returns>
	public static Model FromPath( string modelPath )
	{
		return ModelParser.FromPath( modelPath );
	}
}

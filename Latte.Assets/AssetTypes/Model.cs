using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Zio;

namespace Latte.Assets;

/// <summary>
/// Represents a 3D model.
/// </summary>
[method: SetsRequiredMembers]
public sealed class Model( in ImmutableArray<Mesh> meshes )
{
	/// <summary>
	/// All of the meshes that are a part of the model.
	/// </summary>
	public required ImmutableArray<Mesh> Meshes { get; init; } = meshes;

	/// <summary>
	/// Returns a new model that is loaded from disk.
	/// </summary>
	/// <param name="modelPath">The path to the model.</param>
	/// <returns>The parsed model from disk.</returns>
	public static Model FromPath( in UPath modelPath )
	{
		return ModelParser.FromPath( modelPath.ToAbsolute() );
	}
}

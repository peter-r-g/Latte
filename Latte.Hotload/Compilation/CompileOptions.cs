using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Latte.Hotload.Compilation;

/// <summary>
/// A container for all options to give to the compiler.
/// </summary>
internal sealed class CompileOptions
{
	/// <summary>
	/// The level of optimization to be applied to the source code.
	/// </summary>
	internal OptimizationLevel OptimizationLevel { get; set; }

	/// <summary>
	/// Whether or not to generate symbols for the resulting assembly.
	/// NOTE: This will do nothing when <see cref="OptimizationLevel"/> is <see cref="OptimizationLevel.Release"/>.
	/// </summary>
	internal bool GenerateSymbols { get; set; }

	/// <summary>
	/// The pre-processor symbols to apply to the compilation.
	/// </summary>
	internal ImmutableArray<string> PreProcessorSymbols { get; set; } = ImmutableArray<string>.Empty;
}

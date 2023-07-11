using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.IO;

namespace Latte.Hotload.Compilation;

/// <summary>
/// Represents a final compilation result.
/// </summary>
internal readonly struct CompileResult
{
	/// <summary>
	/// Whether or not the compilation completed successfully.
	/// </summary>
	internal bool WasSuccessful { get; }

	/// <summary>
	/// An array of diagnostics that occurred during compilation.
	/// </summary>
	internal ImmutableArray<Diagnostic> Diagnostics { get; } = ImmutableArray<Diagnostic>.Empty;

	/// <summary>
	/// The stream containing the compiled assembly.
	/// </summary>
	internal Stream? CompiledAssembly { get; }
	/// <summary>
	/// The stream containing the symbols in the assembly.
	/// </summary>
	internal Stream? CompiledAssemblySymbols { get; }

	/// <summary>
	/// Whether or not the compilation has assembly symbols.
	/// </summary>
	internal bool HasSymbols => CompiledAssemblySymbols is not null;

	/// <summary>
	/// Initializes a new instance of <see cref="CompileResult"/>.
	/// </summary>
	/// <param name="wasSuccessful">Whether or not the compilation was successful.</param>
	/// <param name="compiledAssembly">The stream that contains the compiled assembly. Null if <see ref="wasSuccessful"/> is false.</param>
	/// <param name="compiledAssemblySymbols">The compiled assembly's debug symbols. Null if no symbols or if <see ref="wasSuccessful"/> is false.</param>
	/// <param name="diagnostics">An array containing all diagnostics that occurred during compilation.</param>
	private CompileResult( bool wasSuccessful, in ImmutableArray<Diagnostic> diagnostics, Stream? compiledAssembly = null, Stream? compiledAssemblySymbols = null )
	{
		WasSuccessful = wasSuccessful;

		Diagnostics = diagnostics;
		CompiledAssembly = compiledAssembly;
		CompiledAssemblySymbols = compiledAssemblySymbols;
	}

	/// <summary>
	/// Shorthand method to create a failed <see cref="CompileResult"/>.
	/// </summary>
	/// <param name="diagnostics">An array containing all diagnostics that occurred during the compilation.</param>
	/// <returns>The newly created <see cref="CompileResult"/>.</returns>
	internal static CompileResult Failed( in ImmutableArray<Diagnostic> diagnostics )
	{
		return new CompileResult(
			wasSuccessful: false,
			diagnostics: diagnostics
		);
	}

	/// <summary>
	/// Shorthand method to create a successful <see cref="CompileResult"/>.
	/// </summary>
	/// <param name="diagnostics">An array containing all diagnostics that occurred during the compilation.</param>
	/// <param name="compiledAssembly">The stream containing the compiled assembly.</param>
	/// <param name="compiledAssemblySymbols">The stream containing the symbols contained in the compiled assembly. Null if no debug symbols.</param>
	/// <returns>The newly created <see cref="CompileResult"/>.</returns>
	internal static CompileResult Successful( in ImmutableArray<Diagnostic> diagnostics, Stream compiledAssembly, Stream? compiledAssemblySymbols )
	{
		return new CompileResult(
			wasSuccessful: true,
			diagnostics: diagnostics,
			compiledAssembly: compiledAssembly,
			compiledAssemblySymbols: compiledAssemblySymbols
		);
	}
}

using Latte.Hotload.NuGet;
using Latte.Hotload.Util;
using Latte.Logging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using NuGet.Packaging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Latte.Hotload.Compilation;

/// <summary>
/// Contains the core functionality for compilation of C# assemblies.
/// </summary>
internal static class Compiler
{
	/// <summary>
	/// The .NET references to include in every build.
	/// </summary>
	private static ImmutableArray<string> SystemReferences { get; } = ImmutableArray.Create(
		"netstandard.dll",
		"mscorlib.dll",
		"System.dll",

		"System.Core.dll",

		"System.ComponentModel.Primitives.dll",
		"System.ComponentModel.Annotations.dll",

		"System.Collections.dll",
		"System.Collections.Concurrent.dll",
		"System.Collections.Immutable.dll",
		"System.Collections.Specialized.dll",

		"System.Console.dll",

		"System.Data.dll",
		"System.Diagnostics.Process.dll",

		"System.IO.Compression.dll",
		"System.IO.FileSystem.Watcher.dll",

		"System.Linq.dll",
		"System.Linq.Expressions.dll",

		"System.Numerics.Vectors.dll",

		"System.ObjectModel.dll",

		"System.Private.CoreLib.dll",
		"System.Private.Xml.dll",
		"System.Private.Uri.dll",

		"System.Runtime.Extensions.dll",
		"System.Runtime.dll",

		"System.Text.RegularExpressions.dll",
		"System.Text.Json.dll",

		"System.Security.Cryptography.dll",

		"System.Threading.Channels.dll",

		"System.Net.Http.dll",
		"System.Web.HttpUtility.dll",

		"System.Xml.ReaderWriter.dll" );

	private static ConcurrentDictionary<string, ConcurrentHashSet<PortableExecutableReference>> AssemblyReferences { get; } = new();
	private static ConcurrentDictionary<string, AdhocWorkspace> AssemblyWorkspaces { get; } = new();
	private static ConcurrentDictionary<string, PortableExecutableReference> ReferenceCache { get; } = new();

	/// <summary>
	/// Compiles a given assembly.
	/// </summary>
	/// <param name="assemblyInfo">The assembly to compile.</param>
	/// <param name="compileOptions">The options to give to the C# compilation.</param>
	/// <returns>A <see cref="Task"/> that represents the asynchronous operation. The <see cref="Task"/>s return value is the result of the compilation.</returns>
	/// <exception cref="UnreachableException">Thrown when the final workspace project becomes invalid unexpectedly.</exception>
	internal static async Task<CompileResult> CompileAsync( AssemblyInfo assemblyInfo, CompileOptions? compileOptions = null )
	{
		if ( assemblyInfo.ProjectPath is null )
			throw new ArgumentException( $"The assembly \"{assemblyInfo.Name}\" cannot be compiled", nameof( assemblyInfo ) );

		using var _ = new ScopedTimingLogger( Loggers.Compiler );
		compileOptions ??= CompileOptions.Default;

		if ( Loggers.Compiler.IsEnabled( LogLevel.Verbose ) )
			Loggers.Compiler.Verbose( "Starting full build for " + assemblyInfo.Name );

		//
		// Fetch the project and all source files.
		//
		var csproj = CSharpProject.FromFile( Path.Combine( assemblyInfo.ProjectPath, assemblyInfo.Name + ".csproj" ) );
		var parseOptions = CSharpParseOptions.Default
			.WithPreprocessorSymbols( csproj.PreProcessorSymbols.Concat( compileOptions.PreProcessorSymbols ) );

		var syntaxTrees = new ConcurrentBag<SyntaxTree>();

		// Build syntax trees.
		{
			var treeTasks = new List<Task>();

			// Global namespaces.
			var globalUsings = string.Empty;
			foreach ( var (@namespace, @static) in csproj.Usings )
				globalUsings += $"global using{(@static ? " static " : " ")}{@namespace};{Environment.NewLine}";

			if ( globalUsings != string.Empty )
				syntaxTrees.Add( CSharpSyntaxTree.ParseText( globalUsings, options: parseOptions, encoding: Encoding.UTF8 ) );

			// For each source file, create a syntax tree we can use to compile it.
			foreach ( var filePath in csproj.CSharpFiles )
			{
				// Add the parsed syntax tree.
				treeTasks.Add( Task.Run( async () =>
				{
					var text = await File.ReadAllTextAsync( filePath );
					syntaxTrees.Add( CSharpSyntaxTree.ParseText( text, options: parseOptions, encoding: Encoding.UTF8, path: filePath ) );
				} ) );
			}

			// Wait for all tasks to finish before continuing.
			await Task.WhenAll( treeTasks );
		}

		//
		// Build up references.
		//
		var references = new ConcurrentHashSet<PortableExecutableReference>();
		{
			// System references.
			var dotnetBaseDir = Path.GetDirectoryName( typeof( object ).Assembly.Location )!;
			foreach ( var systemReference in SystemReferences )
			{
				if ( Loggers.Compiler.IsEnabled( LogLevel.Verbose ) )
					Loggers.Compiler.Verbose( "Adding system reference: " + systemReference );

				references.TryAdd( CreateMetadataReferenceFromPath( Path.Combine( dotnetBaseDir, systemReference ) ) );
			}

			// NuGet references.
			{
				var installTasks = new List<Task<NuGetPackageEntry>>( csproj.PackageReferences.Count );

				foreach ( var (packageId, packageVersion) in csproj.PackageReferences )
				{
					if ( Loggers.Compiler.IsEnabled( LogLevel.Verbose ) )
						Loggers.Compiler.Verbose( "Adding NuGet package reference: " + packageId );

					installTasks.Add( NuGetManager.InstallPackageAsync( packageId, packageVersion, CancellationToken.None ) );
				}

				await Task.WhenAll( installTasks );

				foreach ( var installTask in installTasks )
					references.AddRange( CreateMetadataReferencesFromPaths( installTask.Result.GetAllDllFilePaths() ) );
			}

			// Project references.
			{
				var projectTasks = new List<Task>( csproj.ProjectReferences.Length );

				foreach ( var projectReference in csproj.ProjectReferences )
				{
					var projectAssemblyName = Path.GetFileNameWithoutExtension( projectReference );
					if ( Loggers.Compiler.IsEnabled( LogLevel.Verbose ) )
						Loggers.Compiler.Verbose( "Adding project reference: " + projectAssemblyName );

					if ( HotloadableAssembly.All.ContainsKey( projectAssemblyName ) || projectReference.Contains( "Latte.Hotload" ) )
						continue;

					if ( Loggers.Compiler.IsEnabled( LogLevel.Verbose ) )
						Loggers.Compiler.Verbose( "Starting on-demand hotload of " + projectAssemblyName );

					var projectAssembly = HotloadableAssembly.New( new AssemblyInfo
					{
						Name = projectAssemblyName,
						ProjectPath = Path.GetDirectoryName( projectReference )
					} );
					projectTasks.Add( projectAssembly.InitAsync() );
				}

				await Task.WhenAll( projectTasks );

				foreach ( var projectReference in csproj.ProjectReferences )
				{
					var projectAssemblyName = Path.GetFileNameWithoutExtension( projectReference );
					if ( projectReference.Contains( "Latte.Hotload" ) )
						references.TryAdd( CreateMetadataReferenceFromPath( "Latte.Hotload.dll" ) );
					else
						references.TryAdd( CreateMetadataReferenceFromPath( projectReference ) );

					if ( AssemblyReferences.TryGetValue( projectAssemblyName, out var projectsReferences ) )
						references.AddRange( projectsReferences );
				}
			}

			// Literal references.
			foreach ( var reference in csproj.DllReferences )
			{
				if ( Loggers.Compiler.IsEnabled( LogLevel.Verbose ) )
					Loggers.Compiler.Verbose( "Adding DLL reference: " + reference );

				references.TryAdd( CreateMetadataReferenceFromPath( Path.GetFullPath( reference ) ) );
			}
		}

		//
		// Setup compilation.
		//

		// Setup compile options.
		var options = new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary )
			.WithPlatform( Platform.AnyCpu )
			.WithOptimizationLevel( compileOptions.OptimizationLevel )
			.WithConcurrentBuild( true )
			.WithAllowUnsafe( csproj.AllowUnsafeBlocks )
			.WithNullableContextOptions( csproj.Nullable ? NullableContextOptions.Enable : NullableContextOptions.Disable );

		// Setup incremental workspace.
		var workspace = new AdhocWorkspace();

		// Setup project.
		var projectInfo = ProjectInfo.Create(
			ProjectId.CreateNewId( assemblyInfo.Name ),
			VersionStamp.Create(),
			assemblyInfo.Name,
			assemblyInfo.Name,
			LanguageNames.CSharp,
			compilationOptions: options,
			parseOptions: parseOptions,
			metadataReferences: references );
		var project = workspace.AddProject( projectInfo );

		// Add documents to workspace.
		foreach ( var syntaxTree in syntaxTrees )
		{
			var documentInfo = DocumentInfo.Create(
				DocumentId.CreateNewId( project.Id ),
				Path.GetFileName( syntaxTree.FilePath ),
				filePath: syntaxTree.FilePath,
				sourceCodeKind: SourceCodeKind.Regular,
				loader: TextLoader.From( TextAndVersion.Create( syntaxTree.GetText(), VersionStamp.Create() ) ) );

			workspace.AddDocument( documentInfo );
		}

		project = workspace.CurrentSolution.GetProject( project.Id );

		// Panic if project became invalid.
		if ( project is null )
			throw new UnreachableException( "The project became invalid unexpectedly" );

		//
		// Compile.
		//

		var compilation = await project.GetCompilationAsync() ?? throw new UnreachableException( "The project compilation became invalid unexpectedly" );
		var result = FinishCompile( assemblyInfo, compileOptions, compilation );

		if ( result.WasSuccessful )
		{
			AssemblyReferences.AddOrUpdate( assemblyInfo.Name, references, ( key, value ) => value );
			AssemblyWorkspaces.AddOrUpdate( assemblyInfo.Name, workspace, ( key, val ) => val );

			if ( Loggers.Compiler.IsEnabled( LogLevel.Information ) )
				Loggers.Compiler.Information( $"Full build for {assemblyInfo.Name} succeeded" );
		}
		else if ( !result.WasSuccessful && Loggers.Compiler.IsEnabled( LogLevel.Error ) )
			Loggers.Compiler.Error( $"Full build for {assemblyInfo.Name} failed" );

		return result;
	}

	/// <summary>
	/// Compiles a <see cref="Workspace"/> assembly with incremental changes.
	/// </summary>
	/// <param name="assemblyInfo">The information regarding the assembly that is being incrementally compiled.</param>
	/// <param name="changedFilePaths">A dictionary of absolute file paths mapped to the type of change it has experienced.</param>
	/// <param name="compileOptions">The <see cref="CompileOptions"/> to give to the C# compilation.</param>
	/// <returns>A <see cref="Task"/> that represents the asynchronous operation. The <see cref="Task"/>s return value is the result of the compilation.</returns>
	/// <exception cref="UnreachableException">Thrown when applying changes to the <see cref="Workspace"/> failed.</exception>
	internal static async Task<CompileResult> IncrementalCompileAsync( AssemblyInfo assemblyInfo, IReadOnlyDictionary<string, WatcherChangeTypes> changedFilePaths, CompileOptions? compileOptions = null )
	{
		using var _ = new ScopedTimingLogger( Loggers.Compiler );
		compileOptions ??= CompileOptions.Default;

		if ( Loggers.Compiler.IsEnabled( LogLevel.Verbose ) )
			Loggers.Compiler.Verbose( "Starting incremental build for " + assemblyInfo.Name );

		var workspace = AssemblyWorkspaces[assemblyInfo.Name];
		var parseOptions = (CSharpParseOptions)workspace.CurrentSolution.Projects.First().ParseOptions!;

		// Update each changed file.
		foreach ( var (filePath, changeType) in changedFilePaths )
		{
			switch ( changeType )
			{
				case WatcherChangeTypes.Created:
					{
						var syntaxTree = CSharpSyntaxTree.ParseText(
							await File.ReadAllTextAsync( filePath ),
							options: parseOptions,
							encoding: Encoding.UTF8,
							path: filePath );

						var documentInfo = DocumentInfo.Create(
							DocumentId.CreateNewId( workspace.CurrentSolution.ProjectIds[0] ),
							Path.GetFileName( syntaxTree.FilePath ),
							filePath: syntaxTree.FilePath,
							sourceCodeKind: SourceCodeKind.Regular,
							loader: TextLoader.From( TextAndVersion.Create( syntaxTree.GetText(), VersionStamp.Create() ) ) );

						workspace.AddDocument( documentInfo );
						if ( !workspace.TryApplyChanges( workspace.CurrentSolution ) )
							throw new UnreachableException();
						break;
					}
				case WatcherChangeTypes.Deleted:
					{
						// Find the existing document for the deleted file.
						var document = workspace.CurrentSolution.GetDocumentIdsWithFilePath( filePath )
							.Select( workspace.CurrentSolution.GetDocument )
							.FirstOrDefault();

						if ( document is null )
							continue;

						// Apply the removed document.
						if ( !workspace.TryApplyChanges( workspace.CurrentSolution.RemoveDocument( document.Id ) ) )
							throw new UnreachableException();
						break;
					}
				case WatcherChangeTypes.Changed:
				case WatcherChangeTypes.Renamed:
					{
						// Find the existing document for the changed file.
						var document = workspace.CurrentSolution.GetDocumentIdsWithFilePath( filePath )
							.Select( workspace.CurrentSolution.GetDocument )
							.FirstOrDefault();

						if ( document is null )
							continue;

						var syntaxTree = CSharpSyntaxTree.ParseText(
							await File.ReadAllTextAsync( filePath ),
							options: parseOptions,
							encoding: Encoding.UTF8,
							path: filePath );

						// Apply the changed tree.
						if ( !workspace.TryApplyChanges( workspace.CurrentSolution.WithDocumentSyntaxRoot( document.Id, syntaxTree.GetRoot() ) ) )
							throw new UnreachableException();

						break;
					}
			}
		}

		var compilation = await workspace.CurrentSolution.Projects.First().GetCompilationAsync() ?? throw new UnreachableException();
		var result = FinishCompile( assemblyInfo, compileOptions, compilation );

		if ( result.WasSuccessful && Loggers.Compiler.IsEnabled( LogLevel.Information ) )
			Loggers.Compiler.Information( $"Incremental build for {assemblyInfo.Name} succeeded" );
		else if ( !result.WasSuccessful && Loggers.Compiler.IsEnabled( LogLevel.Error ) )
			Loggers.Compiler.Error( $"Incremental build for {assemblyInfo.Name} failed" );
			
		return result;
	}

	private static CompileResult FinishCompile( in AssemblyInfo assemblyInfo, CompileOptions compileOptions, Microsoft.CodeAnalysis.Compilation compilation )
	{
		var assemblyStream = new MemoryStream();
		var symbolsStream = compileOptions.GenerateSymbols ? new MemoryStream() : null;

		// Setup emit options.
		EmitOptions? emitOptions = null;
		if ( compileOptions.GenerateSymbols )
		{
			emitOptions = new EmitOptions(
				debugInformationFormat: DebugInformationFormat.PortablePdb,
				pdbFilePath: $"{assemblyInfo.Name}.pdb" );
		}

		// Compile.
		var result = compilation.Emit(
			assemblyStream,
			symbolsStream,
			options: emitOptions
		);

		assemblyStream.Position = 0;
		if ( symbolsStream is not null )
			symbolsStream.Position = 0;

		// Output all diagnostics that came from the compile.
		foreach ( var diagnosticGroup in result.Diagnostics
			.OrderBy( diagnostic => diagnostic.WarningLevel )
			.GroupBy( diagnostic => diagnostic.Severity ) )
		{
			foreach ( var diagnostic in diagnosticGroup )
			{
				switch ( diagnostic.Severity )
				{
					case DiagnosticSeverity.Hidden:
						Loggers.Compiler.Information( $"{diagnostic.Id}: {diagnostic.GetMessage()} ({diagnostic.Location})" );
						break;
					case DiagnosticSeverity.Info:
						Loggers.Compiler.Information( $"{diagnostic.Id}: {diagnostic.GetMessage()} ({diagnostic.Location})" );
						break;
					case DiagnosticSeverity.Warning:
						Loggers.Compiler.Warning( $"{diagnostic.Id}: {diagnostic.GetMessage()} ({diagnostic.Location})" );
						break;
					case DiagnosticSeverity.Error:
						Loggers.Compiler.Error( $"{diagnostic.Id}: {diagnostic.GetMessage()} ({diagnostic.Location})" );
						break;
				}
			}
		}

		if ( result.Success )
		{
			CreateMetadataReferenceFromStream( assemblyInfo.Name, new MemoryStream( assemblyStream.ToArray() ), true );
			return CompileResult.Successful( result.Diagnostics, assemblyStream, symbolsStream );
		}
		else
			return CompileResult.Failed( result.Diagnostics );
	}

	/// <summary>
	/// Returns a set of <see cref="PortableExecutableReference"/> from a set of relative paths.
	/// </summary>
	/// <param name="assemblyPaths">A set of relative paths to create references from.</param>
	/// <returns>A set of <see cref="PortableExecutableReference"/> from a set of relative paths.</returns>
	private static IEnumerable<PortableExecutableReference> CreateMetadataReferencesFromPaths( IEnumerable<string> assemblyPaths, bool ignoreCache = false )
	{
		foreach ( var assemblyPath in assemblyPaths )
			yield return CreateMetadataReferenceFromPath( assemblyPath, ignoreCache );
	}

	/// <summary>
	/// Creates a <see cref="PortableExecutableReference"/> from a relative path.
	/// </summary>
	/// <param name="assemblyPath">The relative path to create a reference from.</param>
	/// <returns>A <see cref="PortableExecutableReference"/> from a relative path.</returns>
	private static PortableExecutableReference CreateMetadataReferenceFromPath( string assemblyPath, bool ignoreCache = false )
	{
		var cacheName = Path.GetFileNameWithoutExtension( assemblyPath );
		if ( !ignoreCache && ReferenceCache.TryGetValue( cacheName, out var reference ) )
			return reference;

		var newReference = MetadataReference.CreateFromFile( assemblyPath );
		ReferenceCache.AddOrUpdate( cacheName, newReference, (key, val) => val );
		return newReference;
	}

	private static MetadataReference CreateMetadataReferenceFromStream( string assemblyName, Stream assemblyStream, bool ignoreCache = false )
	{
		if ( !ignoreCache && ReferenceCache.TryGetValue( assemblyName, out var reference ) )
			return reference;

		var newReference = MetadataReference.CreateFromStream( assemblyStream );
		ReferenceCache.AddOrUpdate( assemblyName, newReference, ( key, val ) => val );
		return newReference;
	}
}

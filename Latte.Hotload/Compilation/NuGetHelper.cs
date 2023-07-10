using Microsoft.CodeAnalysis;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;
using Latte.Hotload.Util;
using System.Diagnostics;

namespace Latte.Hotload.Compilation;

/// <summary>
/// A collection of helper methods for the NuGet.Protocol package.
/// </summary>
internal static class NuGetHelper
{
	private static readonly SourceCacheContext cache = new();
	private static readonly ILogger logger = NullLogger.Instance;

	/// <summary>
	/// Fetches a NuGet package DLL and adds it to the build references.
	/// </summary>
	/// <param name="id">The ID of the NuGet package.</param>
	/// <param name="version">The version of the NuGet package.</param>
	/// <param name="references">The references to append the NuGet package to.</param>
	/// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
	internal static async Task FetchPackageAsync( string id, NuGetVersion version, IProducerConsumerCollection<PortableExecutableReference> references, ConcurrentHashSet<string>? fetchedIds = null )
	{
		fetchedIds ??= new();
		fetchedIds.TryAdd( id );

		// Setup.
		var repository = Repository.Factory.GetCoreV3( "https://api.nuget.org/v3/index.json" );
		var resource = await repository.GetResourceAsync<FindPackageByIdResource>();

		using var packageStream = new MemoryStream();

		// Get NuGet package.
		await resource.CopyNupkgToStreamAsync(
			id,
			version,
			packageStream,
			cache,
			logger,
			CancellationToken.None );

		using var packageReader = new PackageArchiveReader( packageStream );
		var nuspecReader = await packageReader.GetNuspecReaderAsync( CancellationToken.None );

		// Find the framework target we want.
		var currentFramework = NuGetFramework.ParseFrameworkName( CompilerHelper.GetTargetFrameworkName(), DefaultFrameworkNameProvider.Instance );

		// Add dependencies.
		var dependenciesGroup = nuspecReader.GetDependencyGroups().GetNearest( currentFramework );
		if ( dependenciesGroup is not null && dependenciesGroup.Packages.Any() )
		{
			var dependencyTasks = new List<Task>();

			foreach ( var dependency in dependenciesGroup.Packages )
			{
				if ( fetchedIds.Contains( dependency.Id ) )
					continue;

				dependencyTasks.Add( FetchPackageWithVersionRangeAsync( dependency.Id, dependency.VersionRange, references, fetchedIds ) );
			}

			await Task.WhenAll( dependencyTasks );
		}

		// Get DLL from package.
		var packageItemsGroup = packageReader.GetLibItems().GetNearest( currentFramework );
		if ( packageItemsGroup is null || !packageItemsGroup.Items.Any() )
			return;

		var dllFilePath = packageItemsGroup.Items.FirstOrDefault( item => item.EndsWith( "dll" ) );
		if ( dllFilePath is null )
			return;

		var dllFileName = Path.GetFileName( dllFilePath );
		string referencePath;
		if ( File.Exists( dllFilePath ) )
			referencePath = dllFilePath;
		else if ( File.Exists( Path.Combine( "nuget", dllFilePath ) ) )
			referencePath = Path.Combine( "nuget", dllFileName );
		else
		{
			// Extract the correct DLL and add it to references.
			referencePath = Path.Combine( "nuget", dllFileName );
			packageReader.ExtractFile( dllFilePath, Path.Combine( Directory.GetCurrentDirectory(), referencePath ), logger );
		}

		var reference = Compiler.CreateMetadataReferenceFromPath( referencePath );
		if ( !references.Contains( reference ) )
			references.TryAdd( reference );
	}

	/// <summary>
	/// Fetches all versions of a NuGet package fetches the version that best fits.
	/// </summary>
	/// <param name="id">The ID of the NuGet package.</param>
	/// <param name="versionRange">The range of versions to look at.</param>
	/// <param name="references">The references to append the NuGet package to.</param>
	/// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
	internal static async Task FetchPackageWithVersionRangeAsync( string id, VersionRange versionRange, IProducerConsumerCollection<PortableExecutableReference> references, ConcurrentHashSet<string> fetchedIds )
	{
		// Setup.
		var repository = Repository.Factory.GetCoreV3( "https://api.nuget.org/v3/index.json" );
		var resource = await repository.GetResourceAsync<FindPackageByIdResource>();

		// Get all versions of the package.
		var versions = await resource.GetAllVersionsAsync(
			id,
			cache,
			NullLogger.Instance,
			CancellationToken.None );

		// Find the best version and get it.
		var bestVersion = versionRange.FindBestMatch( versions );
		await FetchPackageAsync( id, bestVersion, references, fetchedIds );
	}
}

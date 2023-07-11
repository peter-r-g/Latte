using Latte.Hotload.Compilation;
using Latte.Hotload.Util;
using Microsoft.CodeAnalysis;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Latte.Hotload.NuGet;

internal static class NuGetManager
{
	internal static NuGetFramework CurrentFramework { get; } = NuGetFramework.ParseFrameworkName(
		CompilerHelper.GetTargetFrameworkName(),
		DefaultFrameworkNameProvider.Instance );

	private static ConcurrentHashSet<string> InstallingPackages { get; } = new();

	private static SourceCacheContext Cache { get; } = new();
	private static ILogger Logger { get; } = NullLogger.Instance;
	private const string CacheDirectory = "nuget";

	internal static async ValueTask<Stream> DownloadPackageAsync( string id, NuGetVersion version, CancellationToken cancellationToken )
	{
		cancellationToken.ThrowIfCancellationRequested();

		// Setup.
		var repository = Repository.Factory.GetCoreV3( "https://api.nuget.org/v3/index.json" );
		var resource = await repository.GetResourceAsync<FindPackageByIdResource>();
		cancellationToken.ThrowIfCancellationRequested();

		var packageStream = new MemoryStream();

		// Get NuGet package.
		await resource.CopyNupkgToStreamAsync(
			id,
			version,
			packageStream,
			Cache,
			Logger,
			cancellationToken );
		cancellationToken.ThrowIfCancellationRequested();

		return packageStream;
	}

	internal static async ValueTask<NuGetVersion> GetBestVersionAsync( string id, VersionRange versionRange, CancellationToken cancellationToken )
	{
		cancellationToken.ThrowIfCancellationRequested();

		// Setup.
		var repository = Repository.Factory.GetCoreV3( "https://api.nuget.org/v3/index.json" );
		var resource = await repository.GetResourceAsync<FindPackageByIdResource>();
		cancellationToken.ThrowIfCancellationRequested();

		// Get all versions of the package.
		var versions = await resource.GetAllVersionsAsync(
			id,
			Cache,
			Logger,
			CancellationToken.None );
		cancellationToken.ThrowIfCancellationRequested();

		// Find the best version and return it.
		return versionRange.FindBestMatch( versions );
	}

	internal static async Task<NuGetPackageEntry> InstallPackageAsync( string id, NuGetVersion version, CancellationToken cancellationToken, bool recursive = true )
	{
		while ( InstallingPackages.Contains( id ) )
			await Task.Delay( 1 );

		if ( NuGetPackageEntry.All.TryGetValue( id, out var cachedEntry ) )
			return cachedEntry;

		using var _ = new InstallPeg( id );

		cancellationToken.ThrowIfCancellationRequested();

		using var packageStream = await DownloadPackageAsync( id, version, cancellationToken );
		cancellationToken.ThrowIfCancellationRequested();

		var builder = new NuGetPackageEntryBuilder()
			.WithId( id )
			.WithVersion( version );

		foreach ( var dllFilePath in GetPackageDllPaths( packageStream ) )
		{
			var dllFileName = Path.GetFileName( dllFilePath );
			var destinationPath = Path.Combine( CacheDirectory, dllFileName );
			ExtractFile( packageStream, dllFilePath, destinationPath );
			builder.AddDllFilePath( destinationPath );
		}

		cancellationToken.ThrowIfCancellationRequested();

		if ( !recursive )
			return builder.Build();

		var dependencies = GetPackageDependencies( packageStream );
		var dependencyTasks = new List<Task<NuGetPackageEntry>>();
		foreach ( var dependency in dependencies.Packages )
		{
			var dependencyTask = InstallPackageAsync(
				dependency.Id,
				await GetBestVersionAsync( dependency.Id, dependency.VersionRange, cancellationToken ),
				cancellationToken,
				true );

			dependencyTasks.Add( dependencyTask );
		}

		await Task.WhenAll( dependencyTasks );

		foreach ( var dependencyTask in dependencyTasks )
		{
			builder.AddDependency( dependencyTask.Result );
			cancellationToken.ThrowIfCancellationRequested();
		}

		return builder.Build();
	}

	private static void ExtractFile( Stream packageStream, string packageFilePath, string destinationFilePath )
	{
		if ( !Path.IsPathFullyQualified( destinationFilePath ) )
			destinationFilePath = Path.GetFullPath( destinationFilePath );

		using var reader = new PackageArchiveReader( packageStream, true );
		reader.ExtractFile( packageFilePath, destinationFilePath, Logger );
	}

	private static PackageDependencyGroup GetPackageDependencies( Stream packageStream )
	{
		using var reader = new PackageArchiveReader( packageStream, true );
		return reader.GetPackageDependencies().GetNearest( CurrentFramework );
	}

	private static IEnumerable<string> GetPackageDllPaths( Stream packageStream )
	{
		using var reader = new PackageArchiveReader( packageStream, true );
		var itemGroup = reader.GetLibItems().GetNearest( CurrentFramework );

		if ( itemGroup is null || !itemGroup.Items.Any() )
			yield break;

		foreach ( var itemPath in itemGroup.Items.Where( itemPath => itemPath.EndsWith( ".dll" ) ) )
			yield return itemPath;
	}

	private readonly struct InstallPeg : IDisposable
	{
		internal required string PackageId { get; init; }

		[SetsRequiredMembers]
		internal InstallPeg( string packageId )
		{
			PackageId = packageId;
			InstallingPackages.TryAdd( PackageId );
		}

		public void Dispose()
		{
			InstallingPackages.TryRemove( PackageId );
		}
	}

	/*internal static async Task<NuGetPackageEntry> DownloadPackage( string id, NuGetVersion version )
	{
		if ( DownloadedPackages.TryGetValue( id, out var cachedPackage ) )
			return cachedPackage;

		// Setup.
		var repository = Repository.Factory.GetCoreV3( "https://api.nuget.org/v3/index.json" );
		var resource = await repository.GetResourceAsync<FindPackageByIdResource>();

		using var packageStream = new MemoryStream();

		// Get NuGet package.
		await resource.CopyNupkgToStreamAsync(
			id,
			version,
			packageStream,
			Cache,
			Logger,
			CancellationToken.None );

		using var packageReader = new PackageArchiveReader( packageStream );
		var nuspecReader = await packageReader.GetNuspecReaderAsync( CancellationToken.None );
		packageReader.GetPackageDependencies().GetNearest( CurrentFramework );

		var dependencyFilePaths = new HashSet<string>();

		// Add dependencies.
		var dependenciesGroup = nuspecReader.GetDependencyGroups().GetNearest( CurrentFramework );
		if ( dependenciesGroup is not null && dependenciesGroup.Packages.Any() )
		{
			foreach ( var dependency in dependenciesGroup.Packages )
			{
				var entry = await FetchPackageWithVersionRangeAsync( dependency.Id, dependency.VersionRange );
				dependencyFilePaths.AddRange( entry.DependencyFilePaths );
			}
		}

		// Get DLL from package.
		var packageItemsGroup = packageReader.GetLibItems().GetNearest( CurrentFramework );
		if ( packageItemsGroup is null || !packageItemsGroup.Items.Any() )
		{
			var packageEntry = new NuGetPackageEntry
			{
				Id = id,
				Version = version,
				DependencyFilePaths = dependencyFilePaths.ToImmutableArray()
			};

			await AddToCacheAsync( packageEntry );
			return packageEntry;
		}

		var dllFilePath = packageItemsGroup.Items.FirstOrDefault( item => item.EndsWith( "dll" ) );
		if ( dllFilePath is null )
		{
			var packageEntry = new NuGetPackageEntry
			{
				Id = id,
				Version = version,
				DependencyFilePaths = dependencyFilePaths.ToImmutableArray()
			};

			await AddToCacheAsync( packageEntry );
			return packageEntry;
		}

		var dllFileName = Path.GetFileName( dllFilePath );
		string referencePath;
		if ( File.Exists( dllFilePath ) )
			referencePath = dllFilePath;
		else if ( File.Exists( Path.Combine( CacheDirectory, dllFilePath ) ) )
			referencePath = Path.Combine( CacheDirectory, dllFileName );
		else
		{
			// Extract the correct DLL and add it to references.
			referencePath = Path.Combine( CacheDirectory, dllFileName );

			if ( ExtractionsInProgress.Contains( id ) )
			{
				do
				{
					await Task.Delay( 1 );
				} while ( ExtractionsInProgress.Contains( id ) );
			}
			else
			{
				ExtractionsInProgress.TryAdd( id );
				packageReader.ExtractFile( dllFilePath, Path.Combine( Directory.GetCurrentDirectory(), referencePath ), Logger );
				ExtractionsInProgress.TryRemove( id );
			}
		}

		{
			var packageEntry = new NuGetPackageEntry
			{
				Id = id,
				Version = version,
				DependencyFilePaths = dependencyFilePaths.ToImmutableArray()
			};

			await AddToCacheAsync( packageEntry );
			return packageEntry;
		}
	}

	internal static async Task<NuGetPackageEntry> FetchPackageWithVersionRangeAsync( string id, VersionRange versionRange )
	{
		if ( DownloadedPackages.TryGetValue( id, out var cachedPackage ) )
			return cachedPackage;

		// Setup.
		var repository = Repository.Factory.GetCoreV3( "https://api.nuget.org/v3/index.json" );
		var resource = await repository.GetResourceAsync<FindPackageByIdResource>();

		// Get all versions of the package.
		var versions = await resource.GetAllVersionsAsync(
			id,
			Cache,
			Logger,
			CancellationToken.None );

		// Find the best version and get it.
		var bestVersion = versionRange.FindBestMatch( versions );
		return await DownloadPackage( id, bestVersion );
	}

	private static async Task AddToCacheAsync( NuGetPackageEntry packageEntry )
	{
		DownloadedPackages.TryAdd( packageEntry.Id, packageEntry );

		using var fs = File.Open( CacheFile, FileMode.Create );
		await JsonSerializer.SerializeAsync( fs, DownloadedPackages );
	}*/
}

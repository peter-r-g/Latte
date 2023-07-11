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

	internal static bool IsDllInstalled( string dllName )
	{
		if ( !dllName.EndsWith( ".dll" ) )
			dllName += ".dll";

		return File.Exists( Path.Combine( CacheDirectory, dllName ) );
	}

	internal static string? GetDllPath( string dllName )
	{
		if ( !dllName.EndsWith( ".dll" ) )
			dllName += ".dll";

		if ( !IsDllInstalled( dllName ) )
			return null;

		return Path.Combine( CacheDirectory, dllName );
	}

	internal static async ValueTask<Stream> DownloadPackageAsync( string id, NuGetVersion version, CancellationToken cancellationToken )
	{
		if ( Loggers.NuGet.IsEnabled( Logging.LogLevel.Verbose ) )
			Loggers.NuGet.Verbose( $"Downloading package {id} @ {version} to in memory stream..." );

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

		if ( Loggers.NuGet.IsEnabled( Logging.LogLevel.Verbose ) )
			Loggers.NuGet.Verbose( $"Downloaded {id} @ {version}" );
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

		foreach ( var runtimeFilePath in GetRuntimeItemPaths( packageStream ) )
		{
			ExtractFile( packageStream, runtimeFilePath, runtimeFilePath );
			builder.AddRuntimeFilePath( runtimeFilePath );
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

	private static IEnumerable<string> GetRuntimeItemPaths( Stream packageStream )
	{
		using var reader = new PackageArchiveReader( packageStream, true );
		var runtimeItems = reader.GetItems( "runtimes" ).SelectMany( group => group.Items );

		foreach ( var runtimeItem in runtimeItems )
			yield return runtimeItem;
	}

	private readonly struct InstallPeg : IDisposable
	{
		internal required string PackageId { get; init; }

		[SetsRequiredMembers]
		internal InstallPeg( string packageId )
		{
			PackageId = packageId;
			InstallingPackages.TryAdd( PackageId );

			if ( Loggers.NuGet.IsEnabled( Logging.LogLevel.Verbose ) )
				Loggers.NuGet.Verbose( $"Installing {PackageId}..." );
		}

		public void Dispose()
		{
			InstallingPackages.TryRemove( PackageId );

			if ( Loggers.NuGet.IsEnabled( Logging.LogLevel.Verbose ) )
				Loggers.NuGet.Verbose( $"Finished installing {PackageId}" );
		}
	}
}

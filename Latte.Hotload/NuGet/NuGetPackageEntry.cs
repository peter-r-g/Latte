using NuGet.Versioning;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Latte.Hotload.NuGet;

internal sealed class NuGetPackageEntry
{
	internal static IReadOnlyDictionary<string, NuGetPackageEntry> All => AllEntries;
	private static ConcurrentDictionary<string, NuGetPackageEntry> AllEntries { get; set; } = new();

	internal string Id { get; init; } = string.Empty;
	internal NuGetVersion Version { get; init; } = new NuGetVersion( 1, 0, 0 );
	internal ImmutableArray<string> DllFilePaths { get; init; } = new ImmutableArray<string>();
	internal ImmutableArray<NuGetPackageEntry> Dependencies { get; init; }

	private ImmutableArray<string> CachedAllDllFilePaths { get; set; } = ImmutableArray<string>.Empty;
	private bool CachedDllPaths { get; set; }

	internal NuGetPackageEntry()
	{
		AllEntries.TryAdd( Id, this );
	}

	internal ImmutableArray<string> GetAllDllFilePaths()
	{
		if ( CachedDllPaths )
			return CachedAllDllFilePaths;

		var dllFilePaths = ImmutableArray.CreateBuilder<string>();
		dllFilePaths.AddRange( DllFilePaths );

		foreach ( var dependency in Dependencies )
			dllFilePaths.AddRange( dependency.GetAllDllFilePaths() );

		dllFilePaths.Capacity = dllFilePaths.Count;
		CachedAllDllFilePaths = dllFilePaths.MoveToImmutable();
		CachedDllPaths = true;

		return CachedAllDllFilePaths;
	}
}

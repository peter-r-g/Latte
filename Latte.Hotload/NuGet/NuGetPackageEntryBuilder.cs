using NuGet.Versioning;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Latte.Hotload.NuGet;

internal sealed class NuGetPackageEntryBuilder
{
	private string Id { get; set; } = string.Empty;
	private NuGetVersion Version { get; set; } = new NuGetVersion( 1, 0, 0 );
	private List<string> DllFilePaths { get; set; } = new();
	private List<NuGetPackageEntry> Dependencies { get; set; } = new();

	internal NuGetPackageEntryBuilder WithId( string packageId )
	{
		Id = packageId;
		return this;
	}

	internal NuGetPackageEntryBuilder WithVersion( NuGetVersion version )
	{
		Version = version;
		return this;
	}

	internal NuGetPackageEntryBuilder AddDllFilePath( string filePath )
	{
		DllFilePaths.Add( filePath );
		return this;
	}

	internal NuGetPackageEntryBuilder AddDependency( NuGetPackageEntry packageEntry )
	{
		Dependencies.Add( packageEntry );
		return this;
	}

	internal NuGetPackageEntry Build()
	{
		return new NuGetPackageEntry
		{
			Id = Id,
			Version = Version,
			DllFilePaths = DllFilePaths.ToImmutableArray(),
			Dependencies = Dependencies.ToImmutableArray()
		};
	}
}

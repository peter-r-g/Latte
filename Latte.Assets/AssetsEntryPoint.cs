using Latte.Hotload;
using System.Collections.Concurrent;
using Zio;
using Zio.FileSystems;

namespace Latte.Assets;

public sealed class AssetsEntryPoint : IEntryPoint
{
	private static ConcurrentDictionary<string, IFileSystem> AssemblyFileSystems { get; } = new();

	public void Main()
	{
		foreach ( var (_, assembly) in HotloadableAssembly.All )
			AddAssemblyAssets( assembly.AssemblyInfo );

		HotloadableAssembly.OnAdded += OnAssemblyAdded;
		HotloadableAssembly.OnRemoved += OnAssemblyRemoved;
	}

	public void PreHotload()
	{
		HotloadableAssembly.OnAdded -= OnAssemblyAdded;
		HotloadableAssembly.OnRemoved -= OnAssemblyRemoved;
	}

	public void PostHotload()
	{
		HotloadableAssembly.OnAdded += OnAssemblyAdded;
		HotloadableAssembly.OnRemoved += OnAssemblyRemoved;
	}

	private void OnAssemblyAdded( HotloadableAssembly hotloadableAssembly )
	{
		AddAssemblyAssets( hotloadableAssembly.AssemblyInfo );
	}

	private void OnAssemblyRemoved( HotloadableAssembly hotloadableAssembly )
	{
		if ( !AssemblyFileSystems.TryRemove( hotloadableAssembly.AssemblyInfo.Name, out var fs ) )
			return;

		FileSystems.InternalAssets.RemoveFileSystem( fs );
		fs.Dispose();
	}

	private static void AddAssemblyAssets( in AssemblyInfo assemblyInfo )
	{
		if ( assemblyInfo.ProjectPath is null )
			return;

		var projectPath = FileSystems.System.ConvertPathFromInternal( assemblyInfo.ProjectPath );
		if ( !FileSystems.System.DirectoryExists( projectPath / "Assets" ) )
			return;

		var fs = new SubFileSystem( FileSystems.System, projectPath / "Assets" );
		AssemblyFileSystems.TryAdd( assemblyInfo.Name, fs );
		FileSystems.InternalAssets.AddFileSystem( fs );
	}
}

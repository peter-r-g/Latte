using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Latte.Hotload;

internal sealed class HotloadableAssembly : IDisposable
{
	internal AssemblyInfo AssemblyInfo { get; }
	internal Assembly Assembly { get; private set; }
	internal IEntryPoint EntryPoint { get; private set; }

	private FileSystemWatcher? CsProjectWatcher { get; }
	private FileSystemWatcher? CodeWatcher { get; }
	private AssemblyLoadContext? Context { get; }

	private bool buildRequested;

	internal HotloadableAssembly( in AssemblyInfo assemblyInfo )
	{
		if ( !Path.IsPathFullyQualified( assemblyInfo.Path ) )
		{
			AssemblyInfo = assemblyInfo with
			{
				Path = Path.GetFullPath( Path.Combine( Program.CurrentDirectory, assemblyInfo.Path ) )
			};
		}
		else
			AssemblyInfo = assemblyInfo;

		Context = new AssemblyLoadContext( AssemblyInfo.Name, true );
		Assembly = Context.LoadFromAssemblyPath( AssemblyInfo.Path );
		EntryPoint = GetEntryPoint();

		if ( assemblyInfo.ProjectPath is null )
			return;

		CsProjectWatcher = new FileSystemWatcher( Path.GetFullPath( Path.Combine( Program.CurrentDirectory, assemblyInfo.ProjectPath ) ), "*.csproj" )
		{
			NotifyFilter = NotifyFilters.Attributes
							 | NotifyFilters.CreationTime
							 | NotifyFilters.DirectoryName
							 | NotifyFilters.FileName
							 | NotifyFilters.LastAccess
							 | NotifyFilters.LastWrite
							 | NotifyFilters.Security
							 | NotifyFilters.Size
		};
		CsProjectWatcher.Renamed += OnProjectChanged;
		CsProjectWatcher.EnableRaisingEvents = true;

		CodeWatcher = new FileSystemWatcher( Path.GetFullPath( Path.Combine( Program.CurrentDirectory, assemblyInfo.ProjectPath ) ), "*.cs" )
		{
			NotifyFilter = NotifyFilters.Attributes
							 | NotifyFilters.CreationTime
							 | NotifyFilters.DirectoryName
							 | NotifyFilters.FileName
							 | NotifyFilters.LastAccess
							 | NotifyFilters.LastWrite
							 | NotifyFilters.Security
							 | NotifyFilters.Size
		};
		CodeWatcher.Renamed += OnFileChanged;
		CodeWatcher.IncludeSubdirectories = true;
		CodeWatcher.EnableRaisingEvents = true;
	}

	internal HotloadableAssembly( Assembly assembly )
	{
		AssemblyInfo = new AssemblyInfo
		{
			Name = assembly.GetName().Name ?? "Unknown",
			Path = assembly.Location
		};
		Assembly = assembly;
		EntryPoint = GetEntryPoint();
	}

	private IEntryPoint GetEntryPoint()
	{
		var entryPointType = Assembly.ExportedTypes.FirstOrDefault( type =>
		{
			foreach ( var @interface in type.GetInterfaces() )
			{
				if ( @interface == typeof( IEntryPoint ) )
					return true;
			}

			return false;
		} ) ?? throw new EntryPointNotFoundException( $"No entry point type found for {AssemblyInfo.Name}" );

		return (IEntryPoint)Activator.CreateInstance( entryPointType )!;
	}

	private void OnProjectChanged( object? sender, FileSystemEventArgs args )
	{
		buildRequested = true;
	}

	private void OnFileChanged( object? sender, FileSystemEventArgs args )
	{
		if ( !args.FullPath.EndsWith( ".cs" ) )
			return;

		buildRequested = true;
	}

	public void Dispose()
	{
		CodeWatcher?.Dispose();
		Context?.Unload();
	}
}

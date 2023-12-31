﻿using Latte.Hotload.Compilation;
using Latte.Hotload.NuGet;
using Latte.Hotload.Upgrading;
using Latte.Logging;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;

namespace Latte.Hotload;

public sealed class HotloadableAssembly : IDisposable
{
	public static IReadOnlyDictionary<string, HotloadableAssembly> All => AllAssemblies;
	private static ConcurrentDictionary<string, HotloadableAssembly> AllAssemblies = new();

	public delegate void HotloadableAssemblyHandler( HotloadableAssembly hotloadableAssembly ); 
	public static event HotloadableAssemblyHandler? OnAdded;
	public static event HotloadableAssemblyHandler? OnRemoved;

	public AssemblyInfo AssemblyInfo { get; }
	internal Assembly? Assembly { get; private set; }
	internal IEntryPoint? EntryPoint { get; private set; }

	private FileSystemWatcher? CsProjectWatcher { get; }
	private FileSystemWatcher? CodeWatcher { get; }
	private AssemblyLoadContext? Context { get; set; }
	/// <summary>
	/// A container for all the changed files and the specific change that occurred to them.
	/// </summary>
	private Dictionary<string, WatcherChangeTypes> IncrementalBuildChanges { get; } = new();

	private Logger Log { get; }

	/// <summary>
	/// The <see cref="Task"/> that represents the current build process.
	/// </summary>
	private Task? buildTask;
	/// <summary>
	/// Whether or not a full build has been requested.
	/// </summary>
	private bool buildRequested;
	/// <summary>
	/// Whether or not an incremental build has been requested.
	/// </summary>
	private bool incrementalBuildRequested;

	static HotloadableAssembly()
	{
		AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
	}

	private HotloadableAssembly( in AssemblyInfo assemblyInfo )
	{
		AllAssemblies.TryAdd( assemblyInfo.Name, this );

		AssemblyInfo = assemblyInfo;
		Log = new Logger( $"Hotloader ({AssemblyInfo.Name})", LogLevel.Verbose );

		if ( assemblyInfo.ProjectPath is null )
		{
			OnAdded?.Invoke( this );
			return;
		}

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

		OnAdded?.Invoke( this );
	}

	private HotloadableAssembly( Assembly assembly )
	{
		AssemblyInfo = new AssemblyInfo
		{
			Name = assembly.GetName().Name ?? "Unknown",
			Path = assembly.Location
		};

		AllAssemblies.TryAdd( AssemblyInfo.Name, this );

		Log = new Logger( $"Hotloader ({AssemblyInfo.Name})", LogLevel.Verbose );
		Assembly = assembly;

		OnAdded?.Invoke( this );
	}

	~HotloadableAssembly()
	{
		Dispose();
	}

	internal async Task InitAsync()
	{
		if ( Assembly is null )
			await BuildAsync();
		else
		{
			TryGetEntryPoint( Assembly, out var entryPoint );
			EntryPoint = entryPoint;
		}

		EntryPoint?.Main();
	}

	private async Task BuildAsync( bool incremental = false )
	{
		using var _ = new ScopedTimingLogger( Log );

		// Compile.
		CompileResult compileResult;
		if ( incremental )
		{
			var changedFiles = new Dictionary<string, WatcherChangeTypes>( IncrementalBuildChanges );
			IncrementalBuildChanges.Clear();

			compileResult = await Compiler.IncrementalCompileAsync( AssemblyInfo, changedFiles );
		}
		else
			compileResult = await Compiler.CompileAsync( AssemblyInfo );

		// Check if compile failed.
		if ( !compileResult.WasSuccessful )
		{
			// Check if another build is queued before bailing.
			await PostBuildAsync();
			return;
		}

		// Swap and upgrade the assemblies.
		Swap( compileResult );

		// Check if another build is queued.
		await PostBuildAsync();
	}

	private void Swap( in CompileResult compileResult )
	{
		using var _ = new ScopedTimingLogger( Log );

		var oldAssembly = Assembly;
		var oldEntryPoint = EntryPoint;
		Context?.Unload();

		Context = new AssemblyLoadContext( AssemblyInfo.Name, true );
		Context.Resolving += ResolveContextAssembly;
		var newAssembly = Context.LoadFromStream( compileResult.CompiledAssembly!, compileResult.CompiledAssemblySymbols );
		TryGetEntryPoint( newAssembly, out var newEntryPoint );

		if ( oldAssembly is not null )
		{
			oldEntryPoint?.PreHotload();
			Upgrader.Upgrade( oldAssembly, oldEntryPoint, newAssembly, newEntryPoint );
			newEntryPoint?.PostHotload();
		}

		Assembly = newAssembly;
		EntryPoint = newEntryPoint;
	}

	private Assembly? ResolveContextAssembly( AssemblyLoadContext context, AssemblyName assemblyName )
	{
		var assemblyFileName = assemblyName.Name ?? assemblyName.FullName;

		if ( All.TryGetValue( assemblyFileName, out var hotloadableAssembly ) && hotloadableAssembly.Assembly is not null )
			return hotloadableAssembly.Assembly;

		if ( NuGetManager.IsDllInstalled( assemblyFileName ) )
			return Assembly.LoadFile( Path.GetFullPath( NuGetManager.GetDllPath( assemblyFileName )! ) );

		Log.Error( $"Failed to find assembly: {assemblyName}" );
		return null;
	}

	/// <summary>
	/// Checks if another build needs to be ran after the previous one just finished.
	/// </summary>
	/// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
	private async Task PostBuildAsync()
	{
		if ( !buildRequested && !incrementalBuildRequested )
			return;

		// Need a full build.
		if ( buildRequested )
		{
			buildRequested = false;
			incrementalBuildRequested = false;
			await BuildAsync();
		}
		// Need an incremental build.
		else
		{
			incrementalBuildRequested = false;
			await BuildAsync( true );
		}
	}

	private void OnProjectChanged( object? sender, FileSystemEventArgs args )
	{
		buildRequested = true;
	}

	private void OnFileChanged( object? sender, FileSystemEventArgs args )
	{
		// There could be a false temporary file that gets sent here.
		if ( !args.FullPath.EndsWith( ".cs" ) )
			return;

		// This might be a directory, if it is then skip.
		if ( Directory.Exists( args.FullPath ) )
			return;

		if ( Loggers.Hotloader.IsEnabled( LogLevel.Verbose ) )
			Log.Verbose( $"{args.FullPath} has changed" );

		switch ( args.ChangeType )
		{
			// A C# file was created.
			case WatcherChangeTypes.Created:
				{
					// If a change already exists and is not the file being created then switch to changed.
					if ( IncrementalBuildChanges.TryGetValue( args.FullPath, out var val ) && val != WatcherChangeTypes.Created )
						IncrementalBuildChanges[args.FullPath] = WatcherChangeTypes.Changed;
					// Add created event if it does not exist in the changes.
					else if ( !IncrementalBuildChanges.ContainsKey( args.FullPath ) )
						IncrementalBuildChanges.Add( args.FullPath, WatcherChangeTypes.Created );

					break;
				}
			// A C# file was deleted.
			case WatcherChangeTypes.Deleted:
				{
					if ( IncrementalBuildChanges.TryGetValue( args.FullPath, out var val ) )
					{
						// If the change that currently exists is it being created then just remove the change.
						if ( val == WatcherChangeTypes.Created )
							IncrementalBuildChanges.Remove( args.FullPath );
						// Overwrite any previous change and set it to be deleted.
						else
							IncrementalBuildChanges[args.FullPath] = WatcherChangeTypes.Deleted;
					}
					else if ( !IncrementalBuildChanges.ContainsKey( args.FullPath ) )
						IncrementalBuildChanges.Add( args.FullPath, WatcherChangeTypes.Deleted );

					break;
				}
			// A C# file was changed/renamed.
			case WatcherChangeTypes.Changed:
			case WatcherChangeTypes.Renamed:
				{
					if ( IncrementalBuildChanges.TryGetValue( args.FullPath, out var val ) )
					{
						// If the file was previously created then keep that.
						if ( val == WatcherChangeTypes.Created )
							break;
						// Overwrite any other state with changed.
						else
							IncrementalBuildChanges[args.FullPath] = WatcherChangeTypes.Changed;
					}
					else
						IncrementalBuildChanges.Add( args.FullPath, WatcherChangeTypes.Changed );

					break;
				}
		}

		// Queue build.
		if ( buildTask is null || buildTask.IsCompleted )
			buildTask = BuildAsync( incremental: true );
		else
			incrementalBuildRequested = true;
	}

	public void Dispose()
	{
		OnRemoved?.Invoke( this );

		AllAssemblies.TryRemove( AssemblyInfo.Name, out _ );
		CsProjectWatcher?.Dispose();
		CodeWatcher?.Dispose();
		Context?.Unload();
		Context = null;

		GC.SuppressFinalize( this );
	}

	private static bool TryGetEntryPoint( Assembly assembly, [NotNullWhen( true )] out IEntryPoint? entryPoint )
	{
		var entryPointType = assembly.ExportedTypes.FirstOrDefault( type =>
		{
			foreach ( var @interface in type.GetInterfaces() )
			{
				if ( @interface == typeof( IEntryPoint ) )
					return true;
			}

			return false;
		} );

		if ( entryPointType is null )
		{
			entryPoint = null;
			return false;
		}

		entryPoint = (IEntryPoint)Activator.CreateInstance( entryPointType )!;
		return true;
	}

	private static void OnProcessExit( object? sender, EventArgs e )
	{
		foreach ( var (_, assembly) in All )
			assembly.Dispose();
	}

	internal static HotloadableAssembly New( in AssemblyInfo assemblyInfo ) => new( assemblyInfo );
	internal static HotloadableAssembly New( Assembly assembly ) => new( assembly );
}

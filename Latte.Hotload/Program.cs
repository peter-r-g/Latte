using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Threading;

namespace Latte.Hotload;

public static class Program
{
	public static string CurrentDirectory => Directory.GetCurrentDirectory();
	public static ImmutableArray<string> CommandLineArguments { get; private set; }

	private static HotloadableAssembly EngineAssembly { get; set; } = null!;
	private static ConcurrentStack<HotloadableAssembly> CustomAssemblies { get; } = new();

	private static void Main( string[] args )
	{
		AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
		AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
		CommandLineArguments = args.ToImmutableArray();

		EngineAssembly = new HotloadableAssembly( new AssemblyInfo
		{
			Name = "Latte.Engine",
			ProjectPath = "../Latte.Engine"
		} );

		for ( var i = 0; i < 10; i++ )
			Thread.Sleep( 1000 );
	}

	private static Assembly? ResolveAssembly( object? sender, ResolveEventArgs args )
	{
		var assemblyName = args.Name[..args.Name.IndexOf( ',' )];
		var assemblyPath = Path.GetFullPath( Path.Combine( "nuget", assemblyName + ".dll" ) );
		if ( File.Exists( assemblyPath ) )
			return Assembly.LoadFile( assemblyPath );

		return null;
	}

	internal static void AddAssembly( in AssemblyInfo assemblyInfo )
	{
		var assembly = new HotloadableAssembly( assemblyInfo );
		CustomAssemblies.Push( assembly );
	}

	private static void OnProcessExit( object? sender, EventArgs e )
	{
		while ( CustomAssemblies.TryPop( out var assembly ) )
			assembly.Dispose();

		EngineAssembly.Dispose();
	}
}

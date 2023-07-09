using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
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
		CommandLineArguments = args.ToImmutableArray();

		EngineAssembly = new HotloadableAssembly( new AssemblyInfo
		{
			Name = "Latte.Engine",
			Path = "Latte.Engine.dll",
			ProjectPath = "../Latte.Engine"
		} );

		for ( var i = 0; i < 10; i++ )
			Thread.Sleep( 1000 );
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

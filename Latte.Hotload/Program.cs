using System;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Latte.Hotload;

public static class Program
{
	public static string CurrentDirectory => Directory.GetCurrentDirectory();
	public static ImmutableArray<string> CommandLineArguments { get; private set; }

	private static async Task Main( string[] args )
	{
		AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
		AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
		CommandLineArguments = args.ToImmutableArray();

		await AddAssemblyAsync( new AssemblyInfo
		{
			Name = "Latte.Engine",
			ProjectPath = "../Latte.Engine"
		} );
	}

	private static Assembly? ResolveAssembly( object? sender, ResolveEventArgs args )
	{
		var assemblyName = args.Name[..args.Name.IndexOf( ',' )];
		var assemblyPath = Path.GetFullPath( Path.Combine( "nuget", assemblyName + ".dll" ) );
		if ( File.Exists( assemblyPath ) )
			return Assembly.LoadFile( assemblyPath );

		return null;
	}

	internal static async Task AddAssemblyAsync( AssemblyInfo assemblyInfo )
	{
		var assembly = HotloadableAssembly.New( assemblyInfo );
		await assembly.InitAsync();
	}

	private static void OnProcessExit( object? sender, EventArgs e )
	{
		foreach ( var (_, assembly) in HotloadableAssembly.All )
			assembly.Dispose();
	}
}

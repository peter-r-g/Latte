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
		CommandLineArguments = args.ToImmutableArray();

		await AddAssemblyAsync( new AssemblyInfo
		{
			Name = "Latte.Engine",
			ProjectPath = "../Latte.Engine"
		} );
	}

	internal static async Task AddAssemblyAsync( AssemblyInfo assemblyInfo )
	{
		var assembly = HotloadableAssembly.New( assemblyInfo );
		await assembly.InitAsync();
	}
}

using System.IO;
using System.Threading.Tasks;

namespace Latte.AssetCompiler;

public static class Compiler
{
	public static async Task CompileAsync( string path, bool recursive = true )
	{
		if ( Directory.Exists( path ) )
			await CompileDirectoryAsync( path, recursive );
		else if ( File.Exists( path ) )
			await CompileFileAsync( path );
		else
			throw new FileNotFoundException( path );
	}

	private static async Task CompileDirectoryAsync( string path, bool recursive )
	{
		foreach ( var file in Directory.EnumerateFiles( path, "*.*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly ) )
			await CompileFileAsync( file );
	}

	private static async Task CompileFileAsync( string filePath )
	{
		var extension = Path.GetExtension( filePath );
		if ( extension is null )
			return;

		switch ( extension )
		{
			case ".glsl":
				await ShaderCompiler.CompileAsync( filePath );
				break;
		}
	}
}

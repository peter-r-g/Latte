using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Latte.AssetCompiler;

internal static class ShaderCompiler
{
	private static string? VulkanSdkPath { get; } = Environment.GetEnvironmentVariable( "VULKAN_SDK" );
	private static string? GlslCompilerPath
	{
		get
		{
			if ( VulkanSdkPath is null )
				return null;

			// TODO: Cross platform.
			return Path.Combine( VulkanSdkPath, "Bin", "glslc.exe" );
		}
	}

	internal static async Task CompileAsync( string filePath )
	{
		if ( VulkanSdkPath is null )
			throw new NotSupportedException( "The Vulkan SDK is not installed" );

		var glslC = GlslCompilerPath!;
		var isVertShader = Path.GetFileNameWithoutExtension( filePath ) == "vert";

		var processStartInfo = new ProcessStartInfo()
		{
			FileName = glslC
		};
		var argumentBuilder = new StringBuilder();

		if ( isVertShader )
			argumentBuilder.Append( $"-fshader-stage=vert" );
		else
			argumentBuilder.Append( $"-fshader-stage=frag" );

		argumentBuilder.Append( " \"" );
		argumentBuilder.Append( filePath );
		argumentBuilder.Append( '"' );

		argumentBuilder.Append( " -o \"" );
		argumentBuilder.Append( Path.ChangeExtension( filePath, ".spv" ) );
		argumentBuilder.Append( '"' );

		processStartInfo.Arguments = argumentBuilder.ToString();
		var process = Process.Start( processStartInfo ) ?? throw new UnreachableException();
		await process.WaitForExitAsync();
	}
}

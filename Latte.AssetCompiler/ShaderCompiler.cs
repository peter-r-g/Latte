using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Latte.AssetCompiler;

// TODO: Make this not shit.
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
	private static string? HlslCompilerPath
	{
		get
		{
			if ( VulkanSdkPath is null )
				return null;

			// TODO: Cross platform.
			return Path.Combine( VulkanSdkPath, "Bin", "dxc.exe" );
		}
	}

	internal static async Task CompileAsync( string filePath )
	{
		if ( VulkanSdkPath is null )
			throw new NotSupportedException( "The Vulkan SDK is not installed or the OS is missing the VULKAN_SDK environment variable" );

		var isGlsl = filePath.EndsWith( ".glsl" );
		var compilerPath = isGlsl
			? GlslCompilerPath!
			: HlslCompilerPath!;

		var processStartInfo = new ProcessStartInfo()
		{
			FileName = compilerPath,
			Arguments = isGlsl ? BuildGlslArguments( filePath ) : BuildHlslArguments( filePath )
		};

		var process = Process.Start( processStartInfo ) ?? throw new UnreachableException();
		await process.WaitForExitAsync();
	}

	private static string BuildGlslArguments( string filePath )
	{
		var isVertShader = Path.GetFileNameWithoutExtension( filePath ) == "vert";
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

		return argumentBuilder.ToString();
	}

	private static string BuildHlslArguments( string filePath )
	{
		var isVertShader = Path.GetFileNameWithoutExtension( filePath ) == "vert";
		var argumentBuilder = new StringBuilder();

		// TODO: Don't hard code entry point.
		argumentBuilder.Append( "-fspv-entrypoint-name=main -E main " );

		if ( isVertShader )
			argumentBuilder.Append( "-T vs_6_7 " );
		else
			argumentBuilder.Append( "-T ps_6_7 " );

		argumentBuilder.Append( "-fspv-target-env=vulkan1.3 -spirv " );

		argumentBuilder.Append( "-Fo \"" );
		argumentBuilder.Append( Path.ChangeExtension( filePath, ".spv" ) );
		argumentBuilder.Append( "\" \"" );

		argumentBuilder.Append( filePath );
		argumentBuilder.Append( '"' );

		return argumentBuilder.ToString();
	}
}

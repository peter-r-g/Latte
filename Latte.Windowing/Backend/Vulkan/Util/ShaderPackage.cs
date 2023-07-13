using Silk.NET.Vulkan;
using System.Diagnostics.CodeAnalysis;

namespace Latte.Windowing.Backend.Vulkan;

internal readonly struct ShaderPackage
{
	internal required ShaderModule VertexShaderModule { get; init; }
	internal required ShaderModule FragmentShaderModule { get; init; }

	[SetsRequiredMembers]
	internal ShaderPackage( in ShaderModule vertexShaderModule, in ShaderModule fragmentShaderModule )
	{
		VertexShaderModule = vertexShaderModule;
		FragmentShaderModule = fragmentShaderModule;
	}
}

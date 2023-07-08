using Latte.Windowing.Backend.Vulkan;
using Silk.NET.Vulkan;
using System;
using System.IO;

namespace Latte.Windowing.Assets;

public sealed class Shader
{
	public ReadOnlyMemory<byte> VertexShaderCode { get; }
	public string VertexShaderEntryPoint { get; }
	public ReadOnlyMemory<byte> FragmentShaderCode { get; }
	public string FragmentShaderEntryPoint { get; }

	internal ShaderModule VertexShaderModule { get; private set; }
	internal ShaderModule FragmentShaderModule { get; private set; }

	public Shader( in ReadOnlyMemory<byte> vertexShaderCode, string vertexShaderEntryPoint,
		in ReadOnlyMemory<byte> fragmentShaderCode, string fragmentShaderEntryPoint )
	{
		VertexShaderCode = vertexShaderCode;
		VertexShaderEntryPoint = vertexShaderEntryPoint;
		FragmentShaderCode = fragmentShaderCode;
		FragmentShaderEntryPoint = fragmentShaderEntryPoint;
	}

	public void Initialize( IRenderingBackend backend )
	{
		if ( backend is not VulkanBackend vulkanBackend )
			return;

		VertexShaderModule = vulkanBackend.CreateShaderModule( VertexShaderCode.Span );
		FragmentShaderModule = vulkanBackend.CreateShaderModule( FragmentShaderCode.Span );
	}

	public static Shader FromPath( string vertexShaderPath, string fragmentShaderPath )
	{
		var vertexShaderBytes = File.ReadAllBytes( vertexShaderPath );
		var fragmentShaderBytes = File.ReadAllBytes( fragmentShaderPath );

		return new Shader( vertexShaderBytes, "main", fragmentShaderBytes, "main" );
	}
}

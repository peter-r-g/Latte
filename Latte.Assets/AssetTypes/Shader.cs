using System;
using System.Diagnostics.CodeAnalysis;
using Zio;

namespace Latte.Assets;

public sealed class Shader
{
	public required ReadOnlyMemory<byte> VertexShaderCode { get; init; }
	public required string VertexShaderEntryPoint { get; init; }
	public required ReadOnlyMemory<byte> FragmentShaderCode { get; init; }
	public required string FragmentShaderEntryPoint { get; init; }

	[SetsRequiredMembers]
	public Shader( in ReadOnlyMemory<byte> vertexShaderCode, string vertexShaderEntryPoint,
		in ReadOnlyMemory<byte> fragmentShaderCode, string fragmentShaderEntryPoint )
	{
		VertexShaderCode = vertexShaderCode;
		VertexShaderEntryPoint = vertexShaderEntryPoint;
		FragmentShaderCode = fragmentShaderCode;
		FragmentShaderEntryPoint = fragmentShaderEntryPoint;
	}

	public static Shader FromPath( in UPath vertexShaderPath, in UPath fragmentShaderPath,
		string vertexShaderEntryPoint = "main", string fragmentShaderEntryPoint = "main" )
	{
		var vertexShaderBytes = FileSystems.Assets.ReadAllBytes( vertexShaderPath );
		var fragmentShaderBytes = FileSystems.Assets.ReadAllBytes( fragmentShaderPath );

		return new Shader( vertexShaderBytes, vertexShaderEntryPoint, fragmentShaderBytes, fragmentShaderEntryPoint );
	}
}

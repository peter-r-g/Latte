using System;
using System.Diagnostics.CodeAnalysis;
using Zio;

namespace Latte.Assets;

[method: SetsRequiredMembers]
public sealed class Shader( in ReadOnlyMemory<byte> code, string entryPoint )
{
	public required ReadOnlyMemory<byte> Code { get; init; } = code;
	public required string EntryPoint { get; init; } = entryPoint;

	public static Shader FromPath( in UPath shaderPath, string shaderEntryPoint = "main" )
	{
		var code = FileSystems.Assets.ReadAllBytes( shaderPath.ToAbsolute() );
		return new Shader( code, shaderEntryPoint );
	}
}

using System.Diagnostics.CodeAnalysis;

namespace Latte.Windowing.Renderer.Abstractions;

[method: SetsRequiredMembers]
internal struct BufferElement( string name, BufferDataType type )
{
	internal required string Name { get; init; } = name;
	internal required BufferDataType Type { get; init; } = type;

	internal ulong Offset { get; set; }
}

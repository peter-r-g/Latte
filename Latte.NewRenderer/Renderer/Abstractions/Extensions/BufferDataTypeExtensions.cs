using System;

namespace Latte.Windowing.Renderer.Abstractions.Extensions;

internal static class BufferDataTypeExtensions
{
	internal static ulong Size( this BufferDataType type ) => type switch
	{
		BufferDataType.Byte => 1,
		BufferDataType.Byte2 => 1 * 2,
		BufferDataType.Byte3 => 1 * 3,
		BufferDataType.Byte4 => 1 * 4,
		BufferDataType.Short => 2,
		BufferDataType.Short2 => 2 * 2,
		BufferDataType.Short3 => 2 * 3,
		BufferDataType.Short4 => 2 * 4,
		BufferDataType.Int => 4,
		BufferDataType.Int2 => 4 * 2,
		BufferDataType.Int3 => 4 * 3,
		BufferDataType.Int4 => 4 * 4,
		BufferDataType.Float => 4,
		BufferDataType.Float2 => 4 * 2,
		BufferDataType.Float3 => 4 * 3,
		BufferDataType.Float4 => 4 * 4,
		BufferDataType.Double => 8,
		BufferDataType.Double2 => 8 * 2,
		BufferDataType.Double3 => 8 * 3,
		BufferDataType.Double4 => 8 * 4,
		BufferDataType.Bool => 1,
		BufferDataType.Mat3 => 4 * 3 * 3,
		BufferDataType.Mat4 => 4 * 4 * 4,
		_ => throw new ArgumentOutOfRangeException( nameof( type ) ),
	};
}

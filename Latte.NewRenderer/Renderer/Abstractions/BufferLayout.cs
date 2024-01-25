using Latte.NewRenderer.Renderer.Abstractions.Extensions;
using System.Collections.Immutable;

namespace Latte.NewRenderer.Renderer.Abstractions;

internal sealed class BufferLayout
{
	internal ImmutableArray<BufferElement> Elements { get; }
	internal ulong Stride { get; }

	internal BufferLayout( params BufferElement[] elements )
	{
		var offset = 0ul;
		for ( var i = 0; i < elements.Length; i++ )
		{
			var element = elements[i];

			element.Offset = offset;
			offset += element.Type.Size();

			elements[i] = element;
		}

		Elements = elements.ToImmutableArray();
		Stride = offset;
	}
}

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Latte.Windowing.Backend.Vulkan;

internal struct QueueFamilyIndices
{
	internal uint? GraphicsFamily { get; set; }
	internal uint? PresentFamily { get; set; }

	[MemberNotNullWhen( true, nameof( GraphicsFamily ), nameof( PresentFamily ) )]
	internal readonly bool IsComplete()
	{
		return GraphicsFamily is not null &&
			PresentFamily is not null;
	}

	internal readonly HashSet<uint> GetUniqueFamilies()
	{
		if ( !IsComplete() )
			throw new InvalidOperationException( "Attempted to get unique families when the indices are not complete" );

		return new HashSet<uint>()
		{
			GraphicsFamily.Value,
			PresentFamily.Value
		};
	}
}

﻿using Silk.NET.Vulkan;

namespace Latte.NewRenderer.Wrappers;

internal readonly struct AllocatedBuffer
{
	internal readonly Buffer Buffer;
	internal readonly Allocation Allocation;

	internal AllocatedBuffer( Buffer buffer, Allocation allocation )
	{
		Buffer = buffer;
		Allocation = allocation;
	}
}

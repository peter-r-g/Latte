﻿using Silk.NET.Vulkan;
using System.Diagnostics.CodeAnalysis;

namespace Latte.NewRenderer.Vulkan.Allocations;

[method: SetsRequiredMembers]
internal struct Allocation( DeviceMemory memory, uint memoryType, ulong offset, ulong size )
{
	internal required DeviceMemory Memory = memory;
	internal required uint MemoryType = memoryType;
	internal required ulong Offset = offset;
	internal required ulong Size = size;
}
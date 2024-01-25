using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System;
using VMASharp;

namespace Latte.NewRenderer.Renderer.Vulkan.Extensions;

internal static class AllocationExtensions
{
	internal static unsafe void SetMemory<T>( this Allocation allocation, T data ) where T : unmanaged
	{
		void* dataPtr = (void*)allocation.Map();
		Marshal.StructureToPtr( data, (nint)dataPtr, false );
		allocation.Unmap();
	}

	internal static unsafe void SetMemory<T>( this Allocation allocation, ReadOnlySpan<T> data ) where T : unmanaged
	{
		void* dataPtr = (void*)allocation.Map();
		data.CopyTo( new Span<T>( dataPtr, data.Length ) );
		allocation.Unmap();
	}

	internal static unsafe void SetMemory<T>( this Allocation allocation, T data, ulong dataSize, int index ) where T : unmanaged
	{
		void* dataPtr;

		Apis.Vk.MapMemory( VkContext.LogicalDevice, allocation.DeviceMemory, (ulong)allocation.Offset, dataSize + dataSize * (ulong)index, 0, &dataPtr ).AssertSuccess();
		Marshal.StructureToPtr( data, (nint)dataPtr + (nint)(dataSize * (ulong)index), false );
		Apis.Vk.UnmapMemory( VkContext.LogicalDevice, allocation.DeviceMemory );
	}

	internal static unsafe void SetMemory( this Allocation allocation, nint srcDataPtr, ulong count, nint offset = 0 )
	{
		var dataPtr = (void*)allocation.Map();
		Unsafe.CopyBlock( (void*)((nint)dataPtr + offset), (void*)srcDataPtr, (uint)count );
		allocation.Unmap();
	}
}

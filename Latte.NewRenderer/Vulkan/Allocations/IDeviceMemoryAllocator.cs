using Silk.NET.Vulkan;
using System;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Latte.NewRenderer.Vulkan.Allocations;

internal interface IDeviceMemoryAllocator : IDisposable
{
	int TotalAllocationCount { get; }

	int GetAllocationCount( uint memoryType );
	ulong GetAllocationSize( uint memoryType );

	AllocatedBuffer AllocateBuffer( Buffer buffer, MemoryPropertyFlags memoryFlags );
	AllocatedImage AllocateImage( Image image, MemoryPropertyFlags memoryFlags );

	void SetMemory<T>( Allocation allocation, T data, bool preserveMap = false ) where T : unmanaged;
	void SetMemory<T>( Allocation allocation, ReadOnlySpan<T> data, bool preserveMap = false ) where T : unmanaged;
	void SetMemory<T>( Allocation allocation, T data, ulong dataSize, int index ) where T : unmanaged;
	void SetMemory( Allocation allocation, nint srcDataPtr, ulong count, nint offset = 0, bool preserveMap = false );

	void Free( Allocation allocation );
}

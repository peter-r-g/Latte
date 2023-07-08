using Silk.NET.Vulkan;
using System;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanBuffer : IDisposable
{
	internal LogicalGpu Owner { get; }

	internal Buffer Buffer { get; }
	internal DeviceMemory Memory { get; }
	internal ulong Size { get; }

	private bool disposed;

	internal VulkanBuffer( in Buffer buffer, in DeviceMemory memory, ulong size, LogicalGpu owner )
	{
		Buffer = buffer;
		Memory = memory;
		Size = size;
		Owner = owner;
	}

	~VulkanBuffer()
	{
		Dispose();
	}

	public unsafe void Dispose()
	{
		if ( disposed )
			return;

		disposed = true;
		Apis.Vk.DestroyBuffer( Owner, Buffer, null );
		Apis.Vk.FreeMemory( Owner, Memory, null );

		GC.SuppressFinalize( this );
	}

	internal unsafe void SetMemory<T>( ReadOnlySpan<T> data, ulong offset = 0 ) where T : unmanaged
	{
		void* dataPtr;
		if ( Apis.Vk.MapMemory( Owner, Memory, offset, Size, 0, &dataPtr ) != Result.Success )
			throw new ApplicationException( "Failed to map buffer memory" );

		data.CopyTo( new Span<T>( dataPtr, data.Length ) );
		Apis.Vk.UnmapMemory( Owner, Memory );
	}

	public static implicit operator Buffer( VulkanBuffer vulkanBuffer ) => vulkanBuffer.Buffer;
}

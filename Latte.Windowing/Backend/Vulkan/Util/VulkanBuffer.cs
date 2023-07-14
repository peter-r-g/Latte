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

	private VulkanBuffer( in Buffer buffer, in DeviceMemory memory, ulong size, LogicalGpu owner )
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

	internal unsafe void SetMemory<T>( ReadOnlySpan<T> data, ulong offset = 0 ) where T : unmanaged
	{
		if ( disposed )
			throw new ObjectDisposedException( nameof( VulkanBuffer ) );

		void* dataPtr;
		if ( Apis.Vk.MapMemory( Owner, Memory, offset, Size, 0, &dataPtr ) != Result.Success )
			throw new ApplicationException( "Failed to map buffer memory" );

		data.CopyTo( new Span<T>( dataPtr, data.Length ) );
		Apis.Vk.UnmapMemory( Owner, Memory );
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

	public static implicit operator Buffer( VulkanBuffer vulkanBuffer )
	{
		if ( vulkanBuffer.disposed )
			throw new ObjectDisposedException( nameof( VulkanBuffer ) );

		return vulkanBuffer.Buffer;
	}

	internal static unsafe VulkanBuffer New( LogicalGpu logicalGpu, ulong size, BufferUsageFlags usageFlags, MemoryPropertyFlags memoryFlags,
		SharingMode sharingMode = SharingMode.Exclusive )
	{
		var bufferInfo = new BufferCreateInfo
		{
			SType = StructureType.BufferCreateInfo,
			Size = size,
			Usage = usageFlags,
			SharingMode = sharingMode
		};

		if ( Apis.Vk.CreateBuffer( logicalGpu, bufferInfo, null, out var buffer ) != Result.Success )
			throw new ApplicationException( "Failed to create Vulkan buffer" );

		var requirements = Apis.Vk.GetBufferMemoryRequirements( logicalGpu, buffer );
		var allocateInfo = new MemoryAllocateInfo
		{
			SType = StructureType.MemoryAllocateInfo,
			AllocationSize = requirements.Size,
			MemoryTypeIndex = logicalGpu.FindMemoryType( requirements.MemoryTypeBits, memoryFlags )
		};

		if ( Apis.Vk.AllocateMemory( logicalGpu, allocateInfo, null, out var bufferMemory ) != Result.Success )
			throw new ApplicationException( "Failed to allocate Vulkan buffer memory" );

		if ( Apis.Vk.BindBufferMemory( logicalGpu, buffer, bufferMemory, 0 ) != Result.Success )
			throw new ApplicationException( "Failed to bind buffer memory to buffer" );

		var vulkanBuffer = new VulkanBuffer( buffer, bufferMemory, size, logicalGpu );
		logicalGpu.DisposeQueue.Enqueue( vulkanBuffer.Dispose );
		return vulkanBuffer;
	}
}

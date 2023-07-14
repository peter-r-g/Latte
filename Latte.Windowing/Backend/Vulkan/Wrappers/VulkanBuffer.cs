using Latte.Windowing.Extensions;
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

	public unsafe void Dispose()
	{
		if ( disposed )
			return;

		Apis.Vk.DestroyBuffer( Owner, Buffer, null );
		Apis.Vk.FreeMemory( Owner, Memory, null );

		GC.SuppressFinalize( this );
		disposed = true;
	}

	internal unsafe void SetMemory<T>( ReadOnlySpan<T> data, ulong offset = 0 ) where T : unmanaged
	{
		if ( disposed )
			throw new ObjectDisposedException( nameof( VulkanBuffer ) );

		void* dataPtr;
		Apis.Vk.MapMemory( Owner, Memory, offset, Size, 0, &dataPtr ).Verify();
		data.CopyTo( new Span<T>( dataPtr, data.Length ) );
		Apis.Vk.UnmapMemory( Owner, Memory );
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

		Apis.Vk.CreateBuffer( logicalGpu, bufferInfo, null, out var buffer ).Verify();

		var requirements = Apis.Vk.GetBufferMemoryRequirements( logicalGpu, buffer );
		var allocateInfo = new MemoryAllocateInfo
		{
			SType = StructureType.MemoryAllocateInfo,
			AllocationSize = requirements.Size,
			MemoryTypeIndex = logicalGpu.FindMemoryType( requirements.MemoryTypeBits, memoryFlags )
		};

		Apis.Vk.AllocateMemory( logicalGpu, allocateInfo, null, out var bufferMemory ).Verify();
		Apis.Vk.BindBufferMemory( logicalGpu, buffer, bufferMemory, 0 ).Verify();

		var vulkanBuffer = new VulkanBuffer( buffer, bufferMemory, size, logicalGpu );
		logicalGpu.DisposeQueue.Enqueue( vulkanBuffer.Dispose );
		return vulkanBuffer;
	}
}

using Latte.Windowing.Extensions;
using Silk.NET.Vulkan;
using System;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanBuffer : VulkanWrapper
{
	internal Buffer Buffer { get; }
	internal DeviceMemory Memory { get; }
	internal ulong Size { get; }

	private VulkanBuffer( in Buffer buffer, in DeviceMemory memory, ulong size, LogicalGpu owner ) : base( owner )
	{
		Buffer = buffer;
		Memory = memory;
		Size = size;
	}

	public unsafe override void Dispose()
	{
		if ( Disposed )
			return;

		Apis.Vk.DestroyBuffer( LogicalGpu!, Buffer, null );
		Apis.Vk.FreeMemory( LogicalGpu!, Memory, null );

		GC.SuppressFinalize( this );
		Disposed = true;
	}

	internal unsafe void SetMemory<T>( ReadOnlySpan<T> data, ulong offset = 0 ) where T : unmanaged
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( VulkanBuffer ) );

		void* dataPtr;
		Apis.Vk.MapMemory( LogicalGpu!, Memory, offset, Size, 0, &dataPtr ).Verify();
		data.CopyTo( new Span<T>( dataPtr, data.Length ) );
		Apis.Vk.UnmapMemory( LogicalGpu!, Memory );
	}

	public static implicit operator Buffer( VulkanBuffer vulkanBuffer )
	{
		if ( vulkanBuffer.Disposed )
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

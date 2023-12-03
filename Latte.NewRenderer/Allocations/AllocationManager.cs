using Latte.NewRenderer.Extensions;
using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Latte.NewRenderer.Allocations;

internal sealed class AllocationManager : IDisposable
{
	private readonly PhysicalDevice physicalDevice;
	private readonly Device logicalDevice;

	private readonly List<DeviceMemory> memoryAllocations;
	private bool disposed;

	internal AllocationManager( PhysicalDevice physicalDevice, Device logicalDevice )
	{
		this.physicalDevice = physicalDevice;
		this.logicalDevice = logicalDevice;

		var properties = Apis.Vk.GetPhysicalDeviceProperties( physicalDevice );
		memoryAllocations = new List<DeviceMemory>( (int)properties.Limits.MaxMemoryAllocationCount );
	}

	~AllocationManager()
	{
		Dispose( disposing: false );
	}

	internal unsafe AllocatedBuffer AllocateBuffer( Buffer buffer, MemoryPropertyFlags memoryFlags )
	{
		if ( memoryAllocations.Count >= memoryAllocations.Capacity )
			throw new OutOfMemoryException( $"No more Vulkan allocations can be made (Maximum is {memoryAllocations.Capacity})" );

		var requirements = Apis.Vk.GetBufferMemoryRequirements( logicalDevice, buffer.Validate() );
		var allocateInfo = VkInfo.AllocateMemory( requirements.Size, FindMemoryType( requirements.MemoryTypeBits, memoryFlags ) );

		Apis.Vk.AllocateMemory( logicalDevice, allocateInfo, null, out var memory ).Verify();
		Apis.Vk.BindBufferMemory( logicalDevice, buffer, memory.Validate(), 0 ).Verify();

		memoryAllocations.Add( memory );
		return new AllocatedBuffer( buffer, new Allocation( memory, 0 ) );
	}

	internal unsafe AllocatedImage AllocateImage( Image image, MemoryPropertyFlags memoryFlags )
	{
		if ( memoryAllocations.Count >= memoryAllocations.Capacity )
			throw new OutOfMemoryException( $"No more Vulkan allocations can be made (Maximum is {memoryAllocations.Capacity})" );

		var requirements = Apis.Vk.GetImageMemoryRequirements( logicalDevice, image.Validate() );
		var allocateInfo = VkInfo.AllocateMemory( requirements.Size, FindMemoryType( requirements.MemoryTypeBits, memoryFlags ) );

		Apis.Vk.AllocateMemory( logicalDevice, allocateInfo, null, out var memory ).Verify();
		Apis.Vk.BindImageMemory( logicalDevice, image, memory, 0 );

		memoryAllocations.Add( memory );
		return new AllocatedImage( image, new Allocation( memory, 0 ) );
	}

	internal unsafe void SetMemory<T>( Allocation allocation, ReadOnlySpan<T> data ) where T : unmanaged
	{
		void* dataPtr;
		var dataSize = (ulong)(sizeof( T ) * data.Length);

		Apis.Vk.MapMemory( logicalDevice, allocation.Memory, allocation.Offset, dataSize, 0, &dataPtr ).Verify();
		data.CopyTo( new Span<T>( dataPtr, data.Length ) );
		Apis.Vk.UnmapMemory( logicalDevice, allocation.Memory );
	}

	private uint FindMemoryType( uint typeFilter, MemoryPropertyFlags properties )
	{
		var memoryProperties = Apis.Vk.GetPhysicalDeviceMemoryProperties( physicalDevice );
		for ( var i = 0; i < memoryProperties.MemoryTypeCount; i++ )
		{
			if ( (typeFilter & 1 << i) != 0 && (memoryProperties.MemoryTypes[i].PropertyFlags & properties) == properties )
				return (uint)i;
		}

		throw new ApplicationException( "Failed to find suitable memory type" );
	}

	private unsafe void Dispose( bool disposing )
	{
		if ( !disposed )
		{
			if ( disposing )
			{
			}

			foreach ( var allocatedMemory in memoryAllocations )
				Apis.Vk.FreeMemory( logicalDevice, allocatedMemory, null );

			disposed = true;
		}
	}

	public void Dispose()
	{
		Dispose( disposing: true );
		GC.SuppressFinalize( this );
	}
}

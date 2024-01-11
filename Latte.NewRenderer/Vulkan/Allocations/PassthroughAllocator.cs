using Latte.NewRenderer.Vulkan.Exceptions;
using Latte.NewRenderer.Vulkan.Extensions;
using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Latte.NewRenderer.Vulkan.Allocations;

internal sealed class PassthroughAllocator : IDeviceMemoryAllocator
{
	private readonly List<DeviceMemory> memoryAllocations;
	private readonly Dictionary<Allocation, nint> preservedMaps = [];
	private bool disposed;

	internal PassthroughAllocator()
	{
		memoryAllocations = new List<DeviceMemory>( (int)VkContext.PhysicalDeviceInfo.Properties.Limits.MaxMemoryAllocationCount );
	}

	~PassthroughAllocator()
	{
		Dispose( disposing: false );
	}

	public unsafe AllocatedBuffer AllocateBuffer( Buffer buffer, MemoryPropertyFlags memoryFlags )
	{
		VkInvalidHandleException.ThrowIfInvalid( buffer );

		if ( memoryAllocations.Count >= memoryAllocations.Capacity )
			throw new OutOfMemoryException( $"No more Vulkan allocations can be made (Maximum is {memoryAllocations.Capacity})" );

		var requirements = Apis.Vk.GetBufferMemoryRequirements( VkContext.LogicalDevice, buffer );
		var allocateInfo = VkInfo.AllocateMemory( requirements.Size, FindMemoryType( requirements.MemoryTypeBits, memoryFlags ) );

		Apis.Vk.AllocateMemory( VkContext.LogicalDevice, allocateInfo, null, out var memory ).Verify();
		VkInvalidHandleException.ThrowIfInvalid( memory );
		Apis.Vk.BindBufferMemory( VkContext.LogicalDevice, buffer, memory, 0 ).Verify();

		memoryAllocations.Add( memory );
		return new AllocatedBuffer( buffer, new Allocation( memory, requirements.MemoryTypeBits, 0, requirements.Size ) );
	}

	public unsafe AllocatedImage AllocateImage( Image image, MemoryPropertyFlags memoryFlags )
	{
		VkInvalidHandleException.ThrowIfInvalid( image );

		if ( memoryAllocations.Count >= memoryAllocations.Capacity )
			throw new OutOfMemoryException( $"No more Vulkan allocations can be made (Maximum is {memoryAllocations.Capacity})" );

		var requirements = Apis.Vk.GetImageMemoryRequirements( VkContext.LogicalDevice, image );
		var allocateInfo = VkInfo.AllocateMemory( requirements.Size, FindMemoryType( requirements.MemoryTypeBits, memoryFlags ) );

		Apis.Vk.AllocateMemory( VkContext.LogicalDevice, allocateInfo, null, out var memory ).Verify();
		Apis.Vk.BindImageMemory( VkContext.LogicalDevice, image, memory, 0 );

		memoryAllocations.Add( memory );
		return new AllocatedImage( image, new Allocation( memory, requirements.MemoryTypeBits, 0, requirements.Size ) );
	}

	public unsafe void SetMemory<T>( Allocation allocation, T data, bool preserveMap = false ) where T : unmanaged
	{
		var dataSize = (ulong)sizeof( T );

		void* dataPtr = RetrieveDataPointer( allocation, dataSize, preserveMap );
		Marshal.StructureToPtr( data, (nint)dataPtr, false );
		ReturnDataPointer( allocation, dataPtr, preserveMap );
	}

	public unsafe void SetMemory<T>( Allocation allocation, ReadOnlySpan<T> data, bool preserveMap = false ) where T : unmanaged
	{
		var dataSize = (ulong)(sizeof( T ) * data.Length);

		void* dataPtr = RetrieveDataPointer( allocation, dataSize, preserveMap );
		data.CopyTo( new Span<T>( dataPtr, data.Length ) );
		ReturnDataPointer( allocation, dataPtr, preserveMap );
	}

	public unsafe void SetMemory<T>( Allocation allocation, T data, ulong dataSize, int index ) where T : unmanaged
	{
		void* dataPtr;

		Apis.Vk.MapMemory( VkContext.LogicalDevice, allocation.Memory, allocation.Offset, dataSize + dataSize * (ulong)index, 0, &dataPtr ).Verify();
		Marshal.StructureToPtr( data, (nint)dataPtr + (nint)(dataSize * (ulong)index), false );
		Apis.Vk.UnmapMemory( VkContext.LogicalDevice, allocation.Memory );
	}

	public unsafe void SetMemory( Allocation allocation, nint srcDataPtr, ulong count, nint offset = 0, bool preserveMap = false )
	{
		var dataPtr = RetrieveDataPointer( allocation, count, preserveMap );
		Unsafe.CopyBlock( (void*)((nint)dataPtr + offset), (void*)srcDataPtr, (uint)count );
		ReturnDataPointer( allocation, dataPtr, preserveMap );
	}

	public unsafe void Free( Allocation allocation )
	{
		Apis.Vk.FreeMemory( VkContext.LogicalDevice, allocation.Memory, null );
	}

	private unsafe void* RetrieveDataPointer( Allocation allocation, ulong dataSize, bool preserveMap )
	{
		void* dataPtr;

		if ( preserveMap && preservedMaps.TryGetValue( allocation, out var mappedDataPtr ) )
			dataPtr = (void*)mappedDataPtr;
		else
			Apis.Vk.MapMemory( VkContext.LogicalDevice, allocation.Memory, allocation.Offset, dataSize, 0, &dataPtr ).Verify();

		return dataPtr;
	}

	private unsafe void ReturnDataPointer( Allocation allocation, void* dataPtr, bool preserveMap )
	{
		if ( preserveMap )
		{
			if ( !preservedMaps.ContainsKey( allocation ) )
				preservedMaps.Add( allocation, (nint)dataPtr );

			return;
		}

		preservedMaps.Remove( allocation );
		Apis.Vk.UnmapMemory( VkContext.LogicalDevice, allocation.Memory );
	}

	private static uint FindMemoryType( uint typeFilter, MemoryPropertyFlags properties )
	{
		var memoryProperties = VkContext.PhysicalDeviceInfo.MemoryProperties;
		for ( var i = 0; i < memoryProperties.MemoryTypeCount; i++ )
		{
			if ( (typeFilter & 1 << i) != 0 && (memoryProperties.MemoryTypes[i].PropertyFlags & properties) == properties )
				return (uint)i;
		}

		throw new VkException( "Failed to find suitable memory type" );
	}

	private unsafe void Dispose( bool disposing )
	{
		if ( !disposed )
		{
			if ( disposing )
			{
			}

			foreach ( var (allocation, _) in preservedMaps )
				Apis.Vk.UnmapMemory( VkContext.LogicalDevice, allocation.Memory );

			foreach ( var allocatedMemory in memoryAllocations )
				Apis.Vk.FreeMemory( VkContext.LogicalDevice, allocatedMemory, null );

			disposed = true;
		}
	}

	public void Dispose()
	{
		Dispose( disposing: true );
		GC.SuppressFinalize( this );
	}
}

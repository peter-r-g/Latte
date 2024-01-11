using Latte.NewRenderer.Vulkan.Exceptions;
using Latte.NewRenderer.Vulkan.Extensions;
using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Latte.NewRenderer.Vulkan.Allocations;

internal sealed class PassthroughAllocator : IDeviceMemoryAllocator
{
	public int TotalAllocationCount => memoryAllocations.Count;

	private int currentAllocationId;
	private bool disposed;

	private readonly List<DeviceMemory> memoryAllocations;
	private readonly Dictionary<Allocation, nint> preservedMaps = [];
	private readonly int[] memoryTypeAllocationCounts;
	private readonly ulong[] memoryTypeAllocationSizes;
	private readonly object useLock = new();

	internal PassthroughAllocator()
	{
		memoryAllocations = new List<DeviceMemory>( (int)VkContext.PhysicalDeviceInfo.Properties.Limits.MaxMemoryAllocationCount );
		var memoryTypeCount = VkContext.PhysicalDeviceInfo.MemoryProperties.MemoryTypeCount;
		memoryTypeAllocationCounts = new int[memoryTypeCount];
		memoryTypeAllocationSizes = new ulong[memoryTypeCount];
	}

	~PassthroughAllocator()
	{
		Dispose( disposing: false );
	}

	public int GetAllocationCount( uint memoryType )
	{
		if ( memoryType >= memoryTypeAllocationCounts.Length )
			throw new ArgumentOutOfRangeException( nameof( memoryType ) );

		return memoryTypeAllocationCounts[memoryType];
	}

	public ulong GetAllocationSize( uint memoryType )
	{
		if ( memoryType >= memoryTypeAllocationSizes.Length )
			throw new ArgumentOutOfRangeException( nameof( memoryType ) );

		return memoryTypeAllocationSizes[memoryType];
	}

	public unsafe AllocatedBuffer AllocateBuffer( Buffer buffer, MemoryPropertyFlags memoryFlags )
	{
		Monitor.Enter( useLock );
		try
		{
			VkInvalidHandleException.ThrowIfInvalid( buffer );

			if ( memoryAllocations.Count >= memoryAllocations.Capacity )
				throw new OutOfMemoryException( $"No more Vulkan allocations can be made (Maximum is {memoryAllocations.Capacity})" );

			var requirements = Apis.Vk.GetBufferMemoryRequirements( VkContext.LogicalDevice, buffer );
			var memoryType = FindMemoryType( requirements.MemoryTypeBits, memoryFlags );
			var allocateInfo = VkInfo.AllocateMemory( requirements.Size, memoryType );

			Apis.Vk.AllocateMemory( VkContext.LogicalDevice, allocateInfo, null, out var memory ).AssertSuccess();
			VkInvalidHandleException.ThrowIfInvalid( memory );
			Apis.Vk.BindBufferMemory( VkContext.LogicalDevice, buffer, memory, 0 ).AssertSuccess();

			memoryAllocations.Add( memory );
			memoryTypeAllocationCounts[memoryType]++;
			memoryTypeAllocationSizes[memoryType] += requirements.Size;

			return new AllocatedBuffer( buffer, new Allocation( currentAllocationId++, memory, memoryType, 0, requirements.Size ) );
		}
		finally
		{
			Monitor.Exit( useLock );
		}
	}

	public unsafe AllocatedImage AllocateImage( Image image, MemoryPropertyFlags memoryFlags )
	{
		Monitor.Enter( useLock );
		try
		{
			VkInvalidHandleException.ThrowIfInvalid( image );

			if ( memoryAllocations.Count >= memoryAllocations.Capacity )
				throw new OutOfMemoryException( $"No more Vulkan allocations can be made (Maximum is {memoryAllocations.Capacity})" );

			var requirements = Apis.Vk.GetImageMemoryRequirements( VkContext.LogicalDevice, image );
			var memoryType = FindMemoryType( requirements.MemoryTypeBits, memoryFlags );
			var allocateInfo = VkInfo.AllocateMemory( requirements.Size, memoryType );

			Apis.Vk.AllocateMemory( VkContext.LogicalDevice, allocateInfo, null, out var memory ).AssertSuccess();
			Apis.Vk.BindImageMemory( VkContext.LogicalDevice, image, memory, 0 );

			memoryAllocations.Add( memory );
			memoryTypeAllocationCounts[memoryType]++;
			memoryTypeAllocationSizes[memoryType] += requirements.Size;

			return new AllocatedImage( image, new Allocation( currentAllocationId++, memory, memoryType, 0, requirements.Size ) );
		}
		finally
		{
			Monitor.Exit( useLock );
		}
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

		Apis.Vk.MapMemory( VkContext.LogicalDevice, allocation.Memory, allocation.Offset, dataSize + dataSize * (ulong)index, 0, &dataPtr ).AssertSuccess();
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
		Monitor.Enter( useLock );
		try
		{
			if ( preservedMaps.ContainsKey( allocation ) )
			{
				Apis.Vk.UnmapMemory( VkContext.LogicalDevice, allocation.Memory );
				preservedMaps.Remove( allocation );
			}

			Apis.Vk.FreeMemory( VkContext.LogicalDevice, allocation.Memory, null );
			memoryAllocations.Remove( allocation.Memory );
			memoryTypeAllocationCounts[allocation.MemoryType]--;
			memoryTypeAllocationSizes[allocation.MemoryType] -= allocation.Size;
		}
		finally
		{
			Monitor.Exit( useLock );
		}
	}

	private unsafe void* RetrieveDataPointer( Allocation allocation, ulong dataSize, bool preserveMap )
	{
		void* dataPtr;

		if ( preserveMap && preservedMaps.TryGetValue( allocation, out var mappedDataPtr ) )
			dataPtr = (void*)mappedDataPtr;
		else
			Apis.Vk.MapMemory( VkContext.LogicalDevice, allocation.Memory, allocation.Offset, dataSize, 0, &dataPtr ).AssertSuccess();

		return dataPtr;
	}

	private unsafe void ReturnDataPointer( Allocation allocation, void* dataPtr, bool preserveMap )
	{
		Monitor.Enter( useLock );
		try
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
		finally
		{
			Monitor.Exit( useLock );
		}
	}

	private void Dispose( bool disposing )
	{
		if ( disposed )
			return;

		if ( disposing )
		{
		}

		disposed = true;
	}

	public void Dispose()
	{
		Dispose( disposing: true );
		GC.SuppressFinalize( this );
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
}

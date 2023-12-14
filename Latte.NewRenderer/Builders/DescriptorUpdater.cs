using Silk.NET.Vulkan;
using System;
using System.Runtime.InteropServices;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Latte.NewRenderer.Builders;

internal unsafe sealed class DescriptorUpdater : IDisposable
{
	private readonly Device logicalDevice;

	private WriteDescriptorSet[] writes = [];
	private DescriptorBufferInfo* bufferInfos;
	private DescriptorImageInfo* imageInfos;
	private int currentWrites;
	private bool disposed;

	internal DescriptorUpdater( Device logicalDevice, int initialWrites = 10 )
	{
		this.logicalDevice = logicalDevice;

		ReAllocate( initialWrites );
	}

	~DescriptorUpdater()
	{
		Dispose( disposing: false );
	}

	internal DescriptorUpdater WriteBuffer( uint binding, DescriptorType type, Buffer buffer, ulong offset, ulong size )
	{
		bufferInfos[currentWrites] = new DescriptorBufferInfo
		{
			Buffer = buffer,
			Offset = offset,
			Range = size,
		};

		AddWrite( new WriteDescriptorSet
		{
			SType = StructureType.WriteDescriptorSet,
			PNext = null,
			DstBinding = binding,
			DstSet = default,
			DescriptorCount = 1,
			DescriptorType = type,
			PBufferInfo = bufferInfos + currentWrites
		} );
		return this;
	}

	internal DescriptorUpdater WriteImage( uint binding, DescriptorType type, ImageView imageView, Sampler sampler, ImageLayout layout )
	{
		imageInfos[currentWrites] = new DescriptorImageInfo
		{
			ImageLayout = layout,
			ImageView = imageView,
			Sampler = sampler
		};

		AddWrite( new WriteDescriptorSet
		{
			SType = StructureType.WriteDescriptorSet,
			PNext = null,
			DstBinding = binding,
			DstSet = default,
			DescriptorCount = 1,
			DescriptorType = type,
			PImageInfo = imageInfos + currentWrites
		} );
		return this;
	}

	internal DescriptorUpdater Clear()
	{
		for ( var i = 0; i < currentWrites; i++ )
			writes[i] = default;

		currentWrites = 0;
		return this;
	}

	internal unsafe DescriptorUpdater Update( DescriptorSet descriptorSet )
	{
		for ( var i = 0; i < currentWrites; i++ )
		{
			writes[i] = writes[i] with
			{
				DstSet = descriptorSet
			};
		}

		fixed( WriteDescriptorSet* writesPtr = writes )
			Apis.Vk.UpdateDescriptorSets( logicalDevice, (uint)currentWrites, writesPtr, null );

		return this;
	}

	internal DescriptorUpdater UpdateAndClear( DescriptorSet descriptorSet )
	{
		Update( descriptorSet );
		Clear();
		return this;
	}

	private void AddWrite( WriteDescriptorSet write )
	{
		writes[currentWrites++] = write;
		if ( currentWrites < writes.Length )
			return;

		ReAllocate( (int)(writes.Length * 1.5f) );
	}

	private void ReAllocate( int newSize )
	{
		var newWrites = new WriteDescriptorSet[newSize];
		if ( writes is not null && writes.Length > 0 )
			Array.Copy( writes, newWrites, writes.Length );
		writes = newWrites;

		if ( (nint)bufferInfos == nint.Zero )
		{
			bufferInfos = (DescriptorBufferInfo*)Marshal.AllocHGlobal( sizeof( DescriptorBufferInfo ) * newSize );
			imageInfos = (DescriptorImageInfo*)Marshal.AllocHGlobal( sizeof( DescriptorImageInfo ) * newSize );
		}
		else
		{
			bufferInfos = (DescriptorBufferInfo*)Marshal.ReAllocHGlobal( (nint)bufferInfos, sizeof( DescriptorBufferInfo ) * newSize );
			imageInfos = (DescriptorImageInfo*)Marshal.ReAllocHGlobal( (nint)bufferInfos, sizeof( DescriptorImageInfo ) * newSize );
		}
	}

	private void Dispose( bool disposing )
	{
		if ( disposed )
			return;

		if ( disposing )
		{
		}

		Marshal.FreeHGlobal( (nint)bufferInfos );
		Marshal.FreeHGlobal( (nint)imageInfos );
		disposed = true;
	}

	public void Dispose()
	{
		Dispose( disposing: true );
		GC.SuppressFinalize( this );
	}
}

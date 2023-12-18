using Silk.NET.Vulkan;
using System;
using System.Runtime.InteropServices;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Latte.NewRenderer.Builders;

internal unsafe sealed class VkDescriptorUpdater : IDisposable
{
	private readonly Device logicalDevice;
	private readonly WriteDescriptorSet[] writes = [];
	private readonly DescriptorBufferInfo* bufferInfos;
	private readonly DescriptorImageInfo* imageInfos;

	private int currentWrites;
	private bool disposed;

	internal VkDescriptorUpdater( Device logicalDevice, int maxWrites = 10 )
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( maxWrites, nameof( maxWrites ) );

		this.logicalDevice = logicalDevice;

		writes = new WriteDescriptorSet[maxWrites];
		bufferInfos = (DescriptorBufferInfo*)Marshal.AllocHGlobal( sizeof( DescriptorBufferInfo ) * maxWrites );
		imageInfos = (DescriptorImageInfo*)Marshal.AllocHGlobal( sizeof( DescriptorImageInfo ) * maxWrites );
	}

	~VkDescriptorUpdater()
	{
		Dispose( disposing: false );
	}

	internal VkDescriptorUpdater WriteBuffer( uint binding, DescriptorType type, Buffer buffer, ulong offset, ulong size )
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

	internal VkDescriptorUpdater WriteImage( uint binding, DescriptorType type, ImageView imageView, Sampler sampler, ImageLayout layout )
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

	internal VkDescriptorUpdater Clear()
	{
		for ( var i = 0; i < currentWrites; i++ )
			writes[i] = default;

		currentWrites = 0;
		return this;
	}

	internal unsafe VkDescriptorUpdater Update( DescriptorSet descriptorSet )
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

	internal VkDescriptorUpdater UpdateAndClear( DescriptorSet descriptorSet )
	{
		Update( descriptorSet );
		Clear();
		return this;
	}

	private void AddWrite( WriteDescriptorSet write )
	{
		writes[currentWrites++] = write;
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

using Latte.NewRenderer.Vulkan.Extensions;
using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Latte.NewRenderer.Vulkan.Allocations;

internal sealed class DescriptorAllocator : IDisposable
{
	private const int MaxSetsPerPool = 2048;

	private readonly Device logicalDevice;
	private readonly ImmutableArray<PoolSizeRatio> poolSizeRatios;
	private readonly List<DescriptorPool> fullPools = [];
	private readonly List<DescriptorPool> readyPools = [];

	private uint currentSetsPerPool;
	private bool disposed;

	internal DescriptorAllocator( Device logicalDevice, uint initialSets, ImmutableArray<PoolSizeRatio> poolSizeRatios )
	{
		this.logicalDevice = logicalDevice;
		currentSetsPerPool = initialSets;
		this.poolSizeRatios = poolSizeRatios;

		readyPools.Add( CreatePool( currentSetsPerPool ) );
		currentSetsPerPool = (uint)(currentSetsPerPool * 1.5f);
	}

	~DescriptorAllocator()
	{
		Dispose( disposing: false );
	}

	internal void ClearPools()
	{
		foreach ( var pool in readyPools )
			Apis.Vk.ResetDescriptorPool( logicalDevice, pool, 0 );

		foreach ( var pool in fullPools )
		{
			Apis.Vk.ResetDescriptorPool( logicalDevice, pool, 0 );
			readyPools.Add( pool );
		}

		fullPools.Clear();
	}

	internal DescriptorSet Allocate( ReadOnlySpan<DescriptorSetLayout> layouts )
	{
		var pool = GetPool();

		var allocateInfo = VkInfo.AllocateDescriptorSet( pool, layouts );
		var result = Apis.Vk.AllocateDescriptorSets( logicalDevice, allocateInfo, out var descriptorSet );

		if ( result == Result.ErrorOutOfPoolMemory || result == Result.ErrorFragmentedPool )
		{
			fullPools.Add( pool );
			pool = GetPool();
			allocateInfo.DescriptorPool = pool;

			Apis.Vk.AllocateDescriptorSets( logicalDevice, allocateInfo, out descriptorSet ).Verify();
		}

		readyPools.Add( pool );
		return descriptorSet;
	}

	private DescriptorPool GetPool()
	{
		DescriptorPool pool;

		if ( readyPools.Count > 0 )
		{
			pool = readyPools[^1];
			readyPools.Remove( pool );
		}
		else
		{
			pool = CreatePool( currentSetsPerPool );
			currentSetsPerPool = (uint)(currentSetsPerPool * 1.5f);
			if ( currentSetsPerPool > MaxSetsPerPool )
				currentSetsPerPool = MaxSetsPerPool;
		}

		return pool;
	}

	private unsafe DescriptorPool CreatePool( uint setCount )
	{
		Span<DescriptorPoolSize> poolSizes = stackalloc DescriptorPoolSize[poolSizeRatios.Length];
		for ( var i = 0; i < poolSizeRatios.Length; i++ )
		{
			poolSizes[i] = new DescriptorPoolSize
			{
				Type = poolSizeRatios[i].Type,
				DescriptorCount = (uint)(poolSizeRatios[i].Ratio * setCount)
			};
		}

		var descriptorPoolInfo = VkInfo.DescriptorPool( setCount, poolSizes );
		Apis.Vk.CreateDescriptorPool( logicalDevice, descriptorPoolInfo, null, out var descriptorPool );
		return descriptorPool;
	}

	private unsafe void Dispose( bool disposing )
	{
		if ( disposed )
			return;

		if ( disposing )
		{
		}

		foreach ( var pool in readyPools )
			Apis.Vk.DestroyDescriptorPool( logicalDevice, pool, null );

		foreach ( var pool in fullPools )
			Apis.Vk.DestroyDescriptorPool( logicalDevice, pool, null );

		disposed = true;
	}

	public void Dispose()
	{
		Dispose( disposing: true );
		GC.SuppressFinalize( this );
	}
}

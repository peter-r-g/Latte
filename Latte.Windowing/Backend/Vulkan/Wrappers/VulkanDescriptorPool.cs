using Latte.Windowing.Extensions;
using Silk.NET.Vulkan;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanDescriptorPool : VulkanWrapper
{
	internal required DescriptorPool DescriptorPool { get; init; }

	[SetsRequiredMembers]
	internal VulkanDescriptorPool( in DescriptorPool descriptorPool, LogicalGpu owner ) : base( owner )
	{
		DescriptorPool = descriptorPool;
	}

	public unsafe override void Dispose()
	{
		if ( Disposed )
			return;

		Apis.Vk.DestroyDescriptorPool( LogicalGpu!, DescriptorPool, null );

		GC.SuppressFinalize( this );
		Disposed = true;
	}

	public static implicit operator DescriptorPool( VulkanDescriptorPool vulkanDescriptorPool )
	{
		if ( vulkanDescriptorPool.Disposed )
			throw new ObjectDisposedException( nameof( VulkanDescriptorPool ) );

		return vulkanDescriptorPool.DescriptorPool;
	}

	internal static unsafe VulkanDescriptorPool New( LogicalGpu logicalGpu, in ReadOnlySpan<DescriptorPoolSize> descriptorPoolSizes,
		uint maxDescriptorSets )
	{
		fixed ( DescriptorPoolSize* descriptorPoolSizesPtr = descriptorPoolSizes )
		{
			var poolInfo = new DescriptorPoolCreateInfo
			{
				SType = StructureType.DescriptorPoolCreateInfo,
				PoolSizeCount = 2,
				PPoolSizes = descriptorPoolSizesPtr,
				MaxSets = maxDescriptorSets
			};

			Apis.Vk.CreateDescriptorPool( logicalGpu, poolInfo, null, out var descriptorPool ).Verify();

			return new VulkanDescriptorPool( descriptorPool, logicalGpu );
		}
	}
}

using Silk.NET.Vulkan;
using System;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanDescriptorPool : VulkanWrapper
{
	internal DescriptorPool DescriptorPool { get; }

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
}

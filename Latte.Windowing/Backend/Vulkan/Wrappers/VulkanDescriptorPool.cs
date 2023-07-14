using Silk.NET.Vulkan;
using System;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanDescriptorPool : IDisposable
{
	internal LogicalGpu Owner { get; }

	internal DescriptorPool DescriptorPool { get; }

	private bool disposed;

	internal VulkanDescriptorPool( in DescriptorPool descriptorPool, LogicalGpu owner )
	{
		DescriptorPool = descriptorPool;
		Owner = owner;
	}

	~VulkanDescriptorPool()
	{
		Dispose();
	}

	public unsafe void Dispose()
	{
		if ( disposed )
			return;

		Apis.Vk.DestroyDescriptorPool( Owner, DescriptorPool, null );

		GC.SuppressFinalize( this );
		disposed = true;
	}

	public static implicit operator DescriptorPool( VulkanDescriptorPool vulkanDescriptorPool ) => vulkanDescriptorPool.DescriptorPool;
}

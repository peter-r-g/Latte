using Silk.NET.Vulkan;
using System;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanCommandPool : VulkanWrapper
{
	internal CommandPool CommandPool { get; }

	internal VulkanCommandPool( in CommandPool commandPool, LogicalGpu owner ) : base( owner )
	{
		CommandPool = commandPool;
	}

	public override unsafe void Dispose()
	{
		if ( Disposed )
			return;

		Apis.Vk.DestroyCommandPool( LogicalGpu!, CommandPool, null );

		GC.SuppressFinalize( this );
		Disposed = true;
	}

	public static implicit operator CommandPool( VulkanCommandPool vulkanCommandPool )
	{
		if ( vulkanCommandPool.Disposed )
			throw new ObjectDisposedException( nameof( VulkanCommandPool ) );

		return vulkanCommandPool.CommandPool;
	}
}

using Silk.NET.Vulkan;
using System;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanSemaphore : VulkanWrapper
{
	internal Semaphore Semaphore { get; }

	internal VulkanSemaphore( in Semaphore semaphore, LogicalGpu owner ) : base( owner )
	{
		Semaphore = semaphore;
	}

	public override unsafe void Dispose()
	{
		if ( Disposed )
			return;

		Apis.Vk.DestroySemaphore( LogicalGpu!, Semaphore, null );

		GC.SuppressFinalize( this );
		Disposed = true;
	}

	public static implicit operator Semaphore( VulkanSemaphore vulkanSemaphore )
	{
		if ( vulkanSemaphore.Disposed )
			throw new ObjectDisposedException( nameof( VulkanSemaphore ) );

		return vulkanSemaphore.Semaphore;
	}
}

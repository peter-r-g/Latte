using Silk.NET.Vulkan;
using System;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanFence : VulkanWrapper
{
	internal Fence Fence { get; }

	internal VulkanFence( in Fence fence, LogicalGpu owner ) : base( owner )
	{
		Fence = fence;
	}

	public override unsafe void Dispose()
	{
		if ( Disposed )
			return;

		Apis.Vk.DestroyFence( LogicalGpu!, Fence, null );

		GC.SuppressFinalize( this );
		Disposed = true;
	}

	public static implicit operator Fence( VulkanFence vulkanFence )
	{
		if ( vulkanFence.Disposed )
			throw new ObjectDisposedException( nameof( VulkanFence ) );

		return vulkanFence.Fence;
	}
}

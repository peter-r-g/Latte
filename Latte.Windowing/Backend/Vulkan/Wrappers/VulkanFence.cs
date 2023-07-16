using Latte.Windowing.Extensions;
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

	internal static unsafe VulkanFence New( LogicalGpu logicalGpu, bool signaled )
	{
		var fenceInfo = new FenceCreateInfo
		{
			SType = StructureType.FenceCreateInfo,
			Flags = signaled ? FenceCreateFlags.SignaledBit : 0
		};

		Apis.Vk.CreateFence( logicalGpu, fenceInfo, null, out var fence ).Verify();

		return new VulkanFence( fence, logicalGpu );
	}
}

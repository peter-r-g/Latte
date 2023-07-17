using Latte.Windowing.Extensions;
using Silk.NET.Vulkan;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanCommandPool : VulkanWrapper
{
	internal required CommandPool CommandPool { get; init; }

	[SetsRequiredMembers]
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

	internal static unsafe VulkanCommandPool New( LogicalGpu logicalGpu, uint queueFamilyIndex )
	{
		var poolInfo = new CommandPoolCreateInfo
		{
			SType = StructureType.CommandPoolCreateInfo,
			Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
			QueueFamilyIndex = queueFamilyIndex
		};

		Apis.Vk.CreateCommandPool( logicalGpu, poolInfo, null, out var commandPool ).Verify();

		return new VulkanCommandPool( commandPool, logicalGpu );
	}
}

using Latte.Windowing.Extensions;
using Silk.NET.Vulkan;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanSemaphore : VulkanWrapper
{
	internal required Semaphore Semaphore { get; init; }

	[SetsRequiredMembers]
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

	internal static unsafe VulkanSemaphore New( LogicalGpu logicalGpu )
	{
		var semaphoreCreateInfo = new SemaphoreCreateInfo
		{
			SType = StructureType.SemaphoreCreateInfo
		};

		Apis.Vk.CreateSemaphore( logicalGpu, semaphoreCreateInfo, null, out var semaphore ).Verify();

		return new VulkanSemaphore( semaphore, logicalGpu );
	}
}

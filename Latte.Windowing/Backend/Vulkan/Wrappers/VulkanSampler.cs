using Silk.NET.Vulkan;
using System;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanSampler : VulkanWrapper
{
	internal Sampler Sampler { get; }

	internal VulkanSampler( in Sampler sampler, LogicalGpu owner ) : base( owner )
	{
		Sampler = sampler;
	}

	public override unsafe void Dispose()
	{
		if ( Disposed )
			return;

		Apis.Vk.DestroySampler( LogicalGpu!, Sampler, null );

		GC.SuppressFinalize( this );
		Disposed = true;
	}

	public static implicit operator Sampler( VulkanSampler vulkanSampler )
	{
		if ( vulkanSampler.Disposed )
			throw new ObjectDisposedException( nameof( VulkanSampler ) );

		return vulkanSampler.Sampler;
	}
}

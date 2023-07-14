using Silk.NET.Vulkan;
using System;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanRenderPass : VulkanWrapper
{
	internal RenderPass RenderPass { get; }

	internal VulkanRenderPass( in RenderPass renderPass, LogicalGpu owner ) : base( owner )
	{
		RenderPass = renderPass;
	}

	public unsafe override void Dispose()
	{
		if ( Disposed )
			return;

		Apis.Vk.DestroyRenderPass( LogicalGpu!, RenderPass, null );

		GC.SuppressFinalize( this );
		Disposed = true;
	}

	public static implicit operator RenderPass( VulkanRenderPass vulkanRenderPass )
	{
		if ( vulkanRenderPass.Disposed )
			throw new ObjectDisposedException( nameof( VulkanRenderPass ) );

		return vulkanRenderPass.RenderPass;
	}
}

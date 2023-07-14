using Silk.NET.Vulkan;
using System;

namespace Latte.Windowing.Backend.Vulkan;

internal class VulkanRenderPass : IDisposable
{
	internal LogicalGpu Owner { get; }

	internal RenderPass RenderPass { get; }

	private bool disposed;

	internal VulkanRenderPass( in RenderPass renderPass, LogicalGpu owner )
	{
		RenderPass = renderPass;
		Owner = owner;
	}

	~VulkanRenderPass()
	{
		Dispose();
	}

	public unsafe void Dispose()
	{
		if ( disposed )
			return;

		Apis.Vk.DestroyRenderPass( Owner, RenderPass, null );

		GC.SuppressFinalize( this );
		disposed = true;
	}

	public static implicit operator RenderPass( VulkanRenderPass vulkanRenderPass ) => vulkanRenderPass.RenderPass;
}

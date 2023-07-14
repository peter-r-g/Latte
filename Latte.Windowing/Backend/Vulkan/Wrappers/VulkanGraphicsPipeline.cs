using Silk.NET.Vulkan;
using System;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanGraphicsPipeline : VulkanWrapper
{
	internal Pipeline Pipeline { get; }
	internal PipelineLayout Layout { get; }

	internal VulkanGraphicsPipeline( in Pipeline pipeline, in PipelineLayout layout, LogicalGpu owner ) : base( owner )
	{
		Pipeline = pipeline;
		Layout = layout;
	}

	public unsafe override void Dispose()
	{
		if ( Disposed )
			return;

		Apis.Vk.DestroyPipeline( LogicalGpu!, Pipeline, null );
		Apis.Vk.DestroyPipelineLayout( LogicalGpu!, Layout, null );

		GC.SuppressFinalize( this );
		Disposed = true;
	}

	public static implicit operator Pipeline( VulkanGraphicsPipeline graphicsPipeline )
	{
		if ( graphicsPipeline.Disposed )
			throw new ObjectDisposedException( nameof( VulkanGraphicsPipeline ) );

		return graphicsPipeline.Pipeline;
	}
}

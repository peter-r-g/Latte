using Silk.NET.Vulkan;
using System;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class GraphicsPipeline : IDisposable
{
	internal LogicalGpu Owner { get; }

	internal Pipeline Pipeline { get; }
	internal PipelineLayout Layout { get; }

	private bool disposed;

	internal GraphicsPipeline( in Pipeline pipeline, in PipelineLayout layout, LogicalGpu owner )
	{
		Pipeline = pipeline;
		Layout = layout;
		Owner = owner;
	}

	~GraphicsPipeline()
	{
		Dispose();
	}

	public unsafe void Dispose()
	{
		if ( disposed )
			return;

		disposed = true;
		Apis.Vk.DestroyPipeline( Owner, Pipeline, null );
		Apis.Vk.DestroyPipelineLayout( Owner, Layout, null );

		GC.SuppressFinalize( this );
	}

	public static implicit operator Pipeline( GraphicsPipeline graphicsPipeline ) => graphicsPipeline.Pipeline;
}

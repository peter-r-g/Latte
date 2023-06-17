using Silk.NET.Vulkan;
using System;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class GraphicsPipeline : IDisposable
{
	internal LogicalGpu Owner { get; }

	internal Pipeline Pipeline { get; }
	internal PipelineLayout Layout { get; }

	internal GraphicsPipeline( in Pipeline pipeline, in PipelineLayout layout, LogicalGpu owner )
	{
		Pipeline = pipeline;
		Layout = layout;
		Owner = owner;
	}

	public unsafe void Dispose()
	{
		Apis.Vk.DestroyPipeline( Owner, Pipeline, null );
		Apis.Vk.DestroyPipelineLayout( Owner, Layout, null );
	}

	public static implicit operator Pipeline( GraphicsPipeline graphicsPipeline ) => graphicsPipeline.Pipeline;
}

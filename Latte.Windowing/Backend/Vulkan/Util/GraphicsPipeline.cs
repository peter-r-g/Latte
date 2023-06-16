using Silk.NET.Vulkan;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class GraphicsPipeline
{
	internal Pipeline Pipeline { get; }
	internal PipelineLayout Layout { get; }

	internal GraphicsPipeline( in Pipeline pipeline, in PipelineLayout layout )
	{
		Pipeline = pipeline;
		Layout = layout;
	}

	public static implicit operator Pipeline( GraphicsPipeline graphicsPipeline ) => graphicsPipeline.Pipeline;
}

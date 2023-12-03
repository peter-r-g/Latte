using Silk.NET.Vulkan;

namespace Latte.NewRenderer.Temp;

internal sealed class Material
{
	internal Pipeline Pipeline { get; }
	internal PipelineLayout PipelineLayout { get; }

	internal Material( Pipeline pipeline, PipelineLayout pipelineLayout )
	{
		Pipeline = pipeline;
		PipelineLayout = pipelineLayout;
	}
}

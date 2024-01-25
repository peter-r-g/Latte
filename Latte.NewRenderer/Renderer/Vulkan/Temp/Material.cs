using Silk.NET.Vulkan;

namespace Latte.NewRenderer.Renderer.Vulkan.Temp;

internal sealed class Material
{
	internal Pipeline Pipeline { get; }
	internal PipelineLayout PipelineLayout { get; }
	internal DescriptorSet TextureSet { get; set; }

	internal Material( Pipeline pipeline, PipelineLayout pipelineLayout )
	{
		Pipeline = pipeline;
		PipelineLayout = pipelineLayout;
	}

	internal Material Clone()
	{
		return new Material( Pipeline, PipelineLayout );
	}
}

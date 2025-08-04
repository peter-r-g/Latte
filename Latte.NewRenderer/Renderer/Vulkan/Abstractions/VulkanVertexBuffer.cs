using Latte.Windowing.Renderer.Abstractions;
using Latte.Windowing.Renderer.Vulkan.Allocations;

namespace Latte.Windowing.Renderer.Vulkan.Abstractions;

internal sealed class VulkanVertexBuffer : VertexBuffer
{
	internal override uint Count => throw new System.NotImplementedException();

	private AllocatedBuffer vertexBuffer;

	internal VulkanVertexBuffer()
	{

	}

	internal override void Bind()
	{
		//Apis.Vk.CmdBindVertexBuffers( cmd, 0, vertexBuffer.Buffer, 0, null );
	}

	internal override void Unbind()
	{
		// Don't do anything for Vulkan.
	}
}

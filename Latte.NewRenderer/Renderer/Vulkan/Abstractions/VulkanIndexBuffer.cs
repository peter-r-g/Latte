using Latte.Windowing.Renderer.Abstractions;
using Latte.Windowing.Renderer.Vulkan.Allocations;
using Silk.NET.Vulkan;

namespace Latte.Windowing.Renderer.Vulkan.Abstractions;

internal sealed class VulkanIndexBuffer : IndexBuffer
{
	internal override required IndexBufferFormat Format { get; init; }
	internal override uint Count => throw new System.NotImplementedException();

	private AllocatedBuffer indexBuffer;

	internal VulkanIndexBuffer()
	{

	}

	internal override void Bind()
	{
		//Apis.Vk.CmdBindIndexBuffer( cmd, indexBuffer.Buffer, 0, Format == IndexBufferFormat.UInt16 ? IndexType.Uint16 : IndexType.Uint32 );
	}

	internal override void Unbind()
	{
		// Don't do anything for Vulkan.
	}
}

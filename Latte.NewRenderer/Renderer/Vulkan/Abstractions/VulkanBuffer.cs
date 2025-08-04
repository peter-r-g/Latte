using Latte.Windowing.Renderer.Abstractions;

namespace Latte.Windowing.Renderer.Vulkan.Abstractions;

internal sealed class VulkanBuffer : Buffer
{
	internal required override BufferLayout Layout { get; init; }
	internal required override BufferUsage Usage { get; init; }

	internal override unsafe void SetData( void* dataPtr, ulong size )
	{
		throw new System.NotImplementedException();
	}
}

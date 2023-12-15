using Latte.NewRenderer.Allocations;
using Silk.NET.Vulkan;

namespace Latte.NewRenderer.Temp;

internal sealed class FrameData
{
	internal Semaphore PresentSemaphore;
	internal Semaphore RenderSemaphore;
	internal Fence RenderFence;

	internal CommandPool CommandPool;
	internal CommandBuffer CommandBuffer;

	internal AllocatedBuffer CameraBuffer;
	internal AllocatedBuffer ObjectBuffer;
	internal DescriptorSet FrameDescriptor;
}

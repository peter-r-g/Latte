using Latte.Windowing.Renderer.Vulkan.Allocations;
using Silk.NET.Vulkan;

namespace Latte.Windowing.Renderer.Vulkan.Temp;

internal sealed class FrameData
{
	internal Semaphore PresentSemaphore;
	internal Semaphore RenderSemaphore;
	internal Fence RenderFence;

	internal CommandPool CommandPool;
	internal CommandBuffer CommandBuffer;

	internal AllocatedBuffer CameraBuffer;
	internal AllocatedBuffer ObjectBuffer;
	internal AllocatedBuffer LightBuffer;
	internal DescriptorSet FrameDescriptor;

	internal Camera? Camera;
	internal uint SwapchainImageIndex;
}

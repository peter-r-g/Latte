using Latte.Windowing.Renderer.Abstractions;
using Latte.Windowing.Renderer.Vulkan.Extensions;
using Silk.NET.Vulkan;

namespace Latte.Windowing.Renderer.Vulkan.Abstractions;

internal sealed class VulkanRenderCommandBuffer : RenderCommandBuffer
{
	private CommandPool commandPool;
	private CommandBuffer commandBuffer;

	internal override void Begin()
	{
		Apis.Vk.ResetCommandBuffer( commandBuffer, CommandBufferResetFlags.None ).AssertSuccess();

		var beginInfo = VkInfo.BeginCommandBuffer( CommandBufferUsageFlags.OneTimeSubmitBit );
		Apis.Vk.BeginCommandBuffer( commandBuffer, beginInfo ).AssertSuccess();
	}

	internal override void End()
	{
		Apis.Vk.EndCommandBuffer( commandBuffer ).AssertSuccess();
	}

	internal override void Submit()
	{
		/*var presentInfo = new PresentInfoKHR
		{
			SType = StructureType.PresentInfoKhr,
			PNext = null,
			SwapchainCount = 1,
			PSwapchains = &swapchain,
			WaitSemaphoreCount = 1,
			PWaitSemaphores = &renderSemaphore,
			PImageIndices = &swapchainImageIndex
		};*/

		// FIXME: How the fuck does this work.
		//VkContext.PresentQueue.SubmitPresentAndWait( presentInfo );
	}
}

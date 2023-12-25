using Silk.NET.Vulkan;

namespace Latte.NewRenderer.Extensions;

internal static class VkTypeExtensions
{
	internal static bool IsValid( this Instance instance ) => instance.Handle != 0;
	internal static bool IsValid( this DebugUtilsMessengerEXT debugMessenger ) => debugMessenger.Handle != 0;
	internal static bool IsValid( this PhysicalDevice physicalDevice ) => physicalDevice.Handle != 0;
	internal static bool IsValid( this Device logicalDevice ) => logicalDevice.Handle != 0;
	internal static bool IsValid( this SurfaceKHR surface ) => surface.Handle != 0;
	internal static bool IsValid( this Queue queue ) => queue.Handle != 0;
	internal static bool IsValid( this CommandPool commandPool ) => commandPool.Handle != 0;
	internal static bool IsValid( this CommandBuffer commandBuffer ) => commandBuffer.Handle != 0;
	internal static bool IsValid( this RenderPass renderPass ) => renderPass.Handle != 0;
	internal static bool IsValid( this Framebuffer framebuffer ) => framebuffer.Handle != 0;
	internal static bool IsValid( this Fence fence ) => fence.Handle != 0;
	internal static bool IsValid( this Semaphore semaphore ) => semaphore.Handle != 0;
	internal static bool IsValid( this PipelineLayout pipelineLayout ) => pipelineLayout.Handle != 0;
	internal static bool IsValid( this Pipeline pipeline ) => pipeline.Handle != 0;
	internal static bool IsValid( this Buffer buffer ) => buffer.Handle != 0;
	internal static bool IsValid( this DeviceMemory deviceMemory ) => deviceMemory.Handle != 0;
	internal static bool IsValid( this Image image ) => image.Handle != 0;
	internal static bool IsValid( this ImageView imageView ) => imageView.Handle != 0;
	internal static bool IsValid( this DescriptorPool descriptorPool ) => descriptorPool.Handle != 0;
	internal static bool IsValid( this DescriptorSetLayout descriptorSetLayout ) => descriptorSetLayout.Handle != 0;
	internal static bool IsValid( this DescriptorSet descriptorSet ) => descriptorSet.Handle != 0;
	internal static bool IsValid( this Sampler sampler ) => sampler.Handle != 0;
	internal static bool IsValid( this QueryPool queryPool ) => queryPool.Handle != 0;
}

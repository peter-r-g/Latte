using Latte.NewRenderer.Exceptions;
using Silk.NET.Vulkan;

namespace Latte.NewRenderer.Extensions;

internal static class VkTypeExtensions
{
	internal static bool IsValid( this Instance instance ) => instance.Handle != 0;
	internal static Instance Validate( this Instance instance )
	{
		if ( !IsValid( instance ) )
			throw new VkInvalidInstanceException( typeof( Instance ) );

		return instance;
	}

	internal static bool IsValid( this DebugUtilsMessengerEXT debugMessenger ) => debugMessenger.Handle != 0;
	internal static DebugUtilsMessengerEXT Validate( this DebugUtilsMessengerEXT debugMessenger )
	{
		if ( !IsValid( debugMessenger ) )
			throw new VkInvalidInstanceException( typeof( DebugUtilsMessengerEXT ) );

		return debugMessenger;
	}

	internal static bool IsValid( this PhysicalDevice physicalDevice ) => physicalDevice.Handle != 0;
	internal static PhysicalDevice Validate( this PhysicalDevice physicalDevice )
	{
		if ( !IsValid( physicalDevice ) )
			throw new VkInvalidInstanceException( typeof( PhysicalDevice ) );

		return physicalDevice;
	}

	internal static bool IsValid( this Device logicalDevice ) => logicalDevice.Handle != 0;
	internal static Device Validate( this Device logicalDevice )
	{
		if ( !IsValid( logicalDevice ) )
			throw new VkInvalidInstanceException( typeof( Device ) );

		return logicalDevice;
	}

	internal static bool IsValid( this SurfaceKHR surface ) => surface.Handle != 0;
	internal static SurfaceKHR Validate( this SurfaceKHR surface )
	{
		if ( !IsValid( surface ) )
			throw new VkInvalidInstanceException( typeof( SurfaceKHR ) );

		return surface;
	}

	internal static bool IsValid( this Queue queue ) => queue.Handle != 0;
	internal static Queue Validate( this Queue queue )
	{
		if ( !IsValid( queue ) )
			throw new VkInvalidInstanceException( typeof( Queue ) );

		return queue;
	}

	internal static bool IsValid( this CommandPool commandPool ) => commandPool.Handle != 0;
	internal static CommandPool Validate( this CommandPool commandPool )
	{
		if ( !IsValid( commandPool ) )
			throw new VkInvalidInstanceException( typeof( CommandPool ) );

		return commandPool;
	}

	internal static bool IsValid( this CommandBuffer commandBuffer ) => commandBuffer.Handle != 0;
	internal static CommandBuffer Validate( this CommandBuffer commandBuffer )
	{
		if ( !IsValid( commandBuffer ) )
			throw new VkInvalidInstanceException( typeof( CommandBuffer ) );

		return commandBuffer;
	}

	internal static bool IsValid( this RenderPass renderPass ) => renderPass.Handle != 0;
	internal static RenderPass Validate( this RenderPass renderPass )
	{
		if ( !IsValid( renderPass ) )
			throw new VkInvalidInstanceException( typeof( RenderPass ) );

		return renderPass;
	}

	internal static bool IsValid( this Framebuffer framebuffer ) => framebuffer.Handle != 0;
	internal static Framebuffer Validate( this Framebuffer framebuffer )
	{
		if ( !IsValid( framebuffer ) )
			throw new VkInvalidInstanceException( typeof( Framebuffer ) );

		return framebuffer;
	}

	internal static bool IsValid( this Fence fence ) => fence.Handle != 0;
	internal static Fence Validate( this Fence fence )
	{
		if ( !IsValid( fence ) )
			throw new VkInvalidInstanceException( typeof( Fence ) );

		return fence;
	}

	internal static bool IsValid( this Semaphore semaphore ) => semaphore.Handle != 0;
	internal static Semaphore Validate( this Semaphore semaphore )
	{
		if ( !IsValid( semaphore ) )
			throw new VkInvalidInstanceException( typeof( Semaphore ) );

		return semaphore;
	}

	internal static bool IsValid( this PipelineLayout pipelineLayout ) => pipelineLayout.Handle != 0;
	internal static PipelineLayout Validate( this PipelineLayout pipelineLayout )
	{
		if ( !IsValid( pipelineLayout ) )
			throw new VkInvalidInstanceException( typeof( PipelineLayout ) );

		return pipelineLayout;
	}

	internal static bool IsValid( this Pipeline pipeline ) => pipeline.Handle != 0;
	internal static Pipeline Validate( this Pipeline pipeline )
	{
		if ( !IsValid( pipeline ) )
			throw new VkInvalidInstanceException( typeof( Pipeline ) );

		return pipeline;
	}
}

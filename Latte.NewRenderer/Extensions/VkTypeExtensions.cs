using Latte.NewRenderer.Exceptions;
using Silk.NET.Vulkan;

namespace Latte.NewRenderer.Extensions;

internal static class VkTypeExtensions
{
	internal static bool IsValid( this Instance instance ) => instance.Handle != 0;
	internal static Instance Validate( this Instance instance )
	{
		if ( !IsValid( instance ) )
			throw new VkInvalidHandleException( typeof( Instance ) );

		return instance;
	}

	internal static bool IsValid( this DebugUtilsMessengerEXT debugMessenger ) => debugMessenger.Handle != 0;
	internal static DebugUtilsMessengerEXT Validate( this DebugUtilsMessengerEXT debugMessenger )
	{
		if ( !IsValid( debugMessenger ) )
			throw new VkInvalidHandleException( typeof( DebugUtilsMessengerEXT ) );

		return debugMessenger;
	}

	internal static bool IsValid( this PhysicalDevice physicalDevice ) => physicalDevice.Handle != 0;
	internal static PhysicalDevice Validate( this PhysicalDevice physicalDevice )
	{
		if ( !IsValid( physicalDevice ) )
			throw new VkInvalidHandleException( typeof( PhysicalDevice ) );

		return physicalDevice;
	}

	internal static bool IsValid( this Device logicalDevice ) => logicalDevice.Handle != 0;
	internal static Device Validate( this Device logicalDevice )
	{
		if ( !IsValid( logicalDevice ) )
			throw new VkInvalidHandleException( typeof( Device ) );

		return logicalDevice;
	}

	internal static bool IsValid( this SurfaceKHR surface ) => surface.Handle != 0;
	internal static SurfaceKHR Validate( this SurfaceKHR surface )
	{
		if ( !IsValid( surface ) )
			throw new VkInvalidHandleException( typeof( SurfaceKHR ) );

		return surface;
	}

	internal static bool IsValid( this Queue queue ) => queue.Handle != 0;
	internal static Queue Validate( this Queue queue )
	{
		if ( !IsValid( queue ) )
			throw new VkInvalidHandleException( typeof( Queue ) );

		return queue;
	}

	internal static bool IsValid( this CommandPool commandPool ) => commandPool.Handle != 0;
	internal static CommandPool Validate( this CommandPool commandPool )
	{
		if ( !IsValid( commandPool ) )
			throw new VkInvalidHandleException( typeof( CommandPool ) );

		return commandPool;
	}

	internal static bool IsValid( this CommandBuffer commandBuffer ) => commandBuffer.Handle != 0;
	internal static CommandBuffer Validate( this CommandBuffer commandBuffer )
	{
		if ( !IsValid( commandBuffer ) )
			throw new VkInvalidHandleException( typeof( CommandBuffer ) );

		return commandBuffer;
	}

	internal static bool IsValid( this RenderPass renderPass ) => renderPass.Handle != 0;
	internal static RenderPass Validate( this RenderPass renderPass )
	{
		if ( !IsValid( renderPass ) )
			throw new VkInvalidHandleException( typeof( RenderPass ) );

		return renderPass;
	}

	internal static bool IsValid( this Framebuffer framebuffer ) => framebuffer.Handle != 0;
	internal static Framebuffer Validate( this Framebuffer framebuffer )
	{
		if ( !IsValid( framebuffer ) )
			throw new VkInvalidHandleException( typeof( Framebuffer ) );

		return framebuffer;
	}

	internal static bool IsValid( this Fence fence ) => fence.Handle != 0;
	internal static Fence Validate( this Fence fence )
	{
		if ( !IsValid( fence ) )
			throw new VkInvalidHandleException( typeof( Fence ) );

		return fence;
	}

	internal static bool IsValid( this Semaphore semaphore ) => semaphore.Handle != 0;
	internal static Semaphore Validate( this Semaphore semaphore )
	{
		if ( !IsValid( semaphore ) )
			throw new VkInvalidHandleException( typeof( Semaphore ) );

		return semaphore;
	}

	internal static bool IsValid( this PipelineLayout pipelineLayout ) => pipelineLayout.Handle != 0;
	internal static PipelineLayout Validate( this PipelineLayout pipelineLayout )
	{
		if ( !IsValid( pipelineLayout ) )
			throw new VkInvalidHandleException( typeof( PipelineLayout ) );

		return pipelineLayout;
	}

	internal static bool IsValid( this Pipeline pipeline ) => pipeline.Handle != 0;
	internal static Pipeline Validate( this Pipeline pipeline )
	{
		if ( !IsValid( pipeline ) )
			throw new VkInvalidHandleException( typeof( Pipeline ) );

		return pipeline;
	}

	internal static bool IsValid( this Buffer buffer ) => buffer.Handle != 0;
	internal static Buffer Validate( this Buffer buffer )
	{
		if ( !IsValid( buffer ) )
			throw new VkInvalidHandleException( typeof( Buffer ) );

		return buffer;
	}

	internal static bool IsValid( this DeviceMemory deviceMemory ) => deviceMemory.Handle != 0;
	internal static DeviceMemory Validate( this DeviceMemory deviceMemory )
	{
		if ( !IsValid( deviceMemory ) )
			throw new VkInvalidHandleException( typeof( DeviceMemory ) );

		return deviceMemory;
	}

	internal static bool IsValid( this Image image ) => image.Handle != 0;
	internal static Image Validate( this Image image )
	{
		if ( !IsValid( image ) )
			throw new VkInvalidHandleException( typeof( Image ) );

		return image;
	}

	internal static bool IsValid( this ImageView imageView ) => imageView.Handle != 0;
	internal static ImageView Validate( this ImageView imageView )
	{
		if ( !IsValid( imageView ) )
			throw new VkInvalidHandleException( typeof( ImageView ) );

		return imageView;
	}
}

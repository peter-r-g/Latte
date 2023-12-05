using Latte.NewRenderer.Extensions;
using Silk.NET.Vulkan;
using System;
using System.Diagnostics.CodeAnalysis;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Latte.NewRenderer.Exceptions;

internal sealed class VkInvalidHandleException : VkException
{
	internal VkInvalidHandleException( Type type ) : base( $"An instance of {type} is invalid" )
	{
	}

	[DoesNotReturn]
	private static void Throw( Type type )
	{
		throw new VkInvalidHandleException( type );
	}

	internal static void ThrowIfInvalid( Instance instance )
	{
		if ( !instance.IsValid() )
			Throw( typeof( Instance ) );
	}

	internal static void ThrowIfInvalid( DebugUtilsMessengerEXT debugMessenger )
	{
		if ( !debugMessenger.IsValid() )
			Throw( typeof( DebugUtilsMessengerEXT ) );
	}

	internal static void ThrowIfInvalid( PhysicalDevice physicalDevice )
	{
		if ( !physicalDevice.IsValid() )
			Throw( typeof( PhysicalDevice ) );
	}

	internal static void ThrowIfInvalid( Device logicalDevice )
	{
		if ( !logicalDevice.IsValid() )
			Throw( typeof( Device ) );
	}

	internal static void ThrowIfInvalid( SurfaceKHR surface )
	{
		if ( !surface.IsValid() )
			Throw( typeof( SurfaceKHR ) );
	}

	internal static void ThrowIfInvalid( Queue queue )
	{
		if ( !queue.IsValid() )
			Throw( typeof( Queue ) );
	}

	internal static void ThrowIfInvalid( CommandPool commandPool )
	{
		if ( !commandPool.IsValid() )
			Throw( typeof( CommandPool ) );
	}

	internal static void ThrowIfInvalid( CommandBuffer commandBuffer )
	{
		if ( !commandBuffer.IsValid() )
			Throw( typeof( CommandBuffer ) );
	}

	internal static void ThrowIfInvalid( RenderPass renderPass )
	{
		if ( !renderPass.IsValid() )
			Throw( typeof( RenderPass ) );
	}

	internal static void ThrowIfInvalid( Framebuffer framebuffer )
	{
		if ( !framebuffer.IsValid() )
			Throw( typeof( Framebuffer ) );
	}

	internal static void ThrowIfInvalid( Fence fence )
	{
		if ( !fence.IsValid() )
			Throw( typeof( Fence ) );
	}

	internal static void ThrowIfInvalid( Semaphore semaphore )
	{
		if ( !semaphore.IsValid() )
			Throw( typeof( Semaphore ) );
	}

	internal static void ThrowIfInvalid( PipelineLayout pipelineLayout )
	{
		if ( !pipelineLayout.IsValid() )
			Throw( typeof( PipelineLayout ) );
	}

	internal static void ThrowIfInvalid( Pipeline pipeline )
	{
		if ( !pipeline.IsValid() )
			Throw( typeof( Pipeline ) );
	}

	internal static void ThrowIfInvalid( Buffer buffer )
	{
		if ( !buffer.IsValid() )
			Throw( typeof( Buffer ) );
	}

	internal static void ThrowIfInvalid( DeviceMemory deviceMemory )
	{
		if ( !deviceMemory.IsValid() )
			Throw( typeof( DeviceMemory ) );
	}

	internal static void ThrowIfInvalid( Image image )
	{
		if ( !image.IsValid() )
			Throw( typeof( Image ) );
	}

	internal static void ThrowIfInvalid( ImageView imageView )
	{
		if ( !imageView.IsValid() )
			Throw( typeof( ImageView ) );
	}

	internal static void ThrowIfInvalid( DescriptorPool descriptorPool )
	{
		if ( !descriptorPool.IsValid() )
			Throw( typeof( DescriptorPool ) );
	}

	internal static void ThrowIfInvalid( DescriptorSetLayout descriptorSetLayout )
	{
		if ( !descriptorSetLayout.IsValid() )
			Throw( typeof( DescriptorSetLayout ) );
	}

	internal static void ThrowIfInvalid( DescriptorSet descriptorSet )
	{
		if ( !descriptorSet.IsValid() )
			Throw( typeof( DescriptorSet ) );
	}
}

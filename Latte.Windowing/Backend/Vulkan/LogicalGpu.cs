using Latte.Assets;
using Latte.Windowing.Extensions;
using Latte.Windowing.Options;
using Silk.NET.Vulkan;
using System;
using System.Collections.Concurrent;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class LogicalGpu : VulkanWrapper
{
	internal Device LogicalDevice { get; }

	internal Queue GraphicsQueue { get; }
	internal Queue PresentQueue { get; }

	private ConcurrentQueue<WeakReference<Action>> DisposeQueue { get; } = new();

	private VulkanCommandPool OneTimeCommandPool { get; }

	private ConcurrentDictionary<int, VulkanSampler> Samplers { get; } = new();

	public LogicalGpu( in Device logicalDevice, Gpu gpu, in QueueFamilyIndices familyIndices ) : base( gpu )
	{
		if ( !familyIndices.IsComplete() )
			throw new ArgumentException( $"Cannot create {nameof( LogicalGpu )} with an incomplete {nameof( QueueFamilyIndices )}", nameof( familyIndices ) );

		LogicalDevice = logicalDevice;
		GraphicsQueue = Apis.Vk.GetDeviceQueue( LogicalDevice, familyIndices.GraphicsFamily.Value, 0 );
		PresentQueue = Apis.Vk.GetDeviceQueue( LogicalDevice, familyIndices.PresentFamily.Value, 0 );

		OneTimeCommandPool = CreateCommandPool( familyIndices.GraphicsFamily.Value );
	}

	public unsafe override void Dispose()
	{
		if ( Disposed )
			return;

		while ( DisposeQueue.TryDequeue( out var disposeCbRef ) )
		{
			if ( disposeCbRef.TryGetTarget( out var disposeCb ) )
				disposeCb();
		}

		Apis.Vk.DestroyDevice( LogicalDevice, null );

		GC.SuppressFinalize( this );
		Disposed = true;
	}

	internal TemporaryCommandBuffer BeginOneTimeCommands()
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var allocateInfo = new CommandBufferAllocateInfo
		{
			SType = StructureType.CommandBufferAllocateInfo,
			Level = CommandBufferLevel.Primary,
			CommandBufferCount = 1,
			CommandPool = OneTimeCommandPool
		};

		Apis.Vk.AllocateCommandBuffers( LogicalDevice, allocateInfo, out var commandBuffer ).Verify();

		var beginInfo = new CommandBufferBeginInfo
		{
			SType = StructureType.CommandBufferBeginInfo,
			Flags = CommandBufferUsageFlags.OneTimeSubmitBit
		};

		Apis.Vk.BeginCommandBuffer( commandBuffer, beginInfo ).Verify();
		return new TemporaryCommandBuffer( this, commandBuffer );
	}

	internal unsafe void EndOneTimeCommands( ref TemporaryCommandBuffer temporaryCommandBuffer )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		temporaryCommandBuffer.Disposed = true;
		var commandBuffer = temporaryCommandBuffer.CommandBuffer;
		Apis.Vk.EndCommandBuffer( commandBuffer ).Verify();

		var submitInfo = new SubmitInfo
		{
			SType = StructureType.SubmitInfo,
			CommandBufferCount = 1,
			PCommandBuffers = &commandBuffer
		};

		Apis.Vk.QueueSubmit( GraphicsQueue, 1, submitInfo, default ).Verify();
		Apis.Vk.QueueWaitIdle( GraphicsQueue ).Verify();
		Apis.Vk.FreeCommandBuffers( LogicalDevice, OneTimeCommandPool, 1, commandBuffer );
	}

	internal unsafe VulkanBuffer CreateBuffer( ulong size, BufferUsageFlags usageFlags, MemoryPropertyFlags memoryFlags,
		SharingMode sharingMode = SharingMode.Exclusive )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var buffer = VulkanBuffer.New( this, size, usageFlags, memoryFlags, sharingMode );
		DisposeQueue.Enqueue( new WeakReference<Action>( buffer.Dispose ) );
		return buffer;
	}

	internal unsafe VulkanCommandPool CreateCommandPool( uint queueFamilyIndex )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var commandPool = VulkanCommandPool.New( this, queueFamilyIndex );
		DisposeQueue.Enqueue( new WeakReference<Action>( commandPool.Dispose ) );
		return commandPool;
	}

	internal unsafe VulkanDescriptorPool CreateDescriptorPool( in ReadOnlySpan<DescriptorPoolSize> descriptorPoolSizes, uint maxDescriptorSets )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var descriptorPool = VulkanDescriptorPool.New( this, descriptorPoolSizes, maxDescriptorSets );
		DisposeQueue.Enqueue( new WeakReference<Action>( descriptorPool.Dispose ) );
		return descriptorPool;
	}

	internal unsafe VulkanDescriptorSetLayout CreateDescriptorSetLayout( in ReadOnlySpan<DescriptorSetLayoutBinding> bindings )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var descriptorSetLayout = VulkanDescriptorSetLayout.New( this, bindings );
		DisposeQueue.Enqueue( new WeakReference<Action>( descriptorSetLayout.Dispose ) );
		return descriptorSetLayout;
	}

	internal unsafe VulkanFence CreateFence( bool signaled = false )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var fence = VulkanFence.New( this, signaled );
		DisposeQueue.Enqueue( new WeakReference<Action>( fence.Dispose ) );
		return fence;
	}

	internal unsafe VulkanGraphicsPipeline CreateGraphicsPipeline( IRenderingOptions options, Shader shader, in Extent2D swapchainExtent,
		in VulkanRenderPass renderPass, in ReadOnlySpan<VertexInputBindingDescription> bindingDescriptions,
		in ReadOnlySpan<VertexInputAttributeDescription> attributeDescriptions, in ReadOnlySpan<DynamicState> dynamicStates,
		in ReadOnlySpan<VulkanDescriptorSetLayout> descriptorSetLayouts, in ReadOnlySpan<PushConstantRange> pushConstantRanges )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var graphicsPipeline = VulkanGraphicsPipeline.New( this, options, shader, swapchainExtent, renderPass, bindingDescriptions,
			attributeDescriptions, dynamicStates, descriptorSetLayouts, pushConstantRanges );
		DisposeQueue.Enqueue( new WeakReference<Action>( graphicsPipeline.Dispose ) );
		return graphicsPipeline;
	}

	internal unsafe VulkanImage CreateImage( uint width, uint height, uint mipLevels, SampleCountFlags numSamples,
		Format format, ImageTiling tiling, ImageUsageFlags usageFlags, MemoryPropertyFlags memoryPropertyFlags, ImageAspectFlags aspectFlags )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var image = VulkanImage.New( this, width, height, mipLevels, numSamples, format,
			tiling, usageFlags, memoryPropertyFlags, aspectFlags );
		DisposeQueue.Enqueue( new WeakReference<Action>( image.Dispose ) );
		return image;
	}

	internal unsafe VulkanRenderPass CreateRenderPass( Format swapchainImageFormat, SampleCountFlags msaaSamples )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var renderPass = VulkanRenderPass.New( this, swapchainImageFormat, msaaSamples );
		DisposeQueue.Enqueue( new WeakReference<Action>( renderPass.Dispose ) );
		return renderPass;
	}

	internal unsafe VulkanSampler CreateSampler( bool enableMsaa, uint mipLevels )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var creationHashCode = HashCode.Combine( enableMsaa, mipLevels );
		if ( Samplers.TryGetValue( creationHashCode, out var sampler ) )
			return sampler;

		sampler = VulkanSampler.New( this, enableMsaa, mipLevels );
		DisposeQueue.Enqueue( new WeakReference<Action>( sampler.Dispose ) );
		Samplers.TryAdd( creationHashCode, sampler );
		return sampler;
	}

	internal unsafe VulkanSemaphore CreateSemaphore()
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var semaphore = VulkanSemaphore.New( this );
		DisposeQueue.Enqueue( new WeakReference<Action>( semaphore.Dispose ) );
		return semaphore;
	}

	internal unsafe VulkanShaderModule CreateShaderModule( in ReadOnlySpan<byte> shaderCode )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var shaderModule = VulkanShaderModule.New( this, shaderCode );
		DisposeQueue.Enqueue( new WeakReference<Action>( shaderModule.Dispose ) );
		return shaderModule;
	}

	internal unsafe VulkanSwapchain CreateSwapchain()
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var swapchain = VulkanSwapchain.New( this );
		DisposeQueue.Enqueue( new WeakReference<Action>( swapchain.Dispose ) );
		return swapchain;
	}

	internal uint FindMemoryType( uint typeFilter, MemoryPropertyFlags properties )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var memoryProperties = Gpu!.MemoryProperties;
		for ( var i = 0; i < memoryProperties.MemoryTypeCount; i++ )
		{
			if ( (typeFilter & (1 << i)) != 0 && (memoryProperties.MemoryTypes[i].PropertyFlags & properties) == properties )
				return (uint)i;
		}

		throw new ApplicationException( "Failed to find suitable memory type" );
	}

	internal unsafe ImageView CreateImageView( in Image image, Format format, ImageAspectFlags aspectFlags, uint mipLevels )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var viewInfo = new ImageViewCreateInfo()
		{
			SType = StructureType.ImageViewCreateInfo,
			Image = image,
			ViewType = ImageViewType.Type2D,
			Format = format,
			SubresourceRange =
				{
					AspectMask = aspectFlags,
					BaseMipLevel = 0,
					LevelCount = mipLevels,
					BaseArrayLayer = 0,
					LayerCount = 1
				}
		};

		Apis.Vk.CreateImageView( LogicalDevice, viewInfo, null, out var imageView ).Verify();
		return imageView;
	}

	public static implicit operator Device( LogicalGpu logicalGpu )
	{
		if ( logicalGpu.Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		return logicalGpu.LogicalDevice;
	}
}

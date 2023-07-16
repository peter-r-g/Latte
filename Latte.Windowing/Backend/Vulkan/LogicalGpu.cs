using Latte.Assets;
using Latte.Windowing.Extensions;
using Latte.Windowing.Options;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class LogicalGpu : VulkanWrapper
{
	internal Device LogicalDevice { get; }

	internal Queue GraphicsQueue { get; }
	internal Queue PresentQueue { get; }

	private ConcurrentQueue<Action> DisposeQueue { get; } = new();

	private VulkanCommandPool OneTimeCommandPool { get; }

	private ConcurrentDictionary<Mesh, GpuBuffer<Vertex>> MeshVertexBuffers { get; } = new();
	private ConcurrentDictionary<Mesh, GpuBuffer<uint>> MeshIndexBuffers { get; } = new();
	private ConcurrentDictionary<Texture, DescriptorSet[]> TextureDescriptorSets { get; } = new();

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

		while ( DisposeQueue.TryDequeue( out var disposeCb ) )
			disposeCb();

		Apis.Vk.DestroyDevice( LogicalDevice, null );

		GC.SuppressFinalize( this );
		Disposed = true;
	}

	internal void UpdateFromOptions( IRenderingOptions options )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		if ( options.HasOptionsChanged( nameof( options.Msaa ) ) )
			TextureDescriptorSets.Clear();
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

	internal unsafe VulkanSwapchain CreateSwapchain()
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var swapchain = VulkanSwapchain.New( this );
		DisposeQueue.Enqueue( swapchain.Dispose );
		return swapchain;
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
		DisposeQueue.Enqueue( graphicsPipeline.Dispose );
		return graphicsPipeline;
	}

	internal unsafe VulkanDescriptorSetLayout CreateDescriptorSetLayout( in ReadOnlySpan<DescriptorSetLayoutBinding> bindings )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var descriptorSetLayout = VulkanDescriptorSetLayout.New( this, bindings );
		DisposeQueue.Enqueue( descriptorSetLayout.Dispose );
		return descriptorSetLayout;
	}

	internal unsafe VulkanRenderPass CreateRenderPass( Format swapchainImageFormat, SampleCountFlags msaaSamples )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var renderPass = VulkanRenderPass.New( this, swapchainImageFormat, msaaSamples );
		DisposeQueue.Enqueue( renderPass.Dispose );
		return renderPass;
	}

	internal unsafe VulkanCommandPool CreateCommandPool( uint queueFamilyIndex )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var commandPool = VulkanCommandPool.New( this, queueFamilyIndex );
		DisposeQueue.Enqueue( commandPool.Dispose );
		return commandPool;
	}

	internal unsafe VulkanDescriptorPool CreateDescriptorPool( in ReadOnlySpan<DescriptorPoolSize> descriptorPoolSizes, uint maxDescriptorSets )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var descriptorPool = VulkanDescriptorPool.New( this, descriptorPoolSizes, maxDescriptorSets );
		DisposeQueue.Enqueue( descriptorPool.Dispose );
		return descriptorPool;
	}

	internal unsafe VulkanBuffer CreateBuffer( ulong size, BufferUsageFlags usageFlags, MemoryPropertyFlags memoryFlags,
		SharingMode sharingMode = SharingMode.Exclusive )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var buffer = VulkanBuffer.New( this, size, usageFlags, memoryFlags, sharingMode );
		DisposeQueue.Enqueue( buffer.Dispose );
		return buffer;
	}

	internal unsafe VulkanImage CreateImage( uint width, uint height, uint mipLevels, SampleCountFlags numSamples,
		Format format, ImageTiling tiling, ImageUsageFlags usageFlags, MemoryPropertyFlags memoryPropertyFlags, ImageAspectFlags aspectFlags )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var image = VulkanImage.New( this, width, height, mipLevels, numSamples, format,
			tiling, usageFlags, memoryPropertyFlags, aspectFlags );
		DisposeQueue.Enqueue( image.Dispose );
		return image;
	}

	internal unsafe VulkanSampler CreateTextureSampler( bool enableMsaa, uint mipLevels )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var sampler = VulkanSampler.New( this, enableMsaa, mipLevels );
		DisposeQueue.Enqueue( sampler.Dispose );
		return sampler;
	}

	internal unsafe VulkanSemaphore CreateSemaphore()
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var semaphore = VulkanSemaphore.New( this );
		DisposeQueue.Enqueue( semaphore.Dispose );
		return semaphore;
	}

	internal unsafe VulkanFence CreateFence( bool signaled = false )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var fence = VulkanFence.New( this, signaled );
		DisposeQueue.Enqueue( fence.Dispose );
		return fence;
	}

	internal unsafe void GetMeshGpuBuffers( VulkanBackend vulkanBackend, Mesh mesh, out GpuBuffer<Vertex> gpuVertexBuffer, out GpuBuffer<uint>? gpuIndexBuffer )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		if ( !MeshVertexBuffers.TryGetValue( mesh, out gpuVertexBuffer! ) )
		{
			gpuVertexBuffer = new GpuBuffer<Vertex>( vulkanBackend, mesh.Vertices.AsSpan(), BufferUsageFlags.VertexBufferBit );
			MeshVertexBuffers.TryAdd( mesh, gpuVertexBuffer );
		}

		if ( !MeshIndexBuffers.TryGetValue( mesh, out gpuIndexBuffer ) && mesh.Indices.Length > 0 )
		{
			gpuIndexBuffer = new GpuBuffer<uint>( vulkanBackend, mesh.Indices.AsSpan(), BufferUsageFlags.IndexBufferBit );
			MeshIndexBuffers.TryAdd( mesh, gpuIndexBuffer );
		}
	}

	internal unsafe DescriptorSet[] GetTextureDescriptorSets( VulkanBackend vulkanBackend, Texture texture, in VulkanDescriptorSetLayout descriptorSetLayout,
		in VulkanDescriptorPool descriptorPool, VulkanBuffer[] ubos, SampleCountFlags numSamples )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		if ( TextureDescriptorSets.TryGetValue( texture, out var descriptorSets ) )
			return descriptorSets;

		descriptorSets = new DescriptorSet[(int)VulkanBackend.MaxFramesInFlight];

		var layouts = stackalloc DescriptorSetLayout[(int)VulkanBackend.MaxFramesInFlight];
		for ( var i = 0; i < VulkanBackend.MaxFramesInFlight; i++ )
			layouts[i] = descriptorSetLayout;

		var allocateInfo = new DescriptorSetAllocateInfo
		{
			SType = StructureType.DescriptorSetAllocateInfo,
			DescriptorPool = descriptorPool,
			DescriptorSetCount = VulkanBackend.MaxFramesInFlight,
			PSetLayouts = layouts
		};

		Apis.Vk.AllocateDescriptorSets( LogicalDevice, &allocateInfo, descriptorSets ).Verify();

		var textureImage = CreateImage( (uint)texture.Width, (uint)texture.Height, texture.MipLevels, SampleCountFlags.Count1Bit,
			Format.R8G8B8A8Srgb, ImageTiling.Optimal,
			ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
			MemoryPropertyFlags.DeviceLocalBit, ImageAspectFlags.ColorBit );

		var textureSize = (ulong)texture.Width * (ulong)texture.Height * (ulong)texture.BytesPerPixel;
		var stagingBuffer = CreateBuffer( textureSize, BufferUsageFlags.TransferSrcBit,
			MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit );
		stagingBuffer.SetMemory( texture.PixelData.Span );

		using ( var commandBuffer = BeginOneTimeCommands() )
		{
			textureImage.TransitionImageLayout( commandBuffer, Format.R8G8B8A8Srgb, ImageLayout.Undefined, ImageLayout.TransferDstOptimal, texture.MipLevels );
			textureImage.CopyBufferToImage( commandBuffer, stagingBuffer, (uint)texture.Width, (uint)texture.Height );
			textureImage.GenerateMipMaps( commandBuffer, Format.R8G8B8A8Srgb, (uint)texture.Width, (uint)texture.Height, texture.MipLevels );
		}

		var descriptorWrites = stackalloc WriteDescriptorSet[2];
		for ( var i = 0; i < VulkanBackend.MaxFramesInFlight; i++ )
		{
			var bufferInfo = new DescriptorBufferInfo
			{
				Buffer = ubos[i],
				Offset = 0,
				Range = (ulong)sizeof( UniformBufferObject )
			};

			var imageInfo = new DescriptorImageInfo
			{
				ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
				ImageView = textureImage.View,
				Sampler = CreateTextureSampler( numSamples != SampleCountFlags.Count1Bit, texture.MipLevels )
			};

			var uboWrite = new WriteDescriptorSet
			{
				SType = StructureType.WriteDescriptorSet,
				DstSet = descriptorSets[i],
				DstBinding = 0,
				DstArrayElement = 0,
				DescriptorType = DescriptorType.UniformBuffer,
				DescriptorCount = 1,
				PBufferInfo = &bufferInfo
			};
			descriptorWrites[0] = uboWrite;

			var imageWrite = new WriteDescriptorSet
			{
				SType = StructureType.WriteDescriptorSet,
				DstSet = descriptorSets[i],
				DstBinding = 1,
				DstArrayElement = 0,
				DescriptorType = DescriptorType.CombinedImageSampler,
				DescriptorCount = 1,
				PImageInfo = &imageInfo
			};
			descriptorWrites[1] = imageWrite;

			Apis.Vk.UpdateDescriptorSets( LogicalDevice, 2, descriptorWrites, 0, null );
		}

		TextureDescriptorSets.TryAdd( texture, descriptorSets );
		return descriptorSets;
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

	internal unsafe VulkanShaderModule CreateShaderModule( in ReadOnlySpan<byte> shaderCode )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var shaderModule = VulkanShaderModule.New( this, shaderCode );
		DisposeQueue.Enqueue( shaderModule.Dispose );
		return shaderModule;
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

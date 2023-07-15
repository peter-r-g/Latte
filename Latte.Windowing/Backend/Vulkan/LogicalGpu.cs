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

		var poolInfo = new CommandPoolCreateInfo
		{
			SType = StructureType.CommandPoolCreateInfo,
			Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
			QueueFamilyIndex = queueFamilyIndex
		};

		Apis.Vk.CreateCommandPool( LogicalDevice, poolInfo, null, out var commandPool ).Verify();

		var vulkanCommandPool = new VulkanCommandPool( commandPool, this );
		DisposeQueue.Enqueue( vulkanCommandPool.Dispose );
		return vulkanCommandPool;
	}

	internal unsafe VulkanDescriptorPool CreateDescriptorPool( in ReadOnlySpan<DescriptorPoolSize> descriptorPoolSizes, uint maxDescriptorSets )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		fixed ( DescriptorPoolSize* descriptorPoolSizesPtr = descriptorPoolSizes )
		{
			var poolInfo = new DescriptorPoolCreateInfo
			{
				SType = StructureType.DescriptorPoolCreateInfo,
				PoolSizeCount = 2,
				PPoolSizes = descriptorPoolSizesPtr,
				MaxSets = maxDescriptorSets
			};

			Apis.Vk.CreateDescriptorPool( LogicalDevice, poolInfo, null, out var descriptorPool ).Verify();

			var vulkanDescriptorPool = new VulkanDescriptorPool( descriptorPool, this );
			DisposeQueue.Enqueue( vulkanDescriptorPool.Dispose );
			return vulkanDescriptorPool;
		}
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

		CreateImage( width, height, mipLevels, numSamples,
			format, tiling, usageFlags, memoryPropertyFlags,
			out var image, out var imageMemory );

		var imageView = CreateImageView( image, format, aspectFlags, 1 );
		var vulkanImage = new VulkanImage( image, imageMemory, imageView, this );

		DisposeQueue.Enqueue( vulkanImage.Dispose );
		return vulkanImage;
	}

	internal unsafe VulkanSampler CreateTextureSampler( bool enableMsaa, uint mipLevels )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var samplerInfo = new SamplerCreateInfo()
		{
			SType = StructureType.SamplerCreateInfo,
			MagFilter = Filter.Linear,
			MinFilter = Filter.Linear,
			AddressModeU = SamplerAddressMode.Repeat,
			AddressModeV = SamplerAddressMode.Repeat,
			AddressModeW = SamplerAddressMode.Repeat,
			AnisotropyEnable = enableMsaa ? Vk.True : Vk.False,
			MaxAnisotropy = Gpu!.Properties.Limits.MaxSamplerAnisotropy,
			BorderColor = BorderColor.IntOpaqueBlack,
			UnnormalizedCoordinates = Vk.False,
			CompareEnable = Vk.False,
			CompareOp = CompareOp.Always,
			MipmapMode = SamplerMipmapMode.Linear,
			MipLodBias = 0,
			MinLod = 0,
			MaxLod = mipLevels
		};

		Apis.Vk.CreateSampler( LogicalDevice, samplerInfo, null, out var sampler ).Verify();

		var vulkanSampler = new VulkanSampler( sampler, this );
		DisposeQueue.Enqueue( vulkanSampler.Dispose );
		return vulkanSampler;
	}

	internal unsafe VulkanSemaphore CreateSemaphore()
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var semaphoreCreateInfo = new SemaphoreCreateInfo
		{
			SType = StructureType.SemaphoreCreateInfo
		};

		Apis.Vk.CreateSemaphore( LogicalDevice, semaphoreCreateInfo, null, out var semaphore ).Verify();

		var vulkanSemaphore = new VulkanSemaphore( semaphore, this );
		DisposeQueue.Enqueue( vulkanSemaphore.Dispose );
		return vulkanSemaphore;
	}

	internal unsafe VulkanFence CreateFence( bool signaled = false )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		var fenceInfo = new FenceCreateInfo
		{
			SType = StructureType.FenceCreateInfo,
			Flags = signaled ? FenceCreateFlags.SignaledBit : 0
		};

		Apis.Vk.CreateFence( LogicalDevice, fenceInfo, null, out var fence ).Verify();

		var vulkanFence = new VulkanFence( fence, this );
		DisposeQueue.Enqueue( vulkanFence.Dispose );
		return vulkanFence;
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
		var createInfo = new ShaderModuleCreateInfo
		{
			SType = StructureType.ShaderModuleCreateInfo,
			CodeSize = (nuint)shaderCode.Length
		};

		fixed ( byte* shaderCodePtr = shaderCode )
		{
			createInfo.PCode = (uint*)shaderCodePtr;

			Apis.Vk.CreateShaderModule( LogicalDevice, createInfo, null, out var shaderModule ).Verify();

			var vulkanShaderModule = new VulkanShaderModule( shaderModule, this );
			DisposeQueue.Enqueue( vulkanShaderModule.Dispose );
			return vulkanShaderModule;
		}
	}

	internal unsafe ImageView CreateImageView( in Image image, Format format, ImageAspectFlags aspectFlags, uint mipLevels )
	{
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

	private unsafe void CreateImage( uint width, uint height, uint mipLevels, SampleCountFlags numSamples,
		Format format, ImageTiling tiling, ImageUsageFlags usageFlags, MemoryPropertyFlags memoryPropertyFlags,
		out Image image, out DeviceMemory imageMemory )
	{
		var imageInfo = new ImageCreateInfo()
		{
			SType = StructureType.ImageCreateInfo,
			ImageType = ImageType.Type2D,
			Extent =
			{
				Width = width,
				Height = height,
				Depth = 1
			},
			MipLevels = mipLevels,
			ArrayLayers = 1,
			Format = format,
			Tiling = tiling,
			InitialLayout = ImageLayout.Undefined,
			Usage = usageFlags,
			SharingMode = SharingMode.Exclusive,
			Samples = numSamples
		};

		Apis.Vk.CreateImage( LogicalDevice, imageInfo, null, out image ).Verify();

		var requirements = Apis.Vk.GetImageMemoryRequirements( LogicalDevice, image );
		var allocateInfo = new MemoryAllocateInfo()
		{
			SType = StructureType.MemoryAllocateInfo,
			AllocationSize = requirements.Size,
			MemoryTypeIndex = FindMemoryType( requirements.MemoryTypeBits, memoryPropertyFlags )
		};

		Apis.Vk.AllocateMemory( LogicalDevice, allocateInfo, null, out imageMemory ).Verify();
		Apis.Vk.BindImageMemory( LogicalDevice, image, imageMemory, 0 ).Verify();
	}

	public static implicit operator Device( LogicalGpu logicalGpu )
	{
		if ( logicalGpu.Disposed )
			throw new ObjectDisposedException( nameof( LogicalGpu ) );

		return logicalGpu.LogicalDevice;
	}
}

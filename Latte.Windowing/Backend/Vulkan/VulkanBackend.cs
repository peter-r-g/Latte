using Latte.Assets;
using Latte.Windowing.Extensions;
using Latte.Windowing.Options;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using Zio;
using Buffer = Silk.NET.Vulkan.Buffer;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Latte.Windowing.Backend.Vulkan;

internal unsafe class VulkanBackend : IInternalRenderingBackend
{
	internal const uint MaxFramesInFlight = 2;
	internal const int ExtraSwapImages = 1;

	public IRenderingOptions Options { get; }
	public event IRenderingBackend.OptionsAppliedHandler? OptionsApplied;

	private bool EnableValidationLayers { get; set; }
	private readonly string[] ValidationLayers = new[]
	{
		"VK_LAYER_KHRONOS_validation"
	};
	private readonly string[] DeviceExtensions = new[]
	{
		KhrSwapchain.ExtensionName
	};

	private static bool StaticInitialized { get; set; }
	private VulkanInstance Instance { get; set; } = null!;

	private IWindow Window { get; }
	private Gpu Gpu { get; set; } = null!;
	private LogicalGpu LogicalGpu { get; set; } = null!;
	private VulkanSwapchain Swapchain { get; set; } = null!;

	private Shader DefaultShader { get; set; } = null!;

	private VulkanRenderPass RenderPass { get; set; } = null!;
	private VulkanDescriptorSetLayout DescriptorSetLayout { get; set; } = null!;
	private VulkanGraphicsPipeline GraphicsPipeline { get; set; } = null!;
	private VulkanCommandPool CommandPool { get; set; } = null!;
	private VulkanBuffer[] UniformBuffers { get; set; } = Array.Empty<VulkanBuffer>();
	private Dictionary<BufferUsageFlags, List<VulkanBuffer>> GpuBuffers { get; } = new();
	private Dictionary<BufferUsageFlags, List<ulong>> GpuBuffersOffsets { get; } = new();
	private VulkanDescriptorPool DescriptorPool { get; set; } = null!;
	private CommandBuffer[] CommandBuffers { get; set; } = Array.Empty<CommandBuffer>();

	private VulkanImage DepthImage { get; set; } = null!;
	private VulkanImage ColorImage { get; set; } = null!;

	private VulkanSemaphore[] ImageAvailableSemaphores { get; set; } = Array.Empty<VulkanSemaphore>();
	private VulkanSemaphore[] RenderFinishedSemaphores { get; set; } = Array.Empty<VulkanSemaphore>();
	private VulkanFence[] InFlightFences { get; set; } = Array.Empty<VulkanFence>();

	private bool HasFrameBufferResized { get; set; }

	private ConcurrentDictionary<Mesh, GpuBuffer<Vertex>> MeshVertexBuffers { get; } = new();
	private ConcurrentDictionary<Mesh, GpuBuffer<uint>> MeshIndexBuffers { get; } = new();
	private ConcurrentDictionary<Texture, DescriptorSet[]> TextureDescriptorSets { get; } = new();

	private uint CurrentImageIndex { get; set; }
	private CommandBuffer CurrentCommandBuffer { get; set; }
	private uint CurrentFrame { get; set; }
	private Texture? CurrentTexture { get; set; }
	private Vector3 CurrentModelPosition { get; set; }
	private Buffer CurrentVertexBuffer { get; set; }
	private Buffer CurrentIndexBuffer { get; set; }

	private Gpu[] AllGpus
	{
		get
		{
			uint deviceCount;
			Apis.Vk.EnumeratePhysicalDevices( Instance, &deviceCount, null ).Verify();

			if ( deviceCount == 0 )
				return Array.Empty<Gpu>();

			var devices = stackalloc PhysicalDevice[(int)deviceCount];
			Apis.Vk.EnumeratePhysicalDevices( Instance, &deviceCount, devices ).Verify();

			var gpus = new Gpu[(int)deviceCount];
			for ( var i = 0; i < deviceCount; i++ )
				gpus[i] = new Gpu( devices[i], Instance );

			return gpus;
		}
	}

	internal VulkanBackend( IWindow window, bool enableValidationLayers )
	{
		Window = window;
		EnableValidationLayers = enableValidationLayers;
		Options = new VulkanOptions( this );
	}

	#region API
	public void StaticInitialize()
	{
		if ( StaticInitialized )
			return;

		StaticInitialized = true;
		Instance = new VulkanInstance( Window, EnableValidationLayers, ValidationLayers );
	}

	public void Inititalize()
	{
		StaticInitialize();

		PickPhysicalDevice();
		CreateLogicalDevice();
		CreateDefaultShader();
		CreateSwapChain();
		CreateRenderPass();
		CreateDescriptorSetLayout();
		CreateGraphicsPipeline();
		CreateCommandPool();
		CreateColorResources();
		CreateDepthResources();
		CreateFrameBuffers();
		CreateUniformBuffers();
		CreateDescriptorPool();
		CreateCommandBuffer();
		CreateSyncObjects();

		Window.FramebufferResize += OnFrameBufferResize;
	}

	public void Cleanup()
	{
		WaitForIdle();
		Window.FramebufferResize -= OnFrameBufferResize;

		Gpu.Dispose();
		Instance.Dispose();
	}

	public void BeginFrame()
	{
		Apis.Vk.WaitForFences( LogicalGpu, 1, InFlightFences[CurrentFrame], Vk.True, ulong.MaxValue ).Verify();

		uint imageIndex;
		var result = Swapchain.Extension.AcquireNextImage( LogicalGpu, Swapchain, ulong.MaxValue,
			ImageAvailableSemaphores[CurrentFrame], default, &imageIndex );
		CurrentImageIndex = imageIndex;
		switch ( result )
		{
			case Result.ErrorOutOfDateKhr:
			case Result.SuboptimalKhr:
			case Result.Success when HasFrameBufferResized:
				RecreateSwapChain();
				return;
			case Result.Success:
				break;
			default:
				throw new ApplicationException( "Failed to acquire next image in the swap chain" );
		}

		Apis.Vk.ResetFences( LogicalGpu, 1, InFlightFences[CurrentFrame] ).Verify();
		Apis.Vk.ResetCommandBuffer( CommandBuffers[CurrentFrame], 0 ).Verify();

		UpdateUniformBuffer( CurrentFrame );

		CurrentCommandBuffer = CommandBuffers[CurrentFrame];

		var beginInfo = new CommandBufferBeginInfo
		{
			SType = StructureType.CommandBufferBeginInfo
		};

		Apis.Vk.BeginCommandBuffer( CurrentCommandBuffer, beginInfo ).Verify();

		var renderPassInfo = new RenderPassBeginInfo
		{
			SType = StructureType.RenderPassBeginInfo,
			RenderPass = RenderPass,
			Framebuffer = Swapchain.FrameBuffers[(int)imageIndex],
			ClearValueCount = 1,
			RenderArea =
			{
				Offset = { X = 0, Y = 0 },
				Extent = Swapchain.Extent
			}
		};

		var clearValues = stackalloc ClearValue[]
		{
			new ClearValue( new ClearColorValue( 0.0f, 0.0f, 0.0f, 1.0f ) ),
			new ClearValue( null, new ClearDepthStencilValue( 1, 0 ) )
		};
		renderPassInfo.ClearValueCount = 2;
		renderPassInfo.PClearValues = clearValues;

		Apis.Vk.CmdBeginRenderPass( CurrentCommandBuffer, renderPassInfo, SubpassContents.Inline );
		Apis.Vk.CmdBindPipeline( CurrentCommandBuffer, PipelineBindPoint.Graphics, GraphicsPipeline );

		var viewport = new Viewport()
		{
			X = 0,
			Y = 0,
			Width = Swapchain.Extent.Width,
			Height = Swapchain.Extent.Height,
			MinDepth = 0,
			MaxDepth = 1
		};
		Apis.Vk.CmdSetViewport( CurrentCommandBuffer, 0, 1, viewport );

		var scissor = new Rect2D()
		{
			Offset = new Offset2D( 0, 0 ),
			Extent = Swapchain.Extent
		};
		Apis.Vk.CmdSetScissor( CurrentCommandBuffer, 0, 1, scissor );

		CurrentModelPosition = Vector3.Zero;
		var defaultConstants = new PushConstants( Matrix4x4.CreateTranslation( CurrentModelPosition ) );
		Apis.Vk.CmdPushConstants( CurrentCommandBuffer, GraphicsPipeline.Layout, ShaderStageFlags.VertexBit, 0, (uint)sizeof( PushConstants ), &defaultConstants );

		// TODO: Multiple textures crash Vulkan due to lack of descriptor set space.
		//SetTexture( Texture.Missing );
	}

	public void EndFrame()
	{
		CurrentTexture = null;
		CurrentIndexBuffer = default;
		CurrentVertexBuffer = default;
		CurrentModelPosition = default;

		Apis.Vk.CmdEndRenderPass( CurrentCommandBuffer );
		Apis.Vk.EndCommandBuffer( CurrentCommandBuffer ).Verify();

		var submitInfo = new SubmitInfo
		{
			SType = StructureType.SubmitInfo
		};

		PipelineStageFlags* waitStages = stackalloc PipelineStageFlags[1];
		waitStages[0] = PipelineStageFlags.ColorAttachmentOutputBit;
		submitInfo.PWaitDstStageMask = waitStages;

		var currentCommandBuffer = CurrentCommandBuffer;
		submitInfo.CommandBufferCount = 1;
		submitInfo.PCommandBuffers = &currentCommandBuffer;

		var waitSemaphoreCount = 1;
		var waitSemaphores = stackalloc Semaphore[]
		{
			ImageAvailableSemaphores[CurrentFrame]
		};
		submitInfo.WaitSemaphoreCount = (uint)waitSemaphoreCount;
		submitInfo.PWaitSemaphores = waitSemaphores;

		var signalSemaphoreCount = 1;
		var signalSemaphores = stackalloc Semaphore[]
		{
			RenderFinishedSemaphores[CurrentFrame]
		};
		submitInfo.SignalSemaphoreCount = (uint)signalSemaphoreCount;
		submitInfo.PSignalSemaphores = signalSemaphores;

		Apis.Vk.QueueSubmit( LogicalGpu.GraphicsQueue, 1, submitInfo, InFlightFences[CurrentFrame] ).Verify();

		var swapchainCount = 1;
		var swapchains = stackalloc SwapchainKHR[]
		{
			Swapchain
		};

		var currentImageIndex = CurrentImageIndex;
		var presentInfo = new PresentInfoKHR
		{
			SType = StructureType.PresentInfoKhr,
			WaitSemaphoreCount = 1,
			PWaitSemaphores = signalSemaphores,
			SwapchainCount = (uint)swapchainCount,
			PSwapchains = swapchains,
			PImageIndices = &currentImageIndex
		};

		var result = Swapchain.Extension.QueuePresent( LogicalGpu.PresentQueue, presentInfo );
		switch ( result )
		{
			case Result.ErrorOutOfDateKhr:
			case Result.SuboptimalKhr:
			case Result.Success when HasFrameBufferResized:
				RecreateSwapChain();
				break;
			case Result.Success:
				break;
			default:
				throw new ApplicationException( "Failed to present queue" );
		}

		CurrentCommandBuffer = default;
		CurrentFrame = (CurrentFrame + 1) % MaxFramesInFlight;
	}

	public void WaitForIdle()
	{
		Apis.Vk.DeviceWaitIdle( LogicalGpu );
	}

	public void SetTexture( Texture texture )
	{
		if ( CurrentCommandBuffer.Handle == nint.Zero )
			throw new InvalidOperationException( "You cannot set textures outside of a rendering block" );

		if ( texture == CurrentTexture )
			return;

		CurrentTexture = texture;
		var descriptorSets = GetTextureDescriptorSets( texture, DescriptorSetLayout, DescriptorPool, UniformBuffers, Options.Msaa.ToVulkan() );
		Apis.Vk.CmdBindDescriptorSets( CurrentCommandBuffer, PipelineBindPoint.Graphics, GraphicsPipeline.Layout, 0, 1, descriptorSets[CurrentFrame], 0, null );
	}

	public void DrawModel( Model model ) => DrawModel( model, Vector3.Zero );
	public void DrawModel( Model model, in Vector3 position )
	{
		if ( CurrentCommandBuffer.Handle == nint.Zero )
			throw new InvalidOperationException( "You cannot draw models outside of a rendering block" );

		if ( position != CurrentModelPosition )
		{
			var pushConstants = new PushConstants( Matrix4x4.CreateTranslation( position ) );
			Apis.Vk.CmdPushConstants( CurrentCommandBuffer, GraphicsPipeline.Layout, ShaderStageFlags.VertexBit, 0, (uint)sizeof( PushConstants ), &pushConstants );
		}

		var vertexBuffers = stackalloc Buffer[1];
		foreach ( var mesh in model.Meshes )
		{
			GetMeshGpuBuffers( mesh, out var gpuVertexBuffer, out var gpuIndexBuffer );

			if ( gpuVertexBuffer.Buffer.Handle != CurrentVertexBuffer.Handle )
			{
				var newVertexBuffer = gpuVertexBuffer;
				vertexBuffers[0] = newVertexBuffer.Buffer;
				Apis.Vk.CmdBindVertexBuffers( CurrentCommandBuffer, 0, 1, vertexBuffers, newVertexBuffer.Offset );
				CurrentVertexBuffer = newVertexBuffer.Buffer;
			}
			if ( gpuIndexBuffer is not null && gpuIndexBuffer.Buffer.Handle != CurrentIndexBuffer.Handle )
			{
				var newIndexBuffer = gpuIndexBuffer;
				Apis.Vk.CmdBindIndexBuffer( CurrentCommandBuffer, newIndexBuffer.Buffer, newIndexBuffer.Offset, IndexType.Uint32 );
				CurrentIndexBuffer = newIndexBuffer.Buffer;
			}

			Apis.Vk.CmdDrawIndexed( CurrentCommandBuffer, (uint)mesh.Indices.Length, 1, 0, 0, 0 );
		}
	}

	internal void UpdateFromOptions()
	{
		if ( !Options.HasOptionsChanged() )
			return;

		WaitForIdle();

		if ( Options.HasOptionsChanged( nameof( Options.Msaa ) ) )
		{
			TextureDescriptorSets.Clear();
			DescriptorPool.Dispose();
			GraphicsPipeline.Dispose();
			RenderPass.Dispose();
			CleanupSwapChain();

			CreateSwapChain();
			CreateRenderPass();
			CreateGraphicsPipeline();
			CreateColorResources();
			CreateDepthResources();
			CreateFrameBuffers();
			CreateDescriptorPool();
		}
		else if ( Options.HasOptionsChanged( nameof( Options.WireframeEnabled ) ) )
		{
			GraphicsPipeline.Dispose();
			CreateGraphicsPipeline();
		}

		OptionsApplied?.Invoke( this );
	}

	internal VulkanBuffer GetCPUBuffer( ulong size, BufferUsageFlags usageFlags )
	{
		return LogicalGpu.CreateBuffer( size, usageFlags, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit );
	}

	internal VulkanBuffer GetGPUBuffer( ulong size, BufferUsageFlags usage, out ulong offset )
	{
		const ulong requestSize = 1024000;

		if ( !GpuBuffers.TryGetValue( usage, out var buffers ) )
		{
			buffers = new List<VulkanBuffer>();
			GpuBuffers.Add( usage, buffers );
			GpuBuffersOffsets.Add( usage, new List<ulong>() );
		}
		var buffersOffsets = GpuBuffersOffsets[usage];

		var bufferIndex = 0;
		VulkanBuffer? chosenBuffer = null;
		var bufferOffset = 0ul;
		for ( var i = 0; i < buffers.Count; i++ )
		{
			var thisBuffer = buffers[i];
			var thisBufferOffset = buffersOffsets[i];

			if ( thisBufferOffset + size >= requestSize )
				continue;

			bufferIndex = i;
			chosenBuffer = thisBuffer;
			bufferOffset = thisBufferOffset;
			break;
		}

		if ( chosenBuffer is null )
		{
			var runtimeBuffer = LogicalGpu.CreateBuffer( requestSize, BufferUsageFlags.TransferDstBit | usage, MemoryPropertyFlags.DeviceLocalBit );

			buffers.Add( runtimeBuffer );
			buffersOffsets.Add( 0 );

			bufferIndex = buffers.Count - 1;
			chosenBuffer = runtimeBuffer;
			bufferOffset = 0;
		}

		offset = bufferOffset;
		buffersOffsets[bufferIndex] += size;
		return chosenBuffer;
	}

	internal void UploadToBuffer<T>( in Buffer buffer, in ReadOnlySpan<T> data ) where T : unmanaged
	{
		var bufferSize = (ulong)sizeof( T ) * (ulong)data.Length;
		using var stagingBuffer = LogicalGpu.CreateBuffer( bufferSize, BufferUsageFlags.TransferSrcBit,
			MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit );

		stagingBuffer.SetMemory( data );

		using var commandBuffer = LogicalGpu.BeginOneTimeCommands();

		var copyRegion = new BufferCopy
		{
			Size = bufferSize
		};

		Apis.Vk.CmdCopyBuffer( commandBuffer, stagingBuffer, buffer, 1, copyRegion );
	}

	internal VulkanImage CreateImage( uint width, uint height, uint mipLevels ) => LogicalGpu.CreateImage(
		width, height, mipLevels,
		SampleCountFlags.Count1Bit, Format.R8G8B8A8Srgb, ImageTiling.Optimal,
		ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
		MemoryPropertyFlags.DeviceLocalBit, ImageAspectFlags.ColorBit );
	#endregion

	#region Internal
	private void CleanupSwapChain()
	{
		ColorImage.Dispose();
		DepthImage.Dispose();
		Swapchain.Dispose();
	}

	private void RecreateSwapChain()
	{
		// TODO: This should probably not do this.
		while ( Window.FramebufferSize.X == 0 || Window.FramebufferSize.Y == 0 )
			Window.DoEvents();

		WaitForIdle();

		HasFrameBufferResized = false;
		CleanupSwapChain();

		CreateSwapChain();
		CreateColorResources();
		CreateDepthResources();
		CreateFrameBuffers();
	}

	private void OnFrameBufferResize( Vector2D<int> newSize )
	{
		HasFrameBufferResized = true;
	}

	private void UpdateUniformBuffer( uint currentImage )
	{
		var view = Matrix4x4.CreateLookAt( Camera.Position, Camera.Position + Camera.Front, Camera.Up );
		//It's super important for the width / height calculation to regard each value as a float, otherwise
		//it creates rounding errors that result in viewport distortion
		var projection = Matrix4x4.CreatePerspectiveFieldOfView( Scalar.DegreesToRadians( Camera.Zoom ), (float)Window.Size.X / Window.Size.Y, Camera.ZNear, Camera.ZFar );
		var ubo = new UniformBufferObject( view, projection );

		Span<UniformBufferObject> data = stackalloc UniformBufferObject[]
		{
			ubo
		};
		UniformBuffers[currentImage].SetMemory<UniformBufferObject>( data );
	}

	#region Initialization stages
	private void PickPhysicalDevice()
	{
		Gpu = AllGpus.GetBestGpu( out var deviceScore, IsDeviceSuitable );
		if ( Gpu.PhysicalDevice.Handle == nint.Zero || deviceScore <= 0 )
			throw new ApplicationException( "Failed to find a suitable device" );
	}

	private void CreateLogicalDevice()
	{
		var indices = Gpu.GetQueueFamilyIndices();

		var features = new PhysicalDeviceFeatures
		{
			SamplerAnisotropy = Vk.True,
			SampleRateShading = Vk.True,
			FillModeNonSolid = Vk.True
		};

		LogicalGpu = Gpu.CreateLogicalGpu( indices, features, DeviceExtensions,
			EnableValidationLayers, ValidationLayers );
	}

	private void CreateDefaultShader()
	{
		DefaultShader = Shader.FromPath(
			UPath.Combine( "Shaders", "vert.spv" ),
			UPath.Combine( "Shaders", "frag.spv" ) );
	}

	private void CreateSwapChain()
	{
		Swapchain = LogicalGpu.CreateSwapchain();
	}

	private void CreateRenderPass()
	{
		RenderPass = LogicalGpu.CreateRenderPass( Swapchain.ImageFormat, Options.Msaa.ToVulkan() );
	}

	private void CreateDescriptorSetLayout()
	{
		ReadOnlySpan<DescriptorSetLayoutBinding> bindings = stackalloc DescriptorSetLayoutBinding[]
		{
			new DescriptorSetLayoutBinding
			{
				Binding = 0,
				DescriptorType = DescriptorType.UniformBuffer,
				DescriptorCount = 1,
				StageFlags = ShaderStageFlags.VertexBit
			},
			new DescriptorSetLayoutBinding
			{
				Binding = 1,
				DescriptorType = DescriptorType.CombinedImageSampler,
				DescriptorCount = 1,
				PImmutableSamplers = null,
				StageFlags = ShaderStageFlags.FragmentBit
			}
		};

		DescriptorSetLayout = LogicalGpu.CreateDescriptorSetLayout( bindings );
	}

	private void CreateGraphicsPipeline()
	{
		var bindingDescriptions = VertexDescriptions.GetBindingDescriptions();
		var attributeDescriptions = VertexDescriptions.GetAttributeDescriptions();
		ReadOnlySpan<DynamicState> dynamicStates = stackalloc DynamicState[]
		{
			DynamicState.Viewport,
			DynamicState.Scissor
		};
		ReadOnlySpan<VulkanDescriptorSetLayout> descriptorSetLayouts = new VulkanDescriptorSetLayout[]
		{
			DescriptorSetLayout
		};
		ReadOnlySpan<PushConstantRange> pushConstantRanges = stackalloc PushConstantRange[]
		{
			new PushConstantRange
			{
				Offset = 0,
				Size = (uint)sizeof( PushConstants ),
				StageFlags = ShaderStageFlags.VertexBit
			}
		};

		GraphicsPipeline = LogicalGpu.CreateGraphicsPipeline( Options, DefaultShader, Swapchain.Extent, RenderPass,
			bindingDescriptions, attributeDescriptions, dynamicStates,
			descriptorSetLayouts, pushConstantRanges );
	}

	private void CreateCommandPool()
	{
		var indices = Gpu.GetQueueFamilyIndices();
		if ( !indices.IsComplete() )
			throw new ApplicationException( "Attempted to create a command pool from indices that are not complete" );

		CommandPool = LogicalGpu.CreateCommandPool( indices.GraphicsFamily.Value );
	}

	private void CreateColorResources()
	{
		ColorImage = LogicalGpu.CreateImage( Swapchain.Extent.Width, Swapchain.Extent.Height, 1, Options.Msaa.ToVulkan(),
			Swapchain.ImageFormat, ImageTiling.Optimal, ImageUsageFlags.TransientAttachmentBit | ImageUsageFlags.ColorAttachmentBit,
			MemoryPropertyFlags.DeviceLocalBit, ImageAspectFlags.ColorBit );
	}

	private void CreateDepthResources()
	{
		var depthFormat = FindDepthFormat();
		DepthImage = LogicalGpu.CreateImage( Swapchain.Extent.Width, Swapchain.Extent.Height, 1, Options.Msaa.ToVulkan(),
			depthFormat, ImageTiling.Optimal, ImageUsageFlags.DepthStencilAttachmentBit,
			MemoryPropertyFlags.DeviceLocalBit, ImageAspectFlags.DepthBit );

		using var commandBuffer = LogicalGpu.BeginOneTimeCommands();
		DepthImage.TransitionImageLayout( commandBuffer, depthFormat,
			ImageLayout.Undefined, ImageLayout.DepthStencilAttachmentOptimal, 1 );
	}

	private void CreateFrameBuffers()
	{
		var useMsaa = Options.Msaa != MsaaOption.One;
		var attachments = new ImageView[useMsaa ? 3 : 2];
		attachments[0] = ColorImage.View;
		attachments[1] = DepthImage.View;

		Swapchain.CreateFrameBuffers( RenderPass, attachments, frameBufferIndex =>
		{
			if ( useMsaa )
				attachments[2] = Swapchain.ImageViews[frameBufferIndex];
			else
				attachments[0] = Swapchain.ImageViews[frameBufferIndex];
		} );
	}

	private void CreateUniformBuffers()
	{
		var bufferSize = (ulong)sizeof( UniformBufferObject );

		UniformBuffers = new VulkanBuffer[MaxFramesInFlight];

		for ( var i = 0; i < MaxFramesInFlight; i++ )
		{
			UniformBuffers[i] = LogicalGpu.CreateBuffer( bufferSize, BufferUsageFlags.UniformBufferBit,
				MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit );
		}
	}

	private void CreateDescriptorPool()
	{
		ReadOnlySpan<DescriptorPoolSize> pools = stackalloc DescriptorPoolSize[]
		{
			new DescriptorPoolSize
			{
				Type = DescriptorType.UniformBuffer,
				DescriptorCount = MaxFramesInFlight
			},
			new DescriptorPoolSize
			{
				Type = DescriptorType.CombinedImageSampler,
				DescriptorCount = MaxFramesInFlight
			}
		};

		DescriptorPool = LogicalGpu.CreateDescriptorPool( pools, MaxFramesInFlight );
	}

	private void CreateCommandBuffer()
	{
		CommandBuffer* commandBuffers = stackalloc CommandBuffer[(int)MaxFramesInFlight];

		var allocateInfo = new CommandBufferAllocateInfo
		{
			SType = StructureType.CommandBufferAllocateInfo,
			CommandPool = CommandPool,
			Level = CommandBufferLevel.Primary,
			CommandBufferCount = MaxFramesInFlight
		};

		Apis.Vk.AllocateCommandBuffers( LogicalGpu, allocateInfo, commandBuffers ).Verify();

		CommandBuffers = new CommandBuffer[MaxFramesInFlight];
		for ( var i = 0; i < MaxFramesInFlight; i++ )
			CommandBuffers[i] = commandBuffers[i];
	}

	private void CreateSyncObjects()
	{
		ImageAvailableSemaphores = new VulkanSemaphore[MaxFramesInFlight];
		RenderFinishedSemaphores = new VulkanSemaphore[MaxFramesInFlight];
		InFlightFences = new VulkanFence[MaxFramesInFlight];

		for ( var i = 0; i < MaxFramesInFlight; i++ )
		{
			ImageAvailableSemaphores[i] = LogicalGpu.CreateSemaphore();
			RenderFinishedSemaphores[i] = LogicalGpu.CreateSemaphore();
			InFlightFences[i] = LogicalGpu.CreateFence( true );
		}
	}
	#endregion

	#region Utilities
	private bool IsDeviceSuitable( Gpu gpu )
	{
		var indices = gpu.GetQueueFamilyIndices();
		var extensionsSupported = gpu.SupportsExtensions( DeviceExtensions );

		var swapChainAdequate = false;
		if ( extensionsSupported )
		{
			var swapChainSupport = gpu.SwapchainSupportDetails;
			swapChainAdequate = swapChainSupport.Formats.Length > 0 && swapChainSupport.PresentModes.Length > 0;
		}

		return indices.IsComplete() && extensionsSupported && swapChainAdequate && gpu.Features.SamplerAnisotropy;
	}

	private Format FindSupportedFormat( IEnumerable<Format> candidates, ImageTiling tiling, FormatFeatureFlags features )
	{
		foreach ( var format in candidates )
		{
			var properties = Gpu.GetFormatProperties( format );

			if ( tiling == ImageTiling.Linear && (properties.LinearTilingFeatures & features) == features )
				return format;
			else if ( tiling == ImageTiling.Optimal && (properties.OptimalTilingFeatures & features) == features )
				return format;
		}

		throw new ApplicationException( "Failed to find a suitable format" );
	}

	private Format FindDepthFormat()
	{
		var formats = new Format[]
		{
			Format.D32Sfloat,
			Format.D32SfloatS8Uint,
			Format.D24UnormS8Uint
		};

		return FindSupportedFormat( formats, ImageTiling.Optimal, FormatFeatureFlags.DepthStencilAttachmentBit );
	}

	private unsafe void GetMeshGpuBuffers( Mesh mesh, out GpuBuffer<Vertex> gpuVertexBuffer, out GpuBuffer<uint>? gpuIndexBuffer )
	{
		if ( !MeshVertexBuffers.TryGetValue( mesh, out gpuVertexBuffer! ) )
		{
			gpuVertexBuffer = new GpuBuffer<Vertex>( this, mesh.Vertices.AsSpan(), BufferUsageFlags.VertexBufferBit );
			MeshVertexBuffers.TryAdd( mesh, gpuVertexBuffer );
		}

		if ( !MeshIndexBuffers.TryGetValue( mesh, out gpuIndexBuffer ) && mesh.Indices.Length > 0 )
		{
			gpuIndexBuffer = new GpuBuffer<uint>( this, mesh.Indices.AsSpan(), BufferUsageFlags.IndexBufferBit );
			MeshIndexBuffers.TryAdd( mesh, gpuIndexBuffer );
		}
	}

	private unsafe DescriptorSet[] GetTextureDescriptorSets( Texture texture, in VulkanDescriptorSetLayout descriptorSetLayout,
		in VulkanDescriptorPool descriptorPool, VulkanBuffer[] ubos, SampleCountFlags numSamples )
	{
		if ( TextureDescriptorSets.TryGetValue( texture, out var descriptorSets ) )
			return descriptorSets;

		descriptorSets = new DescriptorSet[(int)MaxFramesInFlight];

		var layouts = stackalloc DescriptorSetLayout[(int)MaxFramesInFlight];
		for ( var i = 0; i < MaxFramesInFlight; i++ )
			layouts[i] = descriptorSetLayout;

		var allocateInfo = new DescriptorSetAllocateInfo
		{
			SType = StructureType.DescriptorSetAllocateInfo,
			DescriptorPool = descriptorPool,
			DescriptorSetCount = MaxFramesInFlight,
			PSetLayouts = layouts
		};

		Apis.Vk.AllocateDescriptorSets( LogicalGpu, &allocateInfo, descriptorSets ).Verify();

		var textureImage = LogicalGpu.CreateImage( (uint)texture.Width, (uint)texture.Height, texture.MipLevels, SampleCountFlags.Count1Bit,
			Format.R8G8B8A8Srgb, ImageTiling.Optimal,
			ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
			MemoryPropertyFlags.DeviceLocalBit, ImageAspectFlags.ColorBit );

		var textureSize = (ulong)texture.Width * (ulong)texture.Height * (ulong)texture.BytesPerPixel;
		var stagingBuffer = LogicalGpu.CreateBuffer( textureSize, BufferUsageFlags.TransferSrcBit,
			MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit );
		stagingBuffer.SetMemory( texture.PixelData.Span );

		using ( var commandBuffer = LogicalGpu.BeginOneTimeCommands() )
		{
			textureImage.TransitionImageLayout( commandBuffer, Format.R8G8B8A8Srgb, ImageLayout.Undefined, ImageLayout.TransferDstOptimal, texture.MipLevels );
			textureImage.CopyBufferToImage( commandBuffer, stagingBuffer, (uint)texture.Width, (uint)texture.Height );
			textureImage.GenerateMipMaps( commandBuffer, Format.R8G8B8A8Srgb, (uint)texture.Width, (uint)texture.Height, texture.MipLevels );
		}

		var descriptorWrites = stackalloc WriteDescriptorSet[2];
		for ( var i = 0; i < MaxFramesInFlight; i++ )
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
				Sampler = LogicalGpu.CreateSampler( numSamples != SampleCountFlags.Count1Bit, texture.MipLevels )
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

			Apis.Vk.UpdateDescriptorSets( LogicalGpu, 2, descriptorWrites, 0, null );
		}

		TextureDescriptorSets.TryAdd( texture, descriptorSets );
		return descriptorSets;
	}
	#endregion
	#endregion
}

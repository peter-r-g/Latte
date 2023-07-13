using Latte.Windowing.Extensions;
using Latte.Windowing.Options;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using System;
using System.Collections.Generic;
using System.Numerics;
using Buffer = Silk.NET.Vulkan.Buffer;
using Semaphore = Silk.NET.Vulkan.Semaphore;
using Latte.Assets;
using Zio;

namespace Latte.Windowing.Backend.Vulkan;

internal unsafe class VulkanBackend : IInternalRenderingBackend
{
	public const uint MaxFramesInFlight = 2;
	private const uint ExtraSwapImages = 1;

	public IRenderingOptions Options { get; }
	public event IInternalRenderingBackend.RenderHandler? Render;
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

	private static Vk Vk => Apis.Vk;
	private static bool StaticInitialized { get; set; }
	private VulkanInstance Instance { get; set; } = null!;
	private ExtDebugUtils? DebugUtilsExtension => Instance.DebugUtilsExtension;
	private DebugUtilsMessengerEXT DebugMessenger => Instance.DebugMessenger;
	private KhrSurface SurfaceExtension => Instance.SurfaceExtension;
	private SurfaceKHR Surface => Instance.Surface;

	private IWindow Window { get; }
	private Gpu Gpu { get; set; } = null!;

	private LogicalGpu LogicalGpu { get; set; } = null!;
	private VulkanSwapchain Swapchain { get; set; } = null!;
	private Image[] SwapchainImages => Swapchain.Images;
	private ImageView[] SwapchainImageViews => Swapchain.ImageViews;
	private Format SwapchainImageFormat => Swapchain.ImageFormat;
	private Extent2D SwapchainExtent => Swapchain.Extent;
	private Framebuffer[] SwapchainFrameBuffers => Swapchain.FrameBuffers;
	private KhrSwapchain SwapchainExtension => Swapchain.Extension;

	private Shader DefaultShader { get; set; } = null!;

	private RenderPass RenderPass { get; set; }
	private DescriptorSetLayout DescriptorSetLayout { get; set; }
	private GraphicsPipeline GraphicsPipeline { get; set; } = null!;
	private PipelineLayout PipelineLayout => GraphicsPipeline.Layout;
	private CommandPool CommandPool { get; set; }
	private VulkanBuffer[] UniformBuffers { get; set; } = Array.Empty<VulkanBuffer>();
	private Dictionary<BufferUsageFlags, List<VulkanBuffer>> GpuBuffers { get; } = new();
	private Dictionary<BufferUsageFlags, List<ulong>> GpuBuffersOffsets { get; } = new();
	private DescriptorPool DescriptorPool { get; set; }
	private CommandBuffer[] CommandBuffers { get; set; } = Array.Empty<CommandBuffer>();

	private VulkanImage DepthImage { get; set; } = null!;
	private DeviceMemory DepthImageMemory => DepthImage.Memory;
	private ImageView DepthImageView => DepthImage.View;

	private VulkanImage ColorImage { get; set; } = null!;
	private DeviceMemory ColorImageMemory => ColorImage.Memory;
	private ImageView ColorImageView => ColorImage.View;

	private Semaphore[] ImageAvailableSemaphores { get; set; } = Array.Empty<Semaphore>();
	private Semaphore[] RenderFinishedSemaphores { get; set; } = Array.Empty<Semaphore>();
	private Fence[] InFlightFences { get; set; } = Array.Empty<Fence>();

	private bool HasFrameBufferResized { get; set; }

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
			if ( Vk.EnumeratePhysicalDevices( Instance, &deviceCount, null ) != Result.Success )
				throw new ApplicationException( "Failed to enumerate Vulkan physical devices (1)" );

			if ( deviceCount == 0 )
				return Array.Empty<Gpu>();

			var devices = stackalloc PhysicalDevice[(int)deviceCount];
			if ( Vk.EnumeratePhysicalDevices( Instance, &deviceCount, devices ) != Result.Success )
				throw new ApplicationException( "Failed to enumerate Vulkan physical devices (2)" );

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

		if ( EnableValidationLayers && DebugUtilsExtension is not null )
			DebugUtilsExtension.DestroyDebugUtilsMessenger( Instance, DebugMessenger, null );

		SurfaceExtension.DestroySurface( Instance, Surface, null );
		Vk.DestroyInstance( Instance, null );
		// TODO: Don't dispose of the API here, dispose when whole program is exiting.
		Vk.Dispose();
	}

	public void DrawFrame( double dt )
	{
		if ( Vk.WaitForFences( LogicalGpu, 1, InFlightFences[CurrentFrame], Vk.True, ulong.MaxValue ) != Result.Success )
			throw new ApplicationException( "Failed to wait for in flight fence" );

		uint imageIndex;
		var result = SwapchainExtension.AcquireNextImage( LogicalGpu, Swapchain, ulong.MaxValue, ImageAvailableSemaphores[CurrentFrame], default, &imageIndex );
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

		if ( Vk.ResetFences( LogicalGpu, 1, InFlightFences[CurrentFrame] ) != Result.Success )
			throw new ApplicationException( "Failed to reset in flight fence" );

		if ( Vk.ResetCommandBuffer( CommandBuffers[CurrentFrame], 0 ) != Result.Success )
			throw new ApplicationException( "Failed to reset the command buffer" );

		UpdateUniformBuffer( CurrentFrame );

		RecordCommandBuffer( CommandBuffers[CurrentFrame], imageIndex, dt );

		var submitInfo = new SubmitInfo
		{
			SType = StructureType.SubmitInfo
		};

		PipelineStageFlags* waitStages = stackalloc PipelineStageFlags[1];
		waitStages[0] = PipelineStageFlags.ColorAttachmentOutputBit;
		submitInfo.PWaitDstStageMask = waitStages;

		var buffer = CommandBuffers[CurrentFrame];
		submitInfo.CommandBufferCount = 1;
		submitInfo.PCommandBuffers = &buffer;

		var waitSemaphoreCount = 1;
		Semaphore* waitSemaphores = stackalloc Semaphore[waitSemaphoreCount];
		waitSemaphores[0] = ImageAvailableSemaphores[CurrentFrame];
		submitInfo.WaitSemaphoreCount = (uint)waitSemaphoreCount;
		submitInfo.PWaitSemaphores = waitSemaphores;

		var signalSemaphoreCount = 1;
		Semaphore* signalSemaphores = stackalloc Semaphore[signalSemaphoreCount];
		signalSemaphores[0] = RenderFinishedSemaphores[CurrentFrame];
		submitInfo.SignalSemaphoreCount = (uint)signalSemaphoreCount;
		submitInfo.PSignalSemaphores = signalSemaphores;

		if ( Vk.QueueSubmit( LogicalGpu.GraphicsQueue, 1, submitInfo, InFlightFences[CurrentFrame] ) != Result.Success )
			throw new ApplicationException( "Failed to submit command buffers to graphics queue" );

		var swapchainCount = 1;
		SwapchainKHR* swapchains = stackalloc SwapchainKHR[swapchainCount];
		swapchains[0] = Swapchain;

		var presentInfo = new PresentInfoKHR
		{
			SType = StructureType.PresentInfoKhr,
			WaitSemaphoreCount = 1,
			PWaitSemaphores = signalSemaphores,
			SwapchainCount = (uint)swapchainCount,
			PSwapchains = swapchains,
			PImageIndices = &imageIndex
		};

		result = SwapchainExtension.QueuePresent( LogicalGpu.PresentQueue, presentInfo );
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

		CurrentFrame = (CurrentFrame + 1) % MaxFramesInFlight;
	}

	public void WaitForIdle()
	{
		Vk.DeviceWaitIdle( LogicalGpu );
	}

	public void SetTexture( Texture texture )
	{
		if ( texture == CurrentTexture )
			return;

		CurrentTexture = texture;
		var descriptorSets = LogicalGpu.GetTextureDescriptorSets( this, texture, DescriptorSetLayout, DescriptorPool, UniformBuffers, Options.Msaa.ToVulkan() );
		Vk.CmdBindDescriptorSets( CurrentCommandBuffer, PipelineBindPoint.Graphics, PipelineLayout, 0, 1, descriptorSets[CurrentFrame], 0, null );
	}

	public void DrawModel( Model model ) => DrawModel( model, Vector3.Zero );
	public void DrawModel( Model model, in Vector3 position )
	{
		if ( position != CurrentModelPosition )
		{
			var pushConstants = new PushConstants( Matrix4x4.CreateTranslation( position ) );
			Vk.CmdPushConstants( CurrentCommandBuffer, PipelineLayout, ShaderStageFlags.VertexBit, 0, (uint)sizeof( PushConstants ), &pushConstants );
		}

		var vertexBuffers = stackalloc Buffer[1];
		foreach ( var mesh in model.Meshes )
		{
			LogicalGpu.GetMeshGpuBuffers( this, mesh, out var gpuVertexBuffer, out var gpuIndexBuffer );

			if ( gpuVertexBuffer.Buffer.Handle != CurrentVertexBuffer.Handle )
			{
				var newVertexBuffer = gpuVertexBuffer;
				vertexBuffers[0] = newVertexBuffer.Buffer;
				Vk.CmdBindVertexBuffers( CurrentCommandBuffer, 0, 1, vertexBuffers, newVertexBuffer.Offset );
				CurrentVertexBuffer = newVertexBuffer.Buffer;
			}
			if ( gpuIndexBuffer is not null && gpuIndexBuffer.Buffer.Handle != CurrentIndexBuffer.Handle )
			{
				var newIndexBuffer = gpuIndexBuffer;
				Vk.CmdBindIndexBuffer( CurrentCommandBuffer, newIndexBuffer.Buffer, newIndexBuffer.Offset, IndexType.Uint32 );
				CurrentIndexBuffer = newIndexBuffer.Buffer;
			}

			Vk.CmdDrawIndexed( CurrentCommandBuffer, (uint)mesh.Indices.Length, 1, 0, 0, 0 );
		}
	}

	internal void UpdateFromOptions()
	{
		if ( !Options.HasOptionsChanged() )
			return;

		WaitForIdle();

		if ( Options.HasOptionsChanged( nameof( Options.Msaa ) ) )
		{
			Vk.DestroyDescriptorPool( LogicalGpu, DescriptorPool, null );
			Vk.DestroyPipeline( LogicalGpu, GraphicsPipeline, null );
			Vk.DestroyPipelineLayout( LogicalGpu, PipelineLayout, null );
			Vk.DestroyRenderPass( LogicalGpu, RenderPass, null );
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
			Vk.DestroyPipeline( LogicalGpu, GraphicsPipeline, null );
			Vk.DestroyPipelineLayout( LogicalGpu, PipelineLayout, null );

			CreateGraphicsPipeline();
		}

		OptionsApplied?.Invoke( this );
	}

	internal CommandBuffer BeginOneTimeCommands()
	{
		var allocateInfo = new CommandBufferAllocateInfo
		{
			SType = StructureType.CommandBufferAllocateInfo,
			Level = CommandBufferLevel.Primary,
			CommandBufferCount = 1,
			CommandPool = CommandPool
		};

		if ( Vk.AllocateCommandBuffers( LogicalGpu, allocateInfo, out var commandBuffer ) != Result.Success )
			throw new ApplicationException( "Failed to allocate command buffer for one time use" );

		var beginInfo = new CommandBufferBeginInfo
		{
			SType = StructureType.CommandBufferBeginInfo,
			Flags = CommandBufferUsageFlags.OneTimeSubmitBit
		};

		if ( Vk.BeginCommandBuffer( commandBuffer, beginInfo ) != Result.Success )
			throw new ApplicationException( "Failed to begin command buffer for one time use" );

		return commandBuffer;
	}

	internal void EndOneTimeCommands( in CommandBuffer commandBuffer )
	{
		if ( Vk.EndCommandBuffer( commandBuffer ) != Result.Success )
			throw new ApplicationException( "Failed to end command buffer for one time use" );

		var commandBuffers = stackalloc CommandBuffer[]
		{
			commandBuffer
		};
		var submitInfo = new SubmitInfo
		{
			SType = StructureType.SubmitInfo,
			CommandBufferCount = 1,
			PCommandBuffers = commandBuffers
		};

		if ( Vk.QueueSubmit( LogicalGpu.GraphicsQueue, 1, submitInfo, default ) != Result.Success )
			throw new ApplicationException( "Failed to submit command buffer to queue for one time use" );

		if ( Vk.QueueWaitIdle( LogicalGpu.GraphicsQueue ) != Result.Success )
			throw new ApplicationException( "Failed to wait for queue to idle for one time use" );

		Vk.FreeCommandBuffers( LogicalGpu, CommandPool, 1, commandBuffer );
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

		CopyBuffer( stagingBuffer, buffer, bufferSize );
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
		Vk.DestroyImageView( LogicalGpu, ColorImageView, null );
		Vk.DestroyImage( LogicalGpu, ColorImage, null );
		Vk.FreeMemory( LogicalGpu, ColorImageMemory, null );

		Vk.DestroyImageView( LogicalGpu, DepthImageView, null );
		Vk.DestroyImage( LogicalGpu, DepthImage, null );
		Vk.FreeMemory( LogicalGpu, DepthImageMemory, null );

		foreach ( var frameBuffer in SwapchainFrameBuffers )
			Vk.DestroyFramebuffer( LogicalGpu, frameBuffer, null );

		foreach ( var imageView in SwapchainImageViews )
			Vk.DestroyImageView( LogicalGpu, imageView, null );

		SwapchainExtension.DestroySwapchain( LogicalGpu, Swapchain, null );
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

	private void RecordCommandBuffer( in CommandBuffer commandBuffer, uint imageIndex, double dt )
	{
		var beginInfo = new CommandBufferBeginInfo
		{
			SType = StructureType.CommandBufferBeginInfo
		};

		if ( Vk.BeginCommandBuffer( commandBuffer, beginInfo ) != Result.Success )
			throw new ApplicationException( "Failed to start recording a command buffer" );

		var renderPassInfo = new RenderPassBeginInfo
		{
			SType = StructureType.RenderPassBeginInfo,
			RenderPass = RenderPass,
			Framebuffer = SwapchainFrameBuffers[imageIndex],
			ClearValueCount = 1,
			RenderArea =
			{
				Offset = { X = 0, Y = 0 },
				Extent = SwapchainExtent
			}
		};

		var clearValues = stackalloc ClearValue[]
		{
			new ClearValue( new ClearColorValue( 0.0f, 0.0f, 0.0f, 1.0f ) ),
			new ClearValue( null, new ClearDepthStencilValue( 1, 0 ) )
		};
		renderPassInfo.ClearValueCount = 2;
		renderPassInfo.PClearValues = clearValues;

		Vk.CmdBeginRenderPass( commandBuffer, renderPassInfo, SubpassContents.Inline );
		Vk.CmdBindPipeline( commandBuffer, PipelineBindPoint.Graphics, GraphicsPipeline );

		var viewport = new Viewport()
		{
			X = 0,
			Y = 0,
			Width = SwapchainExtent.Width,
			Height = SwapchainExtent.Height,
			MinDepth = 0,
			MaxDepth = 1
		};
		Vk.CmdSetViewport( commandBuffer, 0, 1, viewport );

		var scissor = new Rect2D()
		{
			Offset = new Offset2D( 0, 0 ),
			Extent = SwapchainExtent
		};
		Vk.CmdSetScissor( commandBuffer, 0, 1, scissor );

		// User level drawing
		{
			CurrentModelPosition = Vector3.Zero;
			var defaultConstants = new PushConstants( Matrix4x4.CreateTranslation( CurrentModelPosition ) );
			Vk.CmdPushConstants( commandBuffer, PipelineLayout, ShaderStageFlags.VertexBit, 0, (uint)sizeof( PushConstants ), &defaultConstants );
			CurrentCommandBuffer = commandBuffer;

			Render?.Invoke( dt );

			CurrentTexture = null;
			CurrentIndexBuffer = default;
			CurrentVertexBuffer = default;
			CurrentCommandBuffer = default;
			CurrentModelPosition = default;
		}

		Vk.CmdEndRenderPass( commandBuffer );

		if ( Vk.EndCommandBuffer( commandBuffer ) != Result.Success )
			throw new ApplicationException( "Failed to record command buffer" );
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
		var bindings = new DescriptorSetLayoutBinding[]
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
		var dynamicStates = new DynamicState[]
		{
			DynamicState.Viewport,
			DynamicState.Scissor
		};
		var descriptorSetLayouts = new DescriptorSetLayout[]
		{
			DescriptorSetLayout
		};
		var pushConstantRanges = new PushConstantRange[]
		{
			new PushConstantRange
			{
				Offset = 0,
				Size = (uint)sizeof( PushConstants ),
				StageFlags = ShaderStageFlags.VertexBit
			}
		};

		GraphicsPipeline = LogicalGpu.CreateGraphicsPipeline( Options, DefaultShader, SwapchainExtent, RenderPass,
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
		ColorImage = LogicalGpu.CreateImage( SwapchainExtent.Width, SwapchainExtent.Height, 1, Options.Msaa.ToVulkan(),
			SwapchainImageFormat, ImageTiling.Optimal, ImageUsageFlags.TransientAttachmentBit | ImageUsageFlags.ColorAttachmentBit,
			MemoryPropertyFlags.DeviceLocalBit, ImageAspectFlags.ColorBit );
	}

	private void CreateDepthResources()
	{
		var depthFormat = FindDepthFormat();
		DepthImage = LogicalGpu.CreateImage( SwapchainExtent.Width, SwapchainExtent.Height, 1, Options.Msaa.ToVulkan(),
			depthFormat, ImageTiling.Optimal, ImageUsageFlags.DepthStencilAttachmentBit,
			MemoryPropertyFlags.DeviceLocalBit, ImageAspectFlags.DepthBit );

		var commandBuffer = BeginOneTimeCommands();

		DepthImage.TransitionImageLayout( commandBuffer, depthFormat,
			ImageLayout.Undefined, ImageLayout.DepthStencilAttachmentOptimal, 1 );

		EndOneTimeCommands( commandBuffer );
	}

	private void CreateFrameBuffers()
	{
		var useMsaa = Options.Msaa != MsaaOption.One;
		var attachments = new ImageView[useMsaa ? 3 : 2];
		attachments[0] = ColorImageView;
		attachments[1] = DepthImageView;

		Swapchain.CreateFrameBuffers( RenderPass, attachments, frameBufferIndex =>
		{
			if ( useMsaa )
				attachments[2] = SwapchainImageViews[frameBufferIndex];
			else
				attachments[0] = SwapchainImageViews[frameBufferIndex];
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
		var pools = stackalloc DescriptorPoolSize[]
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

		var poolInfo = new DescriptorPoolCreateInfo
		{
			SType = StructureType.DescriptorPoolCreateInfo,
			PoolSizeCount = 2,
			PPoolSizes = pools,
			MaxSets = MaxFramesInFlight
		};

		if ( Vk.CreateDescriptorPool( LogicalGpu, poolInfo, null, out var descriptorPool ) != Result.Success )
			throw new ApplicationException( "Failed to create Vulkan descriptor pool" );

		DescriptorPool = descriptorPool;
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

		if ( Vk.AllocateCommandBuffers( LogicalGpu, allocateInfo, commandBuffers ) != Result.Success )
			throw new ApplicationException( "Failed to create Vulkan command buffers" );

		CommandBuffers = new CommandBuffer[MaxFramesInFlight];
		for ( var i = 0; i < MaxFramesInFlight; i++ )
			CommandBuffers[i] = commandBuffers[i];
	}

	private void CreateSyncObjects()
	{
		ImageAvailableSemaphores = new Semaphore[MaxFramesInFlight];
		RenderFinishedSemaphores = new Semaphore[MaxFramesInFlight];
		InFlightFences = new Fence[MaxFramesInFlight];

		var semaphoreCreateInfo = new SemaphoreCreateInfo
		{
			SType = StructureType.SemaphoreCreateInfo
		};

		var fenceInfo = new FenceCreateInfo
		{
			SType = StructureType.FenceCreateInfo,
			Flags = FenceCreateFlags.SignaledBit
		};

		for ( var i = 0; i < MaxFramesInFlight; i++ )
		{
			if ( Vk.CreateSemaphore( LogicalGpu, semaphoreCreateInfo, null, out var imageAvailableSemaphore ) != Result.Success ||
				Vk.CreateSemaphore( LogicalGpu, semaphoreCreateInfo, null, out var renderFinishedSemaphore ) != Result.Success ||
				Vk.CreateFence( LogicalGpu, fenceInfo, null, out var inFlightFence ) != Result.Success )
				throw new ApplicationException( "Failed to create Vulkan synchronization objects" );

			ImageAvailableSemaphores[i] = imageAvailableSemaphore;
			RenderFinishedSemaphores[i] = renderFinishedSemaphore;
			InFlightFences[i] = inFlightFence;
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

	private uint FindMemoryType( uint typeFilter, MemoryPropertyFlags properties )
	{
		var memoryProperties = Gpu.MemoryProperties;
		for ( var i = 0; i < memoryProperties.MemoryTypeCount; i++ )
		{
			if ( (typeFilter & (1 << i)) != 0 && (memoryProperties.MemoryTypes[i].PropertyFlags & properties) == properties )
				return (uint)i;
		}

		throw new ApplicationException( "Failed to find suitable memory type" );
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

	private void CopyBuffer( in Buffer srcBuffer, in Buffer dstBuffer, ulong size )
	{
		var commandBuffer = BeginOneTimeCommands();

		var copyRegion = new BufferCopy
		{
			Size = size
		};

		Vk.CmdCopyBuffer( commandBuffer, srcBuffer, dstBuffer, 1, copyRegion );

		EndOneTimeCommands( commandBuffer );
	}
	#endregion
	#endregion
}

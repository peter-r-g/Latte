using Latte.Windowing.Assets;
using Latte.Windowing.Extensions;
using Latte.Windowing.Options;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.IO;
using Buffer = Silk.NET.Vulkan.Buffer;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Latte.Windowing.Backend.Vulkan;

internal unsafe class VulkanBackend : IInternalRenderingBackend
{
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
	private const uint ExtraSwapImages = 1;
	private const uint MaxFramesInFlight = 2;

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
	private DescriptorSet[] DescriptorSets { get; set; } = Array.Empty<DescriptorSet>();
	private CommandBuffer[] CommandBuffers { get; set; } = Array.Empty<CommandBuffer>();

	private uint MipLevels { get; set; }
	private Image TextureImage { get; set; }
	private DeviceMemory TextureImageMemory { get; set; }
	private ImageView TextureImageView { get; set; }
	private Sampler TextureSampler { get; set; }

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
		CreateTextureImage();
		CreateTextureImageView();
		CreateTextureSampler();
		CreateUniformBuffers();
		CreateDescriptorPool();
		CreateDescriptorSets();
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
			if ( mesh.VulkanVertexBuffer is null )
				throw new NullReferenceException( "Models mesh vertex buffer is null. Was this model created with the Vulkan backend?" );

			if ( mesh.VulkanVertexBuffer.Buffer.Handle != CurrentVertexBuffer.Handle )
			{
				var newVertexBuffer = mesh.VulkanVertexBuffer;
				vertexBuffers[0] = newVertexBuffer.Buffer;
				Vk.CmdBindVertexBuffers( CurrentCommandBuffer, 0, 1, vertexBuffers, newVertexBuffer.Offset );
				CurrentVertexBuffer = newVertexBuffer.Buffer;
			}
			if ( mesh.VulkanIndexBuffer is not null && mesh.VulkanIndexBuffer.Buffer.Handle != CurrentIndexBuffer.Handle )
			{
				var newIndexBuffer = mesh.VulkanIndexBuffer;
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
			Vk.DestroySampler( LogicalGpu, TextureSampler, null );
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
			CreateTextureSampler();
			CreateDescriptorPool();
			CreateDescriptorSets();
		}
		else if ( Options.HasOptionsChanged( nameof( Options.WireframeEnabled ) ) )
		{
			Vk.DestroyPipeline( LogicalGpu, GraphicsPipeline, null );
			Vk.DestroyPipelineLayout( LogicalGpu, PipelineLayout, null );

			CreateGraphicsPipeline();
		}

		OptionsApplied?.Invoke( this );
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

		void* dataPtr;
		if ( Vk.MapMemory( LogicalGpu, stagingBuffer.Memory, 0, bufferSize, 0, &dataPtr ) != Result.Success )
			throw new ApplicationException( "Failed to map staging buffer memory" );

		data.CopyTo( new Span<T>( dataPtr, data.Length ) );
		Vk.UnmapMemory( LogicalGpu, stagingBuffer.Memory );

		CopyBuffer( stagingBuffer, buffer, bufferSize );
	}

	internal ShaderModule CreateShaderModule( in ReadOnlySpan<byte> shaderCode )
	{
		var createInfo = new ShaderModuleCreateInfo
		{
			SType = StructureType.ShaderModuleCreateInfo,
			CodeSize = (nuint)shaderCode.Length
		};

		ShaderModule shaderModule;
		fixed ( byte* shaderCodePtr = shaderCode )
		{
			createInfo.PCode = (uint*)shaderCodePtr;

			if ( Vk.CreateShaderModule( LogicalGpu, createInfo, null, out shaderModule ) != Result.Success )
				throw new ApplicationException( "Failed to create Vulkan shader module" );
		}

		return shaderModule;
	}
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

	private void CleanupLogicalDevice()
	{
		CleanupSwapChain();

		Vk.DestroyDescriptorPool( LogicalGpu, DescriptorPool, null );
		Vk.DestroyDescriptorSetLayout( LogicalGpu, DescriptorSetLayout, null );
		Vk.DestroyPipeline( LogicalGpu, GraphicsPipeline, null );
		Vk.DestroyPipelineLayout( LogicalGpu, PipelineLayout, null );
		Vk.DestroyRenderPass( LogicalGpu, RenderPass, null );

		for ( var i = 0; i < MaxFramesInFlight; i++ )
		{
			Vk.DestroySemaphore( LogicalGpu, ImageAvailableSemaphores[i], null );
			Vk.DestroySemaphore( LogicalGpu, RenderFinishedSemaphores[i], null );
			Vk.DestroyFence( LogicalGpu, InFlightFences[i], null );
		}

		for ( var i = 0; i < MaxFramesInFlight; i++ )
			Vk.DestroyBuffer( LogicalGpu, UniformBuffers[i], null );

		foreach ( var (_, buffers) in GpuBuffers )
		{
			for ( var i = 0; i < buffers.Count; i++ )
				Vk.DestroyBuffer( LogicalGpu, buffers[i], null );
		}
		GpuBuffers.Clear();
		GpuBuffersOffsets.Clear();

		Vk.DestroySampler( LogicalGpu, TextureSampler, null );
		Vk.DestroyImageView( LogicalGpu, TextureImageView, null );
		Vk.DestroyImage( LogicalGpu, TextureImage, null );
		Vk.FreeMemory( LogicalGpu, TextureImageMemory, null );

		Vk.DestroyCommandPool( LogicalGpu, CommandPool, null );
		Vk.DestroyShaderModule( LogicalGpu, DefaultShader.VertexShaderModule, null );
		Vk.DestroyShaderModule( LogicalGpu, DefaultShader.FragmentShaderModule, null );
		Vk.DestroyDevice( LogicalGpu, null );
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

		void* data;
		if ( Vk.MapMemory( LogicalGpu, UniformBuffers[currentImage].Memory, 0, (ulong)sizeof( UniformBufferObject ), 0, &data ) != Result.Success )
			throw new ApplicationException( "Failed to map memory of the UBO" );

		new Span<UniformBufferObject>( data, 1 )[0] = ubo;

		Vk.UnmapMemory( LogicalGpu, UniformBuffers[currentImage].Memory );
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

		Vk.CmdBindDescriptorSets( commandBuffer, PipelineBindPoint.Graphics, PipelineLayout, 0, 1, DescriptorSets[CurrentFrame], 0, null );

		// User level drawing
		{
			CurrentModelPosition = Vector3.Zero;
			var defaultConstants = new PushConstants( Matrix4x4.CreateTranslation( CurrentModelPosition ) );
			Vk.CmdPushConstants( commandBuffer, PipelineLayout, ShaderStageFlags.VertexBit, 0, (uint)sizeof( PushConstants ), &defaultConstants );
			CurrentCommandBuffer = commandBuffer;

			Render?.Invoke( dt );

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
			Path.Combine( "Assets", "Shaders", "vert.spv" ),
			Path.Combine( "Assets", "Shaders", "frag.spv" )
			);
		DefaultShader.Initialize( this );
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
		var bindingDescriptions = Vertex.GetBindingDescriptions();
		var attributeDescriptions = Vertex.GetAttributeDescriptions();
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

	private void CreateTextureImage()
	{
		// FIXME: Remove this
		using var image = SixLabors.ImageSharp.Image.Load<Rgba32>( Path.Combine( "Assets", "Textures", "viking_room.png" ) );
		var imageSize = (ulong)(image.Width * image.Height * image.PixelType.BitsPerPixel / 8);
		MipLevels = (uint)MathF.Floor( MathF.Log2( Math.Max( image.Width, image.Height ) ) ) + 1;

		using var stagingBuffer = LogicalGpu.CreateBuffer( imageSize, BufferUsageFlags.TransferSrcBit,
			MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit );

		void* data;
		if ( Vk.MapMemory( LogicalGpu, stagingBuffer.Memory, 0, imageSize, 0, &data ) != Result.Success )
			throw new ApplicationException( "Failed to map memory for texture loading" );

		image.CopyPixelDataTo( new Span<Rgba32>( data, (int)imageSize ) );

		Vk.UnmapMemory( LogicalGpu, stagingBuffer.Memory );

		CreateImage( (uint)image.Width, (uint)image.Height, MipLevels, SampleCountFlags.Count1Bit, Format.R8G8B8A8Srgb,
			ImageTiling.Optimal, ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
			MemoryPropertyFlags.DeviceLocalBit,
			out var textureImage, out var textureImageMemory );

		TextureImage = textureImage;
		TextureImageMemory = textureImageMemory;

		TransitionImageLayout( textureImage, Format.R8G8B8A8Srgb, ImageLayout.Undefined, ImageLayout.TransferDstOptimal, MipLevels );
		CopyBufferToImage( stagingBuffer, textureImage, (uint)image.Width, (uint)image.Height );
		GenerateMipMaps( textureImage, Format.R8G8B8A8Srgb, (uint)image.Width, (uint)image.Height, MipLevels );
	}

	private void CreateTextureImageView()
	{
		TextureImageView = CreateImageView( TextureImage, Format.R8G8B8A8Srgb, ImageAspectFlags.ColorBit, MipLevels );
	}

	private void CreateTextureSampler()
	{
		TextureSampler = LogicalGpu.CreateTextureSampler( Options.Msaa != MsaaOption.One, MipLevels );
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
			new DescriptorPoolSize()
			{
				Type = DescriptorType.UniformBuffer,
				DescriptorCount = MaxFramesInFlight
			},
			new DescriptorPoolSize()
			{
				Type = DescriptorType.CombinedImageSampler,
				DescriptorCount = MaxFramesInFlight
			}
		};

		var poolInfo = new DescriptorPoolCreateInfo()
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

	private void CreateDescriptorSets()
	{
		var descriptorSets = stackalloc DescriptorSet[(int)MaxFramesInFlight];

		var layouts = stackalloc DescriptorSetLayout[(int)MaxFramesInFlight];
		for ( var i = 0; i < MaxFramesInFlight; i++ )
			layouts[i] = DescriptorSetLayout;

		var allocateInfo = new DescriptorSetAllocateInfo()
		{
			SType = StructureType.DescriptorSetAllocateInfo,
			DescriptorPool = DescriptorPool,
			DescriptorSetCount = MaxFramesInFlight,
			PSetLayouts = layouts
		};

		if ( Vk.AllocateDescriptorSets( LogicalGpu, allocateInfo, descriptorSets ) != Result.Success )
			throw new ApplicationException( "Failed to allocate Vulkan descriptor sets" );

		DescriptorSets = new DescriptorSet[MaxFramesInFlight];
		for ( var i = 0; i < MaxFramesInFlight; i++ )
			DescriptorSets[i] = descriptorSets[i];

		var descriptorWrites = stackalloc WriteDescriptorSet[2];
		for ( var i = 0; i < MaxFramesInFlight; i++ )
		{
			var bufferInfo = new DescriptorBufferInfo()
			{
				Buffer = UniformBuffers[i],
				Offset = 0,
				Range = (ulong)sizeof( UniformBufferObject )
			};

			var imageInfo = new DescriptorImageInfo()
			{
				ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
				ImageView = TextureImageView,
				Sampler = TextureSampler
			};

			var uboWrite = new WriteDescriptorSet()
			{
				SType = StructureType.WriteDescriptorSet,
				DstSet = DescriptorSets[i],
				DstBinding = 0,
				DstArrayElement = 0,
				DescriptorType = DescriptorType.UniformBuffer,
				DescriptorCount = 1,
				PBufferInfo = &bufferInfo
			};
			descriptorWrites[0] = uboWrite;

			var imageWrite = new WriteDescriptorSet()
			{
				SType = StructureType.WriteDescriptorSet,
				DstSet = DescriptorSets[i],
				DstBinding = 1,
				DstArrayElement = 0,
				DescriptorType = DescriptorType.CombinedImageSampler,
				DescriptorCount = 1,
				PImageInfo = &imageInfo
			};
			descriptorWrites[1] = imageWrite;

			Vk.UpdateDescriptorSets( LogicalGpu, 2, descriptorWrites, 0, null );
		}
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

	private CommandBuffer BeginOneTimeCommands()
	{
		var allocateInfo = new CommandBufferAllocateInfo()
		{
			SType = StructureType.CommandBufferAllocateInfo,
			Level = CommandBufferLevel.Primary,
			CommandBufferCount = 1,
			CommandPool = CommandPool
		};

		if ( Vk.AllocateCommandBuffers( LogicalGpu, allocateInfo, out var commandBuffer ) != Result.Success )
			throw new ApplicationException( "Failed to allocate command buffer for one time use" );

		var beginInfo = new CommandBufferBeginInfo()
		{
			SType = StructureType.CommandBufferBeginInfo,
			Flags = CommandBufferUsageFlags.OneTimeSubmitBit
		};

		if ( Vk.BeginCommandBuffer( commandBuffer, beginInfo ) != Result.Success )
			throw new ApplicationException( "Failed to begin command buffer for one time use" );

		return commandBuffer;
	}

	private void EndOneTimeCommands( in CommandBuffer commandBuffer )
	{
		if ( Vk.EndCommandBuffer( commandBuffer ) != Result.Success )
			throw new ApplicationException( "Failed to end command buffer for one time use" );

		var commandBuffers = stackalloc CommandBuffer[]
		{
			commandBuffer
		};
		var submitInfo = new SubmitInfo()
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

	private static bool HasStencilComponent( Format format )
	{
		return format == Format.D32Sfloat || format == Format.D24UnormS8Uint;
	}

	private void CreateImage( uint width, uint height, uint mipLevels, SampleCountFlags numSamples,
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

		if ( Vk.CreateImage( LogicalGpu, imageInfo, null, out image ) != Result.Success )
			throw new ApplicationException( "Failed to create image" );

		var requirements = Vk.GetImageMemoryRequirements( LogicalGpu, image );

		var allocateInfo = new MemoryAllocateInfo()
		{
			SType = StructureType.MemoryAllocateInfo,
			AllocationSize = requirements.Size,
			MemoryTypeIndex = FindMemoryType( requirements.MemoryTypeBits, memoryPropertyFlags )
		};

		if ( Vk.AllocateMemory( LogicalGpu, allocateInfo, null, out imageMemory ) != Result.Success )
			throw new ApplicationException( "Failed to allocate image memory" );

		if ( Vk.BindImageMemory( LogicalGpu, image, imageMemory, 0 ) != Result.Success )
			throw new ApplicationException( "Failed to bind image memory" );
	}

	private ImageView CreateImageView( in Image image, Format format, ImageAspectFlags aspectFlags, uint mipLevels )
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

		if ( Vk.CreateImageView( LogicalGpu, viewInfo, null, out var imageView ) != Result.Success )
			throw new ApplicationException( "Failed to create Vulkan texture image view" );

		return imageView;
	}

	private void TransitionImageLayout( in Image image, Format format,
		ImageLayout oldLayout, ImageLayout newLayout, uint mipLevels )
	{
		var commandBuffer = BeginOneTimeCommands();

		var barrier = new ImageMemoryBarrier()
		{
			SType = StructureType.ImageMemoryBarrier,
			OldLayout = oldLayout,
			NewLayout = newLayout,
			SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
			DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
			Image = image,
			SubresourceRange =
			{
				AspectMask = ImageAspectFlags.ColorBit,
				BaseMipLevel = 0,
				LevelCount = mipLevels,
				BaseArrayLayer = 0,
				LayerCount = 1
			}
		};

		if ( newLayout == ImageLayout.DepthStencilAttachmentOptimal )
		{
			barrier.SubresourceRange.AspectMask = ImageAspectFlags.DepthBit;

			// FIXME: Adding this causes a validation error.
			/*if ( HasStencilComponent( format ) )
				barrier.SubresourceRange.AspectMask |= ImageAspectFlags.StencilBit;*/
		}
		else
			barrier.SubresourceRange.AspectMask = ImageAspectFlags.ColorBit;

		PipelineStageFlags sourceStage;
		PipelineStageFlags destinationStage;

		if ( oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.TransferDstOptimal )
		{
			barrier.SrcAccessMask = 0;
			barrier.DstAccessMask = AccessFlags.TransferWriteBit;

			sourceStage = PipelineStageFlags.TopOfPipeBit;
			destinationStage = PipelineStageFlags.TransferBit;
		}
		else if ( oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal )
		{
			barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
			barrier.DstAccessMask = AccessFlags.ShaderReadBit;

			sourceStage = PipelineStageFlags.TransferBit;
			destinationStage = PipelineStageFlags.FragmentShaderBit;
		}
		else if ( oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.DepthStencilAttachmentOptimal )
		{
			barrier.SrcAccessMask = 0;
			barrier.DstAccessMask = AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit;

			sourceStage = PipelineStageFlags.TopOfPipeBit;
			destinationStage = PipelineStageFlags.EarlyFragmentTestsBit;
		}
		else
			throw new ArgumentException( "Received unsupported layout transition", $"{nameof( oldLayout )}, {nameof( newLayout )}" );

		Vk.CmdPipelineBarrier( commandBuffer, sourceStage, destinationStage, 0,
			0, null,
			0, null,
			1, barrier );

		EndOneTimeCommands( commandBuffer );
	}

	private void CopyBuffer( in Buffer srcBuffer, in Buffer dstBuffer, ulong size )
	{
		var commandBuffer = BeginOneTimeCommands();

		var copyRegion = new BufferCopy()
		{
			Size = size
		};

		Vk.CmdCopyBuffer( commandBuffer, srcBuffer, dstBuffer, 1, copyRegion );

		EndOneTimeCommands( commandBuffer );
	}

	private void CopyBufferToImage( in Buffer buffer, in Image image, uint width, uint height )
	{
		var commandBuffer = BeginOneTimeCommands();

		var region = new BufferImageCopy()
		{
			BufferOffset = 0,
			BufferRowLength = 0,
			BufferImageHeight = 0,
			ImageOffset = new Offset3D( 0, 0, 0 ),
			ImageExtent =
			{
				Width = width,
				Height = height,
				Depth = 1
			},
			ImageSubresource =
			{
				AspectMask = ImageAspectFlags.ColorBit,
				MipLevel = 0,
				BaseArrayLayer = 0,
				LayerCount = 1
			}
		};

		Vk.CmdCopyBufferToImage( commandBuffer, buffer, image, ImageLayout.TransferDstOptimal, 1, region );

		EndOneTimeCommands( commandBuffer );
	}

	private void GenerateMipMaps( in Image image, Format format, uint width, uint height, uint mipLevels )
	{
		var formatProperties = Gpu.GetFormatProperties( format );
		if ( !formatProperties.OptimalTilingFeatures.HasFlag( FormatFeatureFlags.SampledImageFilterLinearBit ) )
			throw new ApplicationException( "Texture image format does not support linear blitting" );

		var commandBuffer = BeginOneTimeCommands();

		var barrier = new ImageMemoryBarrier()
		{
			SType = StructureType.ImageMemoryBarrier,
			Image = image,
			SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
			DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
			SubresourceRange =
			{
				AspectMask = ImageAspectFlags.ColorBit,
				BaseArrayLayer = 0,
				LayerCount = 1,
				LevelCount = 1,
			}
		};

		var mipWidth = (int)width;
		var mipHeight = (int)height;
		for ( uint i = 1; i < mipLevels; i++ )
		{
			barrier.SubresourceRange.BaseMipLevel = i - 1;
			barrier.OldLayout = ImageLayout.TransferDstOptimal;
			barrier.NewLayout = ImageLayout.TransferSrcOptimal;
			barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
			barrier.DstAccessMask = AccessFlags.TransferReadBit;

			Vk.CmdPipelineBarrier( commandBuffer,
				PipelineStageFlags.TransferBit, PipelineStageFlags.TransferBit, 0,
				0, null,
				0, null,
				1, barrier );

			var blit = new ImageBlit()
			{
				SrcSubresource =
				{
					AspectMask = ImageAspectFlags.ColorBit,
					MipLevel = i - 1,
					BaseArrayLayer = 0,
					LayerCount = 1
				},
				DstSubresource =
				{
					AspectMask = ImageAspectFlags.ColorBit,
					MipLevel = i,
					BaseArrayLayer = 0,
					LayerCount = 1
				}
			};
			blit.SrcOffsets[0] = new Offset3D( 0, 0, 0 );
			blit.SrcOffsets[1] = new Offset3D( mipWidth, mipHeight, 1 );
			blit.DstOffsets[0] = new Offset3D( 0, 0, 0 );
			blit.DstOffsets[1] = new Offset3D(
				mipWidth > 1 ? mipWidth / 2 : 1,
				mipHeight > 1 ? mipHeight / 2 : 1,
				1 );

			Vk.CmdBlitImage( commandBuffer,
				image, ImageLayout.TransferSrcOptimal,
				image, ImageLayout.TransferDstOptimal,
				1, blit, Filter.Linear );

			barrier.OldLayout = ImageLayout.TransferSrcOptimal;
			barrier.NewLayout = ImageLayout.ShaderReadOnlyOptimal;
			barrier.SrcAccessMask = AccessFlags.TransferReadBit;
			barrier.DstAccessMask = AccessFlags.ShaderReadBit;

			Vk.CmdPipelineBarrier( commandBuffer,
				PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit, 0,
				0, null,
				0, null,
				1, barrier );

			if ( mipWidth > 1 ) mipWidth /= 2;
			if ( mipHeight > 1 ) mipHeight /= 2;
		}

		barrier.SubresourceRange.BaseMipLevel = MipLevels - 1;
		barrier.OldLayout = ImageLayout.TransferDstOptimal;
		barrier.NewLayout = ImageLayout.ShaderReadOnlyOptimal;
		barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
		barrier.DstAccessMask = AccessFlags.ShaderReadBit;

		Vk.CmdPipelineBarrier( commandBuffer,
			PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit, 0,
			0, null,
			0, null,
			1, barrier );

		EndOneTimeCommands( commandBuffer );
	}
	#endregion
	#endregion
}

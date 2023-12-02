using Latte.Assets;
using Latte.NewRenderer.Builders;
using Latte.NewRenderer.Extensions;
using Latte.NewRenderer.Temp;
using Latte.NewRenderer.Wrappers;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mesh = Latte.NewRenderer.Temp.Mesh;

namespace Latte.NewRenderer;

internal unsafe sealed class VkEngine : IDisposable
{
	internal bool IsInitialized { get; private set; }

	private IView? view;
	private Instance instance;
	private DebugUtilsMessengerEXT debugMessenger;
	private PhysicalDevice physicalDevice;
	private VkPhysicalDeviceSelector? physicalDeviceSelector;
	private Device logicalDevice;
	private SurfaceKHR surface;

	private SwapchainKHR swapchain;
	private Format swapchainImageFormat;
	private ImmutableArray<Image> swapchainImages = [];
	private ImmutableArray<ImageView> swapchainImageViews = [];

	private AllocatedImage depthImage;
	private Format depthFormat;
	private ImageView depthImageView;

	private Queue graphicsQueue;
	private uint graphicsQueueFamily;
	private CommandPool graphicsCommandPool;
	private CommandBuffer graphicsCommandBuffer;
	private Queue presentQueue;
	private uint presentQueueFamily;

	private RenderPass renderPass;
	private ImmutableArray<Framebuffer> framebuffers;

	private Semaphore presentSemaphore;
	private Semaphore renderSemaphore;
	private Fence renderFence;

	private PipelineLayout trianglePipelineLayout;
	private PipelineLayout meshPipelineLayout;
	internal Pipeline coloredTrianglePipeline;
	internal Pipeline redTrianglePipeline;
	internal Pipeline meshPipeline;
	internal Pipeline currentPipeline;

	//private Mesh? triangleMesh;
	private Mesh? monkeyMesh;
	private int frameNumber;

	private readonly Stack<Action> deletionQueue = [];
	private ExtDebugUtils? debugUtilsExtension;
	private KhrSurface? surfaceExtension;
	private KhrSwapchain? swapchainExtension;
	private bool disposed;

	~VkEngine()
	{
		Dispose( disposing: false );
	}

	internal void Initialize( IView view )
	{
		this.view = view;

		InitializeVulkan();
		InitializeSwapchain();
		InitializeCommands();
		InitializeDefaultRenderPass();
		InitializeFramebuffers();
		InitializeSynchronizationStructures();
		InitializePipelines();
		LoadMeshes();

		IsInitialized = true;
	}

	internal void Draw()
	{
		ArgumentNullException.ThrowIfNull( this.view, nameof( VkEngine.view ) );
		ArgumentNullException.ThrowIfNull( swapchainExtension, nameof( swapchainExtension ) );
		//ArgumentNullException.ThrowIfNull( triangleMesh, nameof( triangleMesh ) );
		ArgumentNullException.ThrowIfNull( monkeyMesh, nameof( monkeyMesh ) );

		Apis.Vk.WaitForFences( logicalDevice, 1, renderFence, true, 1_000_000_000 ).Verify();
		Apis.Vk.ResetFences( logicalDevice, 1, renderFence );

		var swapchain = this.swapchain;
		var presentSemaphore = this.presentSemaphore;
		var renderSemaphore = this.renderSemaphore;
		var cmd = graphicsCommandBuffer;

		uint swapchainImageIndex;
		swapchainExtension.AcquireNextImage( logicalDevice, swapchain, 1_000_000_000, presentSemaphore, default, &swapchainImageIndex );

		Apis.Vk.ResetCommandBuffer( cmd, CommandBufferResetFlags.None ).Verify();

		var beginInfo = VkInfo.BeginCommandBuffer( CommandBufferUsageFlags.OneTimeSubmitBit );
		Apis.Vk.BeginCommandBuffer( cmd, beginInfo ).Verify();

		ClearValue clearValue = default;
		var flash = MathF.Abs( MathF.Sin( frameNumber / 120f ) );
		clearValue.Color = new ClearColorValue( 0, 0, flash, 1 );

		var renderArea = new Rect2D( new Offset2D( 0, 0 ), new Extent2D( (uint)this.view.Size.X, (uint)this.view.Size.Y ) );
		var rpBeginInfo = new RenderPassBeginInfo
		{
			SType = StructureType.RenderPassBeginInfo,
			PNext = null,
			RenderPass = renderPass,
			RenderArea = renderArea,
			Framebuffer = framebuffers[(int)swapchainImageIndex],
			ClearValueCount = 1,
			PClearValues = &clearValue
		};

		Apis.Vk.CmdBeginRenderPass( cmd, rpBeginInfo, SubpassContents.Inline );

		var model = Matrix4x4.Identity * Matrix4x4.CreateRotationY( frameNumber * 0.1f, Vector3.UnitY );
		var view = Matrix4x4.CreateLookAt( new Vector3( 0, 0, -2 ), Vector3.Zero, Vector3.UnitY );
		var projection = Matrix4x4.CreatePerspectiveFieldOfView( 70 * (float)Math.PI / 180, (float)this.view.Size.X / this.view.Size.Y, 0.1f, 200.0f );
		projection.M22 *= -1;
		var pushConstants = new MeshPushConstants( Vector4.Zero, model * view * projection );
		Apis.Vk.CmdPushConstants( cmd, meshPipelineLayout, ShaderStageFlags.VertexBit, 0, (uint)Unsafe.SizeOf<MeshPushConstants>(), &pushConstants );

		Apis.Vk.CmdBindPipeline( cmd, PipelineBindPoint.Graphics, meshPipeline );
		Apis.Vk.CmdBindVertexBuffers( cmd, 0, 1, monkeyMesh.VertexBuffer.Buffer, 0 );
		Apis.Vk.CmdBindIndexBuffer( cmd, monkeyMesh.IndexBuffer.Buffer, 0, IndexType.Uint32 );
		Apis.Vk.CmdDrawIndexed( cmd, (uint)monkeyMesh.Indices.Length, 1, 0, 0, 0 );

		Apis.Vk.CmdEndRenderPass( cmd );

		Apis.Vk.EndCommandBuffer( cmd ).Verify();

		var waitStage = PipelineStageFlags.ColorAttachmentOutputBit;
		var submitInfo = new SubmitInfo
		{
			SType = StructureType.SubmitInfo,
			PNext = null,
			PWaitDstStageMask = &waitStage,
			WaitSemaphoreCount = 1,
			PWaitSemaphores = &presentSemaphore,
			SignalSemaphoreCount = 1,
			PSignalSemaphores = &renderSemaphore,
			CommandBufferCount = 1,
			PCommandBuffers = &cmd,
		};

		Apis.Vk.QueueSubmit( graphicsQueue, 1, submitInfo, renderFence ).Verify();

		var presentInfo = new PresentInfoKHR
		{
			SType = StructureType.PresentInfoKhr,
			PNext = null,
			SwapchainCount = 1,
			PSwapchains = &swapchain,
			WaitSemaphoreCount = 1,
			PWaitSemaphores = &renderSemaphore,
			PImageIndices = &swapchainImageIndex
		};

		swapchainExtension.QueuePresent( presentQueue, presentInfo ).Verify();

		frameNumber++;
	}

	internal void WaitForIdle()
	{
		Apis.Vk.DeviceWaitIdle( logicalDevice ).Verify();
	}

	private void InitializeVulkan()
	{
		ArgumentNullException.ThrowIfNull( view, nameof( view ) );

		var instanceBuilderResult = new VkInstanceBuilder()
			.WithName( "Example" )
			.WithView( view )
			.RequireVulkanVersion( 1, 1, 0 )
			.UseDefaultDebugMessenger()
			.Build();

		instance = instanceBuilderResult.Instance.Validate();
		debugMessenger = instanceBuilderResult.DebugMessenger.Validate();
		debugUtilsExtension = instanceBuilderResult.DebugUtilsExtension;

		if ( !Apis.Vk.TryGetInstanceExtension<KhrSurface>( instance, out var surfaceExtension ) )
			throw new ApplicationException( "Failed to get KHR_surface extension" );

		this.surfaceExtension = surfaceExtension;
		surface = view.VkSurface!.Create<AllocationCallbacks>( instance.ToHandle(), null ).ToSurface();

		physicalDeviceSelector = new VkPhysicalDeviceSelector( instance )
			.RequireDiscreteDevice( true )
			.RequireVersion( 1, 1, 0 )
			.WithSurface( surface, surfaceExtension )
			.RequireUniqueGraphicsQueue( true )
			.RequireUniquePresentQueue( true );
		physicalDevice = physicalDeviceSelector.Select().Validate();

		var logicalDeviceBuilderResult = VkLogicalDeviceBuilder.FromPhysicalSelector( physicalDevice, physicalDeviceSelector )
			.WithExtensions( KhrSwapchain.ExtensionName )
			.Build();
		logicalDevice = logicalDeviceBuilderResult.LogicalDevice.Validate();
		graphicsQueue = logicalDeviceBuilderResult.GraphicsQueue.Validate();
		graphicsQueueFamily = logicalDeviceBuilderResult.GraphicsQueueFamily;
		presentQueue = logicalDeviceBuilderResult.PresentQueue.Validate();
		presentQueueFamily = logicalDeviceBuilderResult.PresentQueueFamily;

		deletionQueue.Push( () => Apis.Vk.DestroyInstance( instance, null ) );
		deletionQueue.Push( () => debugUtilsExtension?.DestroyDebugUtilsMessenger( instance, debugMessenger, null ) );
		deletionQueue.Push( () => surfaceExtension.DestroySurface( instance, surface, null ) );
		deletionQueue.Push( () => Apis.Vk.DestroyDevice( logicalDevice, null ) );
	}

	private void InitializeSwapchain()
	{
		ArgumentNullException.ThrowIfNull( view, nameof( view ) );
		ArgumentNullException.ThrowIfNull( physicalDeviceSelector, nameof( physicalDeviceSelector ) );

		var result = VkSwapchainBuilder.FromPhysicalSelector( physicalDevice, logicalDevice, physicalDeviceSelector )
			.UseDefaultFormat()
			.SetPresentMode( PresentModeKHR.FifoKhr )
			.SetExtent( (uint)view.Size.X, (uint)view.Size.Y )
			.Build();

		swapchain = result.Swapchain;
		swapchainExtension = result.SwapchainExtension;
		swapchainImages = result.SwapchainImages;
		swapchainImageViews = result.SwapchainImageViews;
		swapchainImageFormat = result.SwapchainImageFormat;

		deletionQueue.Push( () => swapchainExtension.DestroySwapchain( logicalDevice, swapchain, null ) );
		for ( var i = 0; i < swapchainImageViews.Length; i++ )
		{
			var index = i;
			deletionQueue.Push( () => Apis.Vk.DestroyImageView( logicalDevice, swapchainImageViews[index], null ) );
		}
	}

	private void InitializeCommands()
	{
		var poolCreateInfo = VkInfo.CommandPool( graphicsQueueFamily, CommandPoolCreateFlags.ResetCommandBufferBit );
		Apis.Vk.CreateCommandPool( logicalDevice, poolCreateInfo, null, out var commandPool ).Verify();
		graphicsCommandPool = commandPool.Validate();

		var bufferAllocateInfo = VkInfo.AllocateCommandBuffer( commandPool, 1, CommandBufferLevel.Primary );
		Apis.Vk.AllocateCommandBuffers( logicalDevice, bufferAllocateInfo, out var commandBuffer ).Verify();
		graphicsCommandBuffer = commandBuffer.Validate();

		deletionQueue.Push( () => Apis.Vk.DestroyCommandPool( logicalDevice, graphicsCommandPool, null ) );
	}

	private void InitializeDefaultRenderPass()
	{
		var colorAttachment = new AttachmentDescription
		{
			Format = swapchainImageFormat,
			Samples = SampleCountFlags.Count1Bit,
			LoadOp = AttachmentLoadOp.Clear,
			StoreOp = AttachmentStoreOp.Store,
			StencilLoadOp = AttachmentLoadOp.DontCare,
			StencilStoreOp = AttachmentStoreOp.DontCare,
			InitialLayout = ImageLayout.Undefined,
			FinalLayout = ImageLayout.PresentSrcKhr
		};

		var colorAttachmentReference = new AttachmentReference
		{
			Attachment = 0,
			Layout = ImageLayout.ColorAttachmentOptimal
		};

		var subpassDescription = new SubpassDescription
		{
			PipelineBindPoint = PipelineBindPoint.Graphics,
			ColorAttachmentCount = 1,
			PColorAttachments = &colorAttachmentReference
		};

		var createInfo = VkInfo.RenderPass( new ReadOnlySpan<AttachmentDescription>( ref colorAttachment ),
			new ReadOnlySpan<SubpassDescription>( ref subpassDescription ) );

		Apis.Vk.CreateRenderPass( logicalDevice, createInfo, null, out var renderPass ).Verify();
		this.renderPass = renderPass.Validate();
		deletionQueue.Push( () => Apis.Vk.DestroyRenderPass( logicalDevice, renderPass, null ) );
	}

	private void InitializeFramebuffers()
	{
		ArgumentNullException.ThrowIfNull( view, nameof( view ) );

		var framebufferBuilder = ImmutableArray.CreateBuilder<Framebuffer>( swapchainImages.Length );
		var ptr = Marshal.AllocHGlobal( sizeof( ImageView ) );

		var createInfo = VkInfo.Framebuffer( renderPass, (uint)view.Size.X, (uint)view.Size.Y );
		for ( var i = 0; i < swapchainImages.Length; i++ )
		{
			Marshal.StructureToPtr( swapchainImageViews[i], ptr, false );
			createInfo.PAttachments = (ImageView*)ptr;
			Apis.Vk.CreateFramebuffer( logicalDevice, createInfo, null, out var framebuffer ).Verify();

			framebufferBuilder.Add( framebuffer.Validate() );
			deletionQueue.Push( () => Apis.Vk.DestroyFramebuffer( logicalDevice, framebuffer, null ) );
		}

		Marshal.FreeHGlobal( ptr );
		framebuffers = framebufferBuilder.MoveToImmutable();
	}

	private void InitializeSynchronizationStructures()
	{
		var fenceCreateInfo = VkInfo.Fence( FenceCreateFlags.SignaledBit );
		Apis.Vk.CreateFence( logicalDevice, fenceCreateInfo, null, out var renderFence ).Verify();
		this.renderFence = renderFence.Validate();

		var semaphoreCreateInfo = VkInfo.Semaphore();
		Apis.Vk.CreateSemaphore( logicalDevice, semaphoreCreateInfo, null, out var presentSemaphore ).Verify();
		this.presentSemaphore = presentSemaphore.Validate();
		Apis.Vk.CreateSemaphore( logicalDevice, semaphoreCreateInfo, null, out var renderSemaphore ).Verify();
		this.renderSemaphore = renderSemaphore.Validate();

		deletionQueue.Push( () => Apis.Vk.DestroySemaphore( logicalDevice, renderSemaphore, null ) );
		deletionQueue.Push( () => Apis.Vk.DestroySemaphore( logicalDevice, presentSemaphore, null ) );
		deletionQueue.Push( () => Apis.Vk.DestroyFence( logicalDevice, renderFence, null ) );
	}

	private void InitializePipelines()
	{
		ArgumentNullException.ThrowIfNull( view, nameof( view ) );

		if ( !TryLoadShaderModule( "E:\\GitHub\\Latte\\Latte.NewRenderer\\Shaders\\colored_triangle.vert.spv", out var coloredTriangleVert ) )
			throw new ApplicationException( "Failed to build colored triangle vertex shader" );

		if ( !TryLoadShaderModule( "E:\\GitHub\\Latte\\Latte.NewRenderer\\Shaders\\colored_triangle.frag.spv", out var coloredTriangleFrag ) )
			throw new ApplicationException( "Failed to build colored triangle fragment shader" );

		var pipelineLayoutCreateInfo = VkInfo.PipelineLayout();
		Apis.Vk.CreatePipelineLayout( logicalDevice, pipelineLayoutCreateInfo, null, out var pipelineLayout ).Verify();
		trianglePipelineLayout = pipelineLayout.Validate();

		var pipelineBuilder = new VkPipelineBuilder( logicalDevice, renderPass )
			.WithPipelineLayout( trianglePipelineLayout )
			.WithViewport( new Viewport( 0, 0, view.Size.X, view.Size.Y, 0, 1 ) )
			.WithScissor( new Rect2D( new Offset2D( 0, 0 ), new Extent2D( (uint)view.Size.X, (uint)view.Size.Y ) ) )
			.AddShaderStage( VkInfo.ShaderStage( ShaderStageFlags.VertexBit, coloredTriangleVert ) )
			.AddShaderStage( VkInfo.ShaderStage( ShaderStageFlags.FragmentBit, coloredTriangleFrag ) )
			.WithVertexInputState( VkInfo.VertexInputState( new VertexInputDescription() ) )
			.WithInputAssemblyState( VkInfo.InputAssemblyState( PrimitiveTopology.TriangleList ) )
			.WithRasterizerState( VkInfo.RasterizationState( PolygonMode.Fill ) )
			.WithMultisamplingState( VkInfo.MultisamplingState() )
			.WithColorBlendAttachmentState( VkInfo.ColorBlendAttachmentState() );
		coloredTrianglePipeline = pipelineBuilder.Build().Validate();

		Apis.Vk.DestroyShaderModule( logicalDevice, coloredTriangleVert, null );
		Apis.Vk.DestroyShaderModule( logicalDevice, coloredTriangleFrag, null );

		if ( !TryLoadShaderModule( "E:\\GitHub\\Latte\\Latte.NewRenderer\\Shaders\\triangle.vert.spv", out var redTriangleVert ) )
			throw new ApplicationException( "Failed to build red triangle vertex shader" );

		if ( !TryLoadShaderModule( "E:\\GitHub\\Latte\\Latte.NewRenderer\\Shaders\\triangle.frag.spv", out var redTriangleFrag ) )
			throw new ApplicationException( "Failed to build red triangle fragment shader" );

		redTrianglePipeline = pipelineBuilder.ClearShaderStages()
			.AddShaderStage( VkInfo.ShaderStage( ShaderStageFlags.VertexBit, redTriangleVert ) )
			.AddShaderStage( VkInfo.ShaderStage( ShaderStageFlags.FragmentBit, redTriangleFrag ) )
			.WithVertexInputState( VkInfo.VertexInputState( VertexInputDescription.GetVertexDescription() ) )
			.Build().Validate();

		Apis.Vk.DestroyShaderModule( logicalDevice, redTriangleVert, null );
		Apis.Vk.DestroyShaderModule( logicalDevice, redTriangleFrag, null );

		if ( !TryLoadShaderModule( "E:\\GitHub\\Latte\\Latte.NewRenderer\\Shaders\\mesh_triangle.vert.spv", out var meshTriangleVert ) )
			throw new ApplicationException( "Failed to build mesh triangle vertex shader" );

		if ( !TryLoadShaderModule( "E:\\GitHub\\Latte\\Latte.NewRenderer\\Shaders\\colored_triangle.frag.spv", out var meshTriangleFrag ) )
			throw new ApplicationException( "Failed to build mesh triangle fragment shader" );

		var meshPipelineCreateInfo = VkInfo.PipelineLayout();
		var pushConstant = new PushConstantRange
		{
			Offset = 0,
			Size = (uint)Unsafe.SizeOf<MeshPushConstants>(),
			StageFlags = ShaderStageFlags.VertexBit
		};
		meshPipelineCreateInfo.PushConstantRangeCount = 1;
		meshPipelineCreateInfo.PPushConstantRanges = &pushConstant;
		Apis.Vk.CreatePipelineLayout( logicalDevice, meshPipelineCreateInfo, null, out var meshPipelineLayout ).Verify();
		this.meshPipelineLayout = meshPipelineLayout.Validate();

		meshPipeline = pipelineBuilder.ClearShaderStages()
			.WithPipelineLayout( meshPipelineLayout )
			.AddShaderStage( VkInfo.ShaderStage( ShaderStageFlags.VertexBit, meshTriangleVert ) )
			.AddShaderStage( VkInfo.ShaderStage( ShaderStageFlags.FragmentBit, meshTriangleFrag ) )
			.Build().Validate();

		Apis.Vk.DestroyShaderModule( logicalDevice, meshTriangleVert, null );
		Apis.Vk.DestroyShaderModule( logicalDevice, meshTriangleFrag, null );

		deletionQueue.Push( () => Apis.Vk.DestroyPipelineLayout( logicalDevice, pipelineLayout, null ) );
		deletionQueue.Push( () => Apis.Vk.DestroyPipeline( logicalDevice, coloredTrianglePipeline, null ) );
		deletionQueue.Push( () => Apis.Vk.DestroyPipeline( logicalDevice, redTrianglePipeline, null ) );
		deletionQueue.Push( () => Apis.Vk.DestroyPipelineLayout( logicalDevice, meshPipelineLayout, null ) );
		deletionQueue.Push( () => Apis.Vk.DestroyPipeline( logicalDevice, meshPipeline, null ) );
		currentPipeline = redTrianglePipeline;
	}

	private void LoadMeshes()
	{
		/*triangleMesh = new Mesh( [
			new Temp.Vertex( new Vector3D<float>( 1, 1, 0 ), Vector3D<float>.Zero, new Vector3D<float>( 0, 1, 0 ) ), 
			new Temp.Vertex( new Vector3D<float>( -1, 1, 0 ), Vector3D<float>.Zero, new Vector3D<float>( 0, 1, 0 ) ), 
			new Temp.Vertex( new Vector3D<float>( 0, -1, 0 ), Vector3D<float>.Zero, new Vector3D<float>( 0, 1, 0 ) ),
		], [] );*/

		//UploadMesh( triangleMesh, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.DeviceLocalBit );

		var model = Model.FromPath( "/monkey_smooth.obj" );
		var mesh = model.Meshes.First();
		monkeyMesh = new Mesh( mesh.Vertices, mesh.Indices );

		UploadMesh( monkeyMesh, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.DeviceLocalBit );
	}

	private void UploadMesh( Mesh mesh, MemoryPropertyFlags memoryFlags, SharingMode sharingMode = SharingMode.Exclusive )
	{
		// Vertex buffer
		{
			var createInfo = VkInfo.Buffer( (ulong)(mesh.Vertices.Length * Unsafe.SizeOf<Vertex>()), BufferUsageFlags.VertexBufferBit, sharingMode );
			Apis.Vk.CreateBuffer( logicalDevice, createInfo, null, out var buffer ).Verify();

			var requirements = Apis.Vk.GetBufferMemoryRequirements( logicalDevice, buffer );
			var allocateInfo = VkInfo.AllocateMemory( requirements.Size, FindMemoryType( requirements.MemoryTypeBits, memoryFlags ) );
			Apis.Vk.AllocateMemory( logicalDevice, allocateInfo, null, out var memory ).Verify();

			Apis.Vk.BindBufferMemory( logicalDevice, buffer, memory, 0 ).Verify();

			void* dataPtr;
			Apis.Vk.MapMemory( logicalDevice, memory, 0, requirements.Size, 0, &dataPtr ).Verify();
			mesh.Vertices.CopyTo( new Span<Vertex>( dataPtr, mesh.Vertices.Length ) ); ;
			Apis.Vk.UnmapMemory( logicalDevice, memory );

			deletionQueue.Push( () => Apis.Vk.DestroyBuffer( logicalDevice, buffer, null ) );
			deletionQueue.Push( () => Apis.Vk.FreeMemory( logicalDevice, memory, null ) );

			mesh.VertexBuffer = new AllocatedBuffer( buffer, new Allocation( memory, 0 ) );
		}

		// Index buffer
		{
			var createInfo = VkInfo.Buffer( (ulong)(mesh.Indices.Length * sizeof( uint )), BufferUsageFlags.IndexBufferBit, sharingMode );
			Apis.Vk.CreateBuffer( logicalDevice, createInfo, null, out var buffer ).Verify();

			var requirements = Apis.Vk.GetBufferMemoryRequirements( logicalDevice, buffer );
			var allocateInfo = VkInfo.AllocateMemory( requirements.Size, FindMemoryType( requirements.MemoryTypeBits, memoryFlags ) );
			Apis.Vk.AllocateMemory( logicalDevice, allocateInfo, null, out var memory ).Verify();

			Apis.Vk.BindBufferMemory( logicalDevice, buffer, memory, 0 ).Verify();

			void* dataPtr;
			Apis.Vk.MapMemory( logicalDevice, memory, 0, requirements.Size, 0, &dataPtr ).Verify();
			mesh.Indices.CopyTo( new Span<uint>( dataPtr, mesh.Indices.Length ) );
			Apis.Vk.UnmapMemory( logicalDevice, memory );

			deletionQueue.Push( () => Apis.Vk.DestroyBuffer( logicalDevice, buffer, null ) );
			deletionQueue.Push( () => Apis.Vk.FreeMemory( logicalDevice, memory, null ) );

			mesh.IndexBuffer = new AllocatedBuffer( buffer, new Allocation( memory, 0 ) );
		}
	}
		
	private uint FindMemoryType( uint typeFilter, MemoryPropertyFlags properties )
	{
		var memoryProperties = Apis.Vk.GetPhysicalDeviceMemoryProperties( physicalDevice );
		for ( var i = 0; i < memoryProperties.MemoryTypeCount; i++ )
		{
			if ( (typeFilter & (1 << i)) != 0 && (memoryProperties.MemoryTypes[i].PropertyFlags & properties) == properties )
				return (uint)i;
		}

		throw new ApplicationException( "Failed to find suitable memory type" );
	}

	private bool TryLoadShaderModule( string filePath, out ShaderModule shaderModule )
	{
		var shaderBytes = File.ReadAllBytes( filePath );

		fixed ( byte* shaderBytesPtr = shaderBytes )
		{
			var createInfo = VkInfo.ShaderModule( shaderBytes, ShaderModuleCreateFlags.None );
			var result = Apis.Vk.CreateShaderModule( logicalDevice, createInfo, null, out shaderModule );

			return result == Result.Success;
		}
	}

	private void Dispose( bool disposing )
	{
		if ( disposed || !IsInitialized )
			return;

		while ( deletionQueue.TryPop( out var deletionCb ) )
			deletionCb();

		if ( disposing )
		{
			swapchainExtension?.Dispose();
			debugUtilsExtension?.Dispose();
			surfaceExtension?.Dispose();
			view?.Dispose();
		}

		disposed = true;
	}

	public void Dispose()
	{
		Dispose( disposing: true );
		GC.SuppressFinalize( this );
	}
}

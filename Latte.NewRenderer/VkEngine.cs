using Latte.Assets;
using Latte.NewRenderer.Allocations;
using Latte.NewRenderer.Builders;
using Latte.NewRenderer.Exceptions;
using Latte.NewRenderer.Extensions;
using Latte.NewRenderer.Temp;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Mesh = Latte.NewRenderer.Temp.Mesh;
using LatteTexture = Latte.Assets.Texture;
using Texture = Latte.NewRenderer.Temp.Texture;
using System.Runtime.InteropServices;

namespace Latte.NewRenderer;

internal unsafe sealed class VkEngine : IDisposable
{
	private const int MaxFramesInFlight = 2;
	private const int MaxObjects = 10_000;
	private const string DefaultMeshMaterialName = "defaultmesh";
	private const string TexturedMeshMaterialName = "texturedmesh";
	private const string SwapchainTag = "swapchain";
	private const string WireframeTag = "wireframe";

	[MemberNotNullWhen( true, nameof( allocationManager ), nameof( disposalManager ), nameof( descriptorAllocator ) )]
	internal bool IsInitialized { get; private set; }

	internal bool WireframeEnabled
	{
		get => wireframeEnabled;
		set
		{
			wireframeEnabled = value;
			RecreateWireframe();
		}
	}
	private bool wireframeEnabled;

	private IView? view;
	private Instance instance;
	private DebugUtilsMessengerEXT debugMessenger;
	private PhysicalDevice physicalDevice;
	private VkQueueFamilyIndices queueFamilyIndices;
	private Device logicalDevice;
	private SurfaceKHR surface;
	private AllocationManager? allocationManager;
	private DisposalManager? disposalManager;
	private PhysicalDeviceProperties physicalDeviceProperties;

	private SwapchainKHR swapchain;
	private Format swapchainImageFormat;
	private ImmutableArray<Image> swapchainImages = [];
	private ImmutableArray<ImageView> swapchainImageViews = [];

	private AllocatedImage depthImage;
	private Format depthFormat;
	private ImageView depthImageView;

	private Queue graphicsQueue;
	private uint graphicsQueueFamily;
	private Queue presentQueue;
	private uint presentQueueFamily;
	private Queue transferQueue;
	private uint transferQueueFamily;

	private ImmutableArray<FrameData> frameData = [];
	private FrameData CurrentFrameData => frameData[frameNumber % MaxFramesInFlight];

	private DescriptorAllocator? descriptorAllocator;
	private DescriptorSetLayout frameSetLayout;
	private DescriptorSetLayout singleTextureSetLayout;

	private RenderPass renderPass;
	private ImmutableArray<Framebuffer> framebuffers;

	private Sampler linearSampler;
	private Sampler nearestSampler;

	private UploadContext uploadContext;
	private GpuSceneData sceneParameters;
	private AllocatedBuffer sceneParameterBuffer;
	private int frameNumber;

	private readonly List<Renderable> Renderables = [];
	private readonly Dictionary<string, Material> Materials = [];
	private readonly Dictionary<string, Mesh> Meshes = [];
	private readonly Dictionary<string, Texture> Textures = [];
	private readonly GpuObjectData[] objectData = new GpuObjectData[MaxObjects];

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
		if ( IsInitialized )
			throw new VkException( $"This {nameof( VkEngine )} has already been initialized" );

		ObjectDisposedException.ThrowIf( disposed, this );

		this.view = view;
		view.FramebufferResize += OnFramebufferResize;

		InitializeVulkan();
		InitializeSwapchain();
		InitializeCommands();
		InitializeDefaultRenderPass();
		InitializeFramebuffers();
		InitializeSynchronizationStructures();
		InitializeDescriptors();
		InitializePipelines();
		InitializeSamplers();
		LoadImages();
		LoadMeshes();
		SetupTextureSets();
		InitializeScene();

		IsInitialized = true;
	}

	internal void Draw()
	{
		if ( !IsInitialized )
			throw new VkException( $"This {nameof( VkEngine )} has not been initialized" );

		ObjectDisposedException.ThrowIf( disposed, this );
		ArgumentNullException.ThrowIfNull( view, nameof( view ) );
		ArgumentNullException.ThrowIfNull( swapchainExtension, nameof( swapchainExtension ) );

		var swapchain = this.swapchain;
		var currentFrameData = CurrentFrameData;
		var renderFence = currentFrameData.RenderFence;
		var presentSemaphore = currentFrameData.PresentSemaphore;
		var renderSemaphore = currentFrameData.RenderSemaphore;
		var cmd = currentFrameData.CommandBuffer;

		Apis.Vk.WaitForFences( logicalDevice, 1, renderFence, true, 1_000_000_000 ).Verify();

		uint swapchainImageIndex;
		var acquireResult = swapchainExtension.AcquireNextImage( logicalDevice, swapchain, 1_000_000_000, presentSemaphore, default, &swapchainImageIndex );
		switch ( acquireResult )
		{
			case Result.ErrorOutOfDateKhr:
			case Result.SuboptimalKhr:
				RecreateSwapchain();
				return;
			case Result.Success:
				break;
			default:
				throw new VkException( "Failed to acquire next image in the swap chain" );
		}

		Apis.Vk.ResetFences( logicalDevice, 1, renderFence ).Verify();
		Apis.Vk.ResetCommandBuffer( cmd, CommandBufferResetFlags.None ).Verify();

		var beginInfo = VkInfo.BeginCommandBuffer( CommandBufferUsageFlags.OneTimeSubmitBit );
		Apis.Vk.BeginCommandBuffer( cmd, beginInfo ).Verify();

		var renderArea = new Rect2D( new Offset2D( 0, 0 ), new Extent2D( (uint)view.Size.X, (uint)view.Size.Y ) );

		ReadOnlySpan<ClearValue> clearValues = stackalloc ClearValue[]
		{
			new ClearValue
			{
				Color = new ClearColorValue( Camera.Main.ClearColor.X, Camera.Main.ClearColor.Y, Camera.Main.ClearColor.Z )
			},
			new ClearValue
			{
				DepthStencil = new ClearDepthStencilValue
				{
					Depth = 1
				}
			}
		};

		fixed( ClearValue* clearValuesPtr = clearValues )
		{
			var rpBeginInfo = new RenderPassBeginInfo
			{
				SType = StructureType.RenderPassBeginInfo,
				PNext = null,
				RenderPass = renderPass,
				RenderArea = renderArea,
				Framebuffer = framebuffers[(int)swapchainImageIndex],
				ClearValueCount = (uint)clearValues.Length,
				PClearValues = clearValuesPtr
			};

			Apis.Vk.CmdBeginRenderPass( cmd, rpBeginInfo, SubpassContents.Inline );
		}

		DrawObjects( cmd, 0, Renderables.Count );

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

		var presentResult = swapchainExtension.QueuePresent( presentQueue, presentInfo );
		switch ( presentResult )
		{
			case Result.ErrorOutOfDateKhr:
			case Result.SuboptimalKhr:
				RecreateSwapchain();
				break;
			case Result.Success:
				break;
			default:
				throw new VkException( "Failed to present queue" );
		}

		frameNumber++;
	}

	private void DrawObjects( CommandBuffer cmd, int first, int count )
	{
		ArgumentNullException.ThrowIfNull( this.view, nameof( this.view ) );
		ArgumentNullException.ThrowIfNull( allocationManager, nameof( allocationManager ) );

		var currentFrameData = CurrentFrameData;

		var view = Matrix4x4.Identity * Matrix4x4.CreateLookAt( Camera.Main.Position, Camera.Main.Position + Camera.Main.Front, Camera.Main.Up );
		var projection = Matrix4x4.CreatePerspectiveFieldOfView( Scalar.DegreesToRadians( Camera.Main.Zoom ),
			(float)this.view.Size.X / this.view.Size.Y,
			Camera.Main.ZNear, Camera.Main.ZFar );
		projection.M22 *= -1;

		var cameraData = new GpuCameraData
		{
			View = view,
			Projection = projection,
			ViewProjection = view * projection
		};
		allocationManager.SetMemory( currentFrameData.CameraBuffer.Allocation, cameraData, true );

		var frameIndex = frameNumber % frameData.Length;
		allocationManager.SetMemory( sceneParameterBuffer.Allocation, sceneParameters, PadUniformBufferSize( (ulong)sizeof( GpuSceneData ) ), frameIndex );

		var objectData = this.objectData.AsSpan().Slice( first, count );
		for ( var i = 0; i < count; i++ )
			objectData[i] = new GpuObjectData( Renderables[first + i].Transform );

		allocationManager.SetMemory( currentFrameData.ObjectBuffer.Allocation, (ReadOnlySpan<GpuObjectData>)objectData, true );

		Mesh? lastMesh = null;
		Material? lastMaterial = null;

		for ( var i = 0; i < count; i++ )
		{
			var obj = Renderables[first + i];
			var mesh = GetMesh( obj.MeshName );
			var material = GetMaterial( obj.MaterialName );

			if ( !ReferenceEquals( lastMaterial, material ) )
			{
				Apis.Vk.CmdBindPipeline( cmd, PipelineBindPoint.Graphics, material.Pipeline );
				
				var uniformOffset = (uint)(PadUniformBufferSize( (ulong)sizeof( GpuSceneData ) ) * (ulong)frameIndex);
				Apis.Vk.CmdBindDescriptorSets( cmd, PipelineBindPoint.Graphics, material.PipelineLayout, 0, 1, currentFrameData.FrameDescriptor, 1, &uniformOffset );

				if ( material.TextureSet.IsValid() )
					Apis.Vk.CmdBindDescriptorSets( cmd, PipelineBindPoint.Graphics, material.PipelineLayout, 1, 1, material.TextureSet, 0, null );

				lastMaterial = material;
			}

			if ( !ReferenceEquals( lastMesh, mesh ) )
			{
				Apis.Vk.CmdBindVertexBuffers( cmd, 0, 1, mesh.VertexBuffer.Buffer, 0 );
				if ( mesh.Indices.Length > 0 )
					Apis.Vk.CmdBindIndexBuffer( cmd, mesh.IndexBuffer.Buffer, 0, IndexType.Uint32 );

				lastMesh = mesh;
			}

			var instanceCount = 1;
			while ( i + instanceCount < count )
			{
				var nextObj = Renderables[first + i + instanceCount];
				var nextMesh = GetMesh( nextObj.MeshName );
				var nextMaterial = GetMaterial( nextObj.MaterialName );

				if ( !ReferenceEquals( mesh, nextMesh ) )
					break;

				if ( !ReferenceEquals( material, nextMaterial ) )
					break;

				instanceCount++;
			}

			if ( mesh.Indices.Length > 0 )
				Apis.Vk.CmdDrawIndexed( cmd, (uint)mesh.Indices.Length, (uint)instanceCount, 0, 0, (uint)i );
			else
				Apis.Vk.CmdDraw( cmd, (uint)mesh.Vertices.Length, (uint)instanceCount, 0, (uint)i );

			i += instanceCount - 1;
		}
	}

	internal void WaitForIdle()
	{
		if ( !IsInitialized )
			throw new VkException( $"This {nameof( VkEngine )} has not been initialized" );

		ObjectDisposedException.ThrowIf( disposed, this );

		Apis.Vk.DeviceWaitIdle( logicalDevice ).Verify();
	}

	private void RecreateSwapchain()
	{
		ArgumentNullException.ThrowIfNull( disposalManager, nameof( disposalManager ) );

		WaitForIdle();

		disposalManager.Dispose( SwapchainTag );
		InitializeSwapchain();
		InitializeFramebuffers();
		InitializePipelines();
		SetupTextureSets();
	}

	private void RecreateWireframe()
	{
		ArgumentNullException.ThrowIfNull( disposalManager, nameof( disposalManager ) );

		WaitForIdle();

		disposalManager.Dispose( WireframeTag );
		InitializePipelines();
		SetupTextureSets();
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

		instance = instanceBuilderResult.Instance;
		debugMessenger = instanceBuilderResult.DebugMessenger;
		debugUtilsExtension = instanceBuilderResult.DebugUtilsExtension;

		VkInvalidHandleException.ThrowIfInvalid( instance );
		VkInvalidHandleException.ThrowIfInvalid( debugMessenger );

		if ( !Apis.Vk.TryGetInstanceExtension<KhrSurface>( instance, out var surfaceExtension ) )
			throw new VkException( "Failed to get KHR_surface extension" );

		this.surfaceExtension = surfaceExtension;
		surface = view.VkSurface!.Create<AllocationCallbacks>( instance.ToHandle(), null ).ToSurface();

		var physicalDeviceSelectorResult = new VkPhysicalDeviceSelector( instance )
			.RequireDiscreteDevice( true )
			.RequireVersion( 1, 1, 0 )
			.WithSurface( surface, surfaceExtension )
			.RequireUniqueGraphicsQueue( true )
			.RequireUniquePresentQueue( true )
			.RequireUniqueTransferQueue( true )
			.Select();
		physicalDevice = physicalDeviceSelectorResult.PhysicalDevice;
		queueFamilyIndices = physicalDeviceSelectorResult.QueueFamilyIndices;
		VkInvalidHandleException.ThrowIfInvalid( physicalDevice );

		var logicalDeviceBuilderResult = new VkLogicalDeviceBuilder( physicalDevice )
			.WithSurface( surface, surfaceExtension )
			.WithQueueFamilyIndices( queueFamilyIndices )
			.WithExtensions( KhrSwapchain.ExtensionName )
			.WithFeatures( new PhysicalDeviceFeatures
			{
				FillModeNonSolid = Vk.True
			} )
			.WithPNext( new PhysicalDeviceShaderDrawParametersFeatures
			{
				SType = StructureType.PhysicalDeviceShaderDrawParametersFeatures,
				PNext = null,
				ShaderDrawParameters = Vk.True
			} )
			.Build();

		logicalDevice = logicalDeviceBuilderResult.LogicalDevice;
		graphicsQueue = logicalDeviceBuilderResult.GraphicsQueue;
		graphicsQueueFamily = logicalDeviceBuilderResult.GraphicsQueueFamily;
		presentQueue = logicalDeviceBuilderResult.PresentQueue;
		presentQueueFamily = logicalDeviceBuilderResult.PresentQueueFamily;
		transferQueue = logicalDeviceBuilderResult.TransferQueue;
		transferQueueFamily = logicalDeviceBuilderResult.TransferQueueFamily;

		VkInvalidHandleException.ThrowIfInvalid( logicalDevice );
		VkInvalidHandleException.ThrowIfInvalid( graphicsQueue );
		VkInvalidHandleException.ThrowIfInvalid( presentQueue );
		VkInvalidHandleException.ThrowIfInvalid( transferQueue );

		allocationManager = new AllocationManager( physicalDevice, logicalDevice );
		disposalManager = new DisposalManager();
		physicalDeviceProperties = Apis.Vk.GetPhysicalDeviceProperties( physicalDevice );

		var frameDataBuilder = ImmutableArray.CreateBuilder<FrameData>( MaxFramesInFlight );
		for ( var i = 0; i < MaxFramesInFlight; i++ )
			frameDataBuilder.Add( new FrameData() );
		frameData = frameDataBuilder.MoveToImmutable();

		disposalManager.Add( () => Apis.Vk.DestroyInstance( instance, null ) );
		disposalManager.Add( () => debugUtilsExtension?.DestroyDebugUtilsMessenger( instance, debugMessenger, null ) );
		disposalManager.Add( () => surfaceExtension.DestroySurface( instance, surface, null ) );
		disposalManager.Add( () => Apis.Vk.DestroyDevice( logicalDevice, null ) );
	}

	private void InitializeSwapchain()
	{
		ArgumentNullException.ThrowIfNull( view, nameof( view ) );
		ArgumentNullException.ThrowIfNull( allocationManager, nameof( allocationManager ) );
		ArgumentNullException.ThrowIfNull( disposalManager, nameof( disposalManager ) );

		var result = new VkSwapchainBuilder( instance, physicalDevice, logicalDevice )
			.WithSurface( surface, surfaceExtension )
			.WithQueueFamilyIndices( queueFamilyIndices )
			.UseDefaultFormat()
			.SetPresentMode( PresentModeKHR.FifoKhr )
			.SetExtent( (uint)view.Size.X, (uint)view.Size.Y )
			.Build();

		swapchain = result.Swapchain;
		swapchainExtension = result.SwapchainExtension;
		swapchainImages = result.SwapchainImages;
		swapchainImageViews = result.SwapchainImageViews;
		swapchainImageFormat = result.SwapchainImageFormat;

		var depthExtent = new Extent3D
		{
			Width = (uint)view.Size.X,
			Height = (uint)view.Size.Y,
			Depth = 1
		};

		depthFormat = Format.D32Sfloat;

		var depthImageInfo = VkInfo.Image( depthFormat, ImageUsageFlags.DepthStencilAttachmentBit, depthExtent );
		Apis.Vk.CreateImage( logicalDevice, depthImageInfo, null, out var depthImage ).Verify();
		VkInvalidHandleException.ThrowIfInvalid( depthImage );
		this.depthImage = allocationManager.AllocateImage( depthImage, MemoryPropertyFlags.DeviceLocalBit );

		var depthImageViewInfo = VkInfo.ImageView( depthFormat, depthImage, ImageAspectFlags.DepthBit );
		Apis.Vk.CreateImageView( logicalDevice, depthImageViewInfo, null, out var depthImageView ).Verify();
		VkInvalidHandleException.ThrowIfInvalid( depthImageView );
		this.depthImageView = depthImageView;

		disposalManager.Add( () => swapchainExtension.DestroySwapchain( logicalDevice, swapchain, null ), SwapchainTag );
		for ( var i = 0; i < swapchainImageViews.Length; i++ )
		{
			var index = i;
			disposalManager.Add( () => Apis.Vk.DestroyImageView( logicalDevice, swapchainImageViews[index], null ), SwapchainTag );
		}

		disposalManager.Add( () => Apis.Vk.DestroyImage( logicalDevice, depthImage, null ), SwapchainTag );
		disposalManager.Add( () => Apis.Vk.DestroyImageView( logicalDevice, depthImageView, null ), SwapchainTag );
	}

	private void InitializeCommands()
	{
		ArgumentNullException.ThrowIfNull( disposalManager, nameof( disposalManager ) );

		var poolCreateInfo = VkInfo.CommandPool( graphicsQueueFamily, CommandPoolCreateFlags.ResetCommandBufferBit );

		for ( var i = 0; i < MaxFramesInFlight; i++ )
		{			
			Apis.Vk.CreateCommandPool( logicalDevice, poolCreateInfo, null, out var commandPool ).Verify();
			var bufferAllocateInfo = VkInfo.AllocateCommandBuffer( commandPool, 1, CommandBufferLevel.Primary );
			Apis.Vk.AllocateCommandBuffers( logicalDevice, bufferAllocateInfo, out var commandBuffer ).Verify();

			VkInvalidHandleException.ThrowIfInvalid( commandPool );
			VkInvalidHandleException.ThrowIfInvalid( commandBuffer );

			frameData[i].CommandPool = commandPool;
			frameData[i].CommandBuffer = commandBuffer;

			disposalManager.Add( () => Apis.Vk.DestroyCommandPool( logicalDevice, commandPool, null ) );
		}

		Apis.Vk.CreateCommandPool( logicalDevice, poolCreateInfo, null, out var uploadCommandPool ).Verify();
		VkInvalidHandleException.ThrowIfInvalid( uploadCommandPool );
		uploadContext.CommandPool = uploadCommandPool;

		var uploadBufferAllocateInfo = VkInfo.AllocateCommandBuffer( uploadCommandPool, 1, CommandBufferLevel.Primary );
		Apis.Vk.AllocateCommandBuffers( logicalDevice, uploadBufferAllocateInfo, out var uploadCommandBuffer ).Verify();
		VkInvalidHandleException.ThrowIfInvalid( uploadCommandBuffer );
		uploadContext.CommandBuffer = uploadCommandBuffer;

		disposalManager.Add( () => Apis.Vk.DestroyCommandPool( logicalDevice, uploadCommandPool, null ) );
	}

	private void InitializeDefaultRenderPass()
	{
		ArgumentNullException.ThrowIfNull( disposalManager, nameof( disposalManager ) );

		var colorAttachment = new AttachmentDescription
		{
			Format = swapchainImageFormat,
			Samples = SampleCountFlags.Count1Bit,
			LoadOp = AttachmentLoadOp.Clear,
			StoreOp = AttachmentStoreOp.Store,
			StencilLoadOp = AttachmentLoadOp.DontCare,
			StencilStoreOp = AttachmentStoreOp.DontCare,
			InitialLayout = ImageLayout.Undefined,
			FinalLayout = ImageLayout.PresentSrcKhr,
			Flags = AttachmentDescriptionFlags.None
		};

		var colorAttachmentReference = new AttachmentReference
		{
			Attachment = 0,
			Layout = ImageLayout.ColorAttachmentOptimal
		};

		var depthAttachment = new AttachmentDescription
		{
			Format = depthFormat,
			Samples = SampleCountFlags.Count1Bit,
			LoadOp = AttachmentLoadOp.Clear,
			StoreOp = AttachmentStoreOp.Store,
			StencilLoadOp = AttachmentLoadOp.Clear,
			StencilStoreOp = AttachmentStoreOp.DontCare,
			InitialLayout = ImageLayout.Undefined,
			FinalLayout = ImageLayout.DepthStencilAttachmentOptimal,
			Flags = AttachmentDescriptionFlags.None
		};

		var depthAttachmentReference = new AttachmentReference
		{
			Attachment = 1,
			Layout = ImageLayout.DepthStencilAttachmentOptimal
		};

		var subpassDescription = new SubpassDescription
		{
			PipelineBindPoint = PipelineBindPoint.Graphics,
			ColorAttachmentCount = 1,
			PColorAttachments = &colorAttachmentReference,
			PDepthStencilAttachment = &depthAttachmentReference
		};

		var colorDependency = new SubpassDependency
		{
			SrcSubpass = Vk.SubpassExternal,
			DstSubpass = 0,
			SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
			SrcAccessMask = 0,
			DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
			DstAccessMask = AccessFlags.ColorAttachmentWriteBit
		};

		var depthDependency = new SubpassDependency
		{
			SrcSubpass = Vk.SubpassExternal,
			DstSubpass = 0,
			SrcStageMask = PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
			SrcAccessMask = 0,
			DstStageMask = PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
			DstAccessMask = AccessFlags.DepthStencilAttachmentWriteBit
		};

		var createInfo = VkInfo.RenderPass(
			stackalloc AttachmentDescription[]
			{
				colorAttachment,
				depthAttachment
			},
			stackalloc SubpassDescription[] { subpassDescription },
			stackalloc SubpassDependency[]
			{
				colorDependency,
				depthDependency
			} );

		Apis.Vk.CreateRenderPass( logicalDevice, createInfo, null, out var renderPass ).Verify();

		VkInvalidHandleException.ThrowIfInvalid( renderPass );
		this.renderPass = renderPass;
		disposalManager.Add( () => Apis.Vk.DestroyRenderPass( logicalDevice, renderPass, null ) );
	}

	private void InitializeFramebuffers()
	{
		ArgumentNullException.ThrowIfNull( view, nameof( view ) );
		ArgumentNullException.ThrowIfNull( disposalManager, nameof( disposalManager ) );

		var framebufferBuilder = ImmutableArray.CreateBuilder<Framebuffer>( swapchainImages.Length );
		Span<ImageView> imageViews = stackalloc ImageView[2];
		imageViews[1] = depthImageView;

		var createInfo = VkInfo.Framebuffer( renderPass, (uint)view.Size.X, (uint)view.Size.Y, imageViews );
		for ( var i = 0; i < swapchainImages.Length; i++ )
		{
			imageViews[0] = swapchainImageViews[i];
			Apis.Vk.CreateFramebuffer( logicalDevice, createInfo, null, out var framebuffer ).Verify();

			VkInvalidHandleException.ThrowIfInvalid( framebuffer );
			framebufferBuilder.Add( framebuffer );
			disposalManager.Add( () => Apis.Vk.DestroyFramebuffer( logicalDevice, framebuffer, null ), SwapchainTag );
		}

		framebuffers = framebufferBuilder.MoveToImmutable();
	}

	private void InitializeSynchronizationStructures()
	{
		ArgumentNullException.ThrowIfNull( disposalManager, nameof( disposalManager ) );

		var fenceCreateInfo = VkInfo.Fence( FenceCreateFlags.SignaledBit );
		var semaphoreCreateInfo = VkInfo.Semaphore();

		for ( var i = 0; i < frameData.Length; i++ )
		{
			Apis.Vk.CreateFence( logicalDevice, fenceCreateInfo, null, out var renderFence ).Verify();
			Apis.Vk.CreateSemaphore( logicalDevice, semaphoreCreateInfo, null, out var presentSemaphore ).Verify();
			Apis.Vk.CreateSemaphore( logicalDevice, semaphoreCreateInfo, null, out var renderSemaphore ).Verify();

			VkInvalidHandleException.ThrowIfInvalid( renderFence );
			VkInvalidHandleException.ThrowIfInvalid( presentSemaphore );
			VkInvalidHandleException.ThrowIfInvalid( renderSemaphore );

			frameData[i].RenderFence = renderFence;
			frameData[i].PresentSemaphore = presentSemaphore;
			frameData[i].RenderSemaphore = renderSemaphore;

			disposalManager.Add( () => Apis.Vk.DestroySemaphore( logicalDevice, renderSemaphore, null ) );
			disposalManager.Add( () => Apis.Vk.DestroySemaphore( logicalDevice, presentSemaphore, null ) );
			disposalManager.Add( () => Apis.Vk.DestroyFence( logicalDevice, renderFence, null ) );
		}

		fenceCreateInfo.Flags = FenceCreateFlags.None;
		Apis.Vk.CreateFence( logicalDevice, fenceCreateInfo, null, out var uploadFence ).Verify();
		VkInvalidHandleException.ThrowIfInvalid( uploadFence );
		uploadContext.UploadFence = uploadFence;
		disposalManager.Add( () => Apis.Vk.DestroyFence( logicalDevice, uploadFence, null ) );
	}
	
	private void InitializeDescriptors()
	{
		ArgumentNullException.ThrowIfNull( disposalManager, nameof( disposalManager ) );

		descriptorAllocator = new DescriptorAllocator( logicalDevice, 100,
		[
			new( DescriptorType.UniformBuffer, 2 ),
			new( DescriptorType.UniformBufferDynamic, 1 ),
			new( DescriptorType.StorageBuffer, 1 ),
			new( DescriptorType.CombinedImageSampler, 4 )
		] );

		var layoutBuilder = new VkDescriptorSetLayoutBuilder( logicalDevice, 3 );

		frameSetLayout = layoutBuilder
			.AddBinding( 0, DescriptorType.UniformBuffer, ShaderStageFlags.VertexBit )
			.AddBinding( 1, DescriptorType.UniformBufferDynamic, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit )
			.AddBinding( 2, DescriptorType.StorageBuffer, ShaderStageFlags.VertexBit )
			.Build();
		VkInvalidHandleException.ThrowIfInvalid( frameSetLayout );

		singleTextureSetLayout = layoutBuilder.Clear()
			.AddBinding( 0, DescriptorType.CombinedImageSampler, ShaderStageFlags.FragmentBit )
			.Build();
		VkInvalidHandleException.ThrowIfInvalid( singleTextureSetLayout );

		var sceneParameterBufferSize = (ulong)frameData.Length * PadUniformBufferSize( (ulong)sizeof( GpuSceneData ) );
		sceneParameterBuffer = CreateBuffer( sceneParameterBufferSize, BufferUsageFlags.UniformBufferBit,
			MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.DeviceLocalBit );

		disposalManager.Add( () => Apis.Vk.DestroyDescriptorSetLayout( logicalDevice, frameSetLayout, null ) );
		disposalManager.Add( () => Apis.Vk.DestroyDescriptorSetLayout( logicalDevice, singleTextureSetLayout, null ) );
		disposalManager.Add( () => Apis.Vk.DestroyBuffer( logicalDevice, sceneParameterBuffer.Buffer, null ) );

		for ( var i = 0; i < frameData.Length; i++ )
		{
			frameData[i].FrameDescriptor = descriptorAllocator.Allocate( new ReadOnlySpan<DescriptorSetLayout>( ref frameSetLayout ) );
			VkInvalidHandleException.ThrowIfInvalid( frameData[i].FrameDescriptor );

			frameData[i].CameraBuffer = CreateBuffer( (ulong)sizeof( GpuCameraData ), BufferUsageFlags.UniformBufferBit,
				MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.DeviceLocalBit );

			frameData[i].ObjectBuffer = CreateBuffer( (ulong)sizeof( GpuObjectData ) * MaxObjects, BufferUsageFlags.StorageBufferBit,
				MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.DeviceLocalBit );

			var index = i;
			disposalManager.Add( () => Apis.Vk.DestroyBuffer( logicalDevice, frameData[index].CameraBuffer.Buffer, null ) );
			disposalManager.Add( () => Apis.Vk.DestroyBuffer( logicalDevice, frameData[index].ObjectBuffer.Buffer, null ) );

			new VkDescriptorUpdater( logicalDevice, 3 )
				.WriteBuffer( 0, DescriptorType.UniformBuffer, frameData[i].CameraBuffer.Buffer, 0, (ulong)sizeof( GpuCameraData ) )
				.WriteBuffer( 1, DescriptorType.UniformBufferDynamic, sceneParameterBuffer.Buffer, 0, (ulong)sizeof( GpuSceneData ) )
				.WriteBuffer( 2, DescriptorType.StorageBuffer, frameData[i].ObjectBuffer.Buffer, 0, (ulong)sizeof( GpuObjectData ) * MaxObjects )
				.Update( frameData[i].FrameDescriptor )
				.Dispose();
		}
	}

	private void InitializePipelines()
	{
		ArgumentNullException.ThrowIfNull( view, nameof( view ) );
		ArgumentNullException.ThrowIfNull( disposalManager, nameof( disposalManager ) );

		var meshTriangleShader = Shader.FromPath( "/Assets/Shaders/mesh_triangle.vert.spv" );
		if ( !TryLoadShaderModule( meshTriangleShader.Code.Span, out var meshTriangleVert ) )
			throw new VkException( "Failed to build mesh triangle shader" );

		var defaultLitShader = Shader.FromPath( "/Assets/Shaders/default_lit.frag.spv" );
		if ( !TryLoadShaderModule( defaultLitShader.Code.Span, out var defaultLitFrag ) )
			throw new VkException( "Failed to build default lit shader" );

		var pipelineLayoutBuilder = new VkPipelineLayoutBuilder( logicalDevice, 0, 2 );
		var meshPipelineLayout = pipelineLayoutBuilder
			.AddDescriptorSetLayout( frameSetLayout )
			.Build();
		VkInvalidHandleException.ThrowIfInvalid( meshPipelineLayout );

		var meshTriangleEntryPoint = Marshal.StringToHGlobalAnsi( meshTriangleShader.EntryPoint );
		var defaultLitEntryPoint = Marshal.StringToHGlobalAnsi( defaultLitShader.EntryPoint );

		var pipelineBuilder = new VkPipelineBuilder( logicalDevice, renderPass )
			.WithPipelineLayout( meshPipelineLayout )
			.WithViewport( new Viewport( 0, 0, view.Size.X, view.Size.Y, 0, 1 ) )
			.WithScissor( new Rect2D( new Offset2D( 0, 0 ), new Extent2D( (uint)view.Size.X, (uint)view.Size.Y ) ) )
			.AddShaderStage( VkInfo.PipelineShaderStage( ShaderStageFlags.VertexBit, meshTriangleVert, (byte*)meshTriangleEntryPoint ) )
			.AddShaderStage( VkInfo.PipelineShaderStage( ShaderStageFlags.FragmentBit, defaultLitFrag, (byte*)defaultLitEntryPoint ) )
			.WithVertexInputState( VkInfo.PipelineVertexInputState( VertexInputDescription.GetVertexDescription() ) )
			.WithInputAssemblyState( VkInfo.PipelineInputAssemblyState( PrimitiveTopology.TriangleList ) )
			.WithRasterizerState( VkInfo.PipelineRasterizationState( WireframeEnabled ? PolygonMode.Line : PolygonMode.Fill ) )
			.WithMultisamplingState( VkInfo.PipelineMultisamplingState() )
			.WithColorBlendAttachmentState( VkInfo.PipelineColorBlendAttachmentState() )
			.WithDepthStencilState( VkInfo.PipelineDepthStencilState( true, true, CompareOp.LessOrEqual ) );
		var meshPipeline = pipelineBuilder.Build();
		VkInvalidHandleException.ThrowIfInvalid( meshPipeline );

		Marshal.FreeHGlobal( defaultLitEntryPoint );

		var texturedLitShader = Shader.FromPath( "/Assets/Shaders/textured_lit.frag.spv" );
		if ( !TryLoadShaderModule( texturedLitShader.Code.Span, out var texturedLitFrag ) )
			throw new VkException( "Failed to build textured lit shader" );

		var texturedPipelineLayout = pipelineLayoutBuilder
			.AddDescriptorSetLayout( singleTextureSetLayout )
			.Build();
		VkInvalidHandleException.ThrowIfInvalid( texturedPipelineLayout );

		var texturedLitEntryPoint = Marshal.StringToHGlobalAnsi( texturedLitShader.EntryPoint );

		var texturedMeshPipeline = pipelineBuilder
			.WithPipelineLayout( texturedPipelineLayout )
			.ClearShaderStages()
			.AddShaderStage( VkInfo.PipelineShaderStage( ShaderStageFlags.VertexBit, meshTriangleVert, (byte*)meshTriangleEntryPoint ) )
			.AddShaderStage( VkInfo.PipelineShaderStage( ShaderStageFlags.FragmentBit, texturedLitFrag, (byte*)texturedLitEntryPoint ) )
			.Build();
		VkInvalidHandleException.ThrowIfInvalid( texturedMeshPipeline );

		Marshal.FreeHGlobal( meshTriangleEntryPoint );
		Marshal.FreeHGlobal( texturedLitEntryPoint );

		var defaultMeshMaterial = CreateMaterial( DefaultMeshMaterialName, meshPipeline, meshPipelineLayout );
		var texturedMeshMaterial = CreateMaterial( TexturedMeshMaterialName, texturedMeshPipeline, texturedPipelineLayout );
		disposalManager.Add( () => RemoveMaterial( DefaultMeshMaterialName ), SwapchainTag, WireframeTag );
		disposalManager.Add( () => RemoveMaterial( TexturedMeshMaterialName ), SwapchainTag, WireframeTag );

		Apis.Vk.DestroyShaderModule( logicalDevice, meshTriangleVert, null );
		Apis.Vk.DestroyShaderModule( logicalDevice, defaultLitFrag, null );
		Apis.Vk.DestroyShaderModule( logicalDevice, texturedLitFrag, null );

		disposalManager.Add( () => Apis.Vk.DestroyPipelineLayout( logicalDevice, meshPipelineLayout, null ), SwapchainTag, WireframeTag );
		disposalManager.Add( () => Apis.Vk.DestroyPipeline( logicalDevice, meshPipeline, null ), SwapchainTag, WireframeTag );
		disposalManager.Add( () => Apis.Vk.DestroyPipelineLayout( logicalDevice, texturedPipelineLayout, null ), SwapchainTag, WireframeTag );
		disposalManager.Add( () => Apis.Vk.DestroyPipeline( logicalDevice, texturedMeshPipeline, null ), SwapchainTag, WireframeTag );
	}

	private void InitializeSamplers()
	{
		ArgumentNullException.ThrowIfNull( disposalManager, nameof( disposalManager ) );

		Apis.Vk.CreateSampler( logicalDevice, VkInfo.Sampler( Filter.Linear ), null, out var linearSampler ).Verify();
		VkInvalidHandleException.ThrowIfInvalid( linearSampler );
		this.linearSampler = linearSampler;

		Apis.Vk.CreateSampler( logicalDevice, VkInfo.Sampler( Filter.Nearest ), null, out var nearestSampler ).Verify();
		VkInvalidHandleException.ThrowIfInvalid( nearestSampler );
		this.nearestSampler = nearestSampler;

		disposalManager.Add( () => Apis.Vk.DestroySampler( logicalDevice, linearSampler, null ) );
		disposalManager.Add( () => Apis.Vk.DestroySampler( logicalDevice, nearestSampler, null ) );
	}

	private void LoadImages()
	{
		void LoadTexture( string texturePath )
		{
			ArgumentNullException.ThrowIfNull( disposalManager, nameof( disposalManager ) );

			var latteTexture = LatteTexture.FromPath( texturePath );
			var texture = new Texture( (uint)latteTexture.Width, (uint)latteTexture.Height, (uint)latteTexture.BytesPerPixel, latteTexture.PixelData );
			UploadTexture( texture );

			var imageViewInfo = VkInfo.ImageView( Format.R8G8B8A8Srgb, texture.GpuTexture.Image, ImageAspectFlags.ColorBit );
			Apis.Vk.CreateImageView( logicalDevice, imageViewInfo, null, out var imageView ).Verify();
			VkInvalidHandleException.ThrowIfInvalid( imageView );
			texture.TextureView = imageView;

			Textures.Add( Path.GetFileNameWithoutExtension( texturePath ).ToLower(), texture );

			disposalManager.Add( () => Apis.Vk.DestroyImageView( logicalDevice, imageView, null ) );
		}

		LoadTexture( "/Assets/Models/Car 05/car5.png" );
		LoadTexture( "/Assets/Models/Car 05/car5_green.png" );
		LoadTexture( "/Assets/Models/Car 05/car5_grey.png" );
		LoadTexture( "/Assets/Models/Car 05/car5_police.png" );
		LoadTexture( "/Assets/Models/Car 05/car5_taxi.png" );
	}

	private void LoadMeshes()
	{
		void LoadMesh( string modelPath )
		{
			var model = Model.FromPath( modelPath );
			var carMesh = new Mesh( model.Meshes.First().Vertices, model.Meshes.First().Indices );

			UploadMesh( carMesh );
			Meshes.Add( Path.GetFileNameWithoutExtension( modelPath ).ToLower(), carMesh );
		}

		LoadMesh( "/Assets/Models/Car 05/Car5.obj" );
		LoadMesh( "/Assets/Models/Car 05/Car5_Police.obj" );
		LoadMesh( "/Assets/Models/Car 05/Car5_Taxi.obj" );
		LoadMesh( "/Assets/Models/quad.obj" );
	}

	private void SetupTextureSets()
	{
		ArgumentNullException.ThrowIfNull( descriptorAllocator, nameof( descriptorAllocator ) );
		ArgumentNullException.ThrowIfNull( disposalManager, nameof( disposalManager ) );

		var defaultMaterial = GetMaterial( TexturedMeshMaterialName );
		foreach ( var (textureName, texture) in Textures )
		{
			var texturedMaterial = defaultMaterial.Clone();
			texturedMaterial.TextureSet = descriptorAllocator.Allocate( new ReadOnlySpan<DescriptorSetLayout>( ref singleTextureSetLayout ) );
			VkInvalidHandleException.ThrowIfInvalid( texturedMaterial.TextureSet );

			new VkDescriptorUpdater( logicalDevice, 1 )
				.WriteImage( 0, DescriptorType.CombinedImageSampler, texture.TextureView, nearestSampler, ImageLayout.ShaderReadOnlyOptimal )
				.Update( texturedMaterial.TextureSet )
				.Dispose();

			Materials.Add( textureName, texturedMaterial );
			disposalManager.Add( () => RemoveMaterial( textureName ), SwapchainTag, WireframeTag );
		}
	}

	private void InitializeScene()
	{
		Renderables.Add( new Renderable( "quad", DefaultMeshMaterialName )
		{
			Transform = Matrix4x4.CreateScale( 1000, 0, 1000 )
		} );

		foreach ( var (materialName, _) in Materials.Skip( 2 ) )
		{
			for ( var i = 0; i < 1000; i++ )
			{
				var meshName = materialName switch
				{
					"car5_police" => "car5_police",
					"car5_taxi" => "car5_taxi",
					_ => "car5"
				};

				Renderables.Add( new Renderable( meshName, materialName )
				{
					Transform = Matrix4x4.CreateTranslation( Random.Shared.Next( -250, 251 ), 0, Random.Shared.Next( -250, 251 ) )
				} );
			}
		}
	}

	private void OnFramebufferResize( Vector2D<int> newSize )
	{
		RecreateSwapchain();
	}

	private void ImmediateSubmit( Action<CommandBuffer> cb )
	{
		var cmd = uploadContext.CommandBuffer;
		var beginInfo = VkInfo.BeginCommandBuffer( CommandBufferUsageFlags.OneTimeSubmitBit );

		Apis.Vk.BeginCommandBuffer( cmd, beginInfo ).Verify();
		cb( cmd );
		Apis.Vk.EndCommandBuffer( cmd ).Verify();

		var submitInfo = VkInfo.SubmitInfo( new ReadOnlySpan<CommandBuffer>( ref cmd ) );
		Apis.Vk.QueueSubmit( graphicsQueue, 1, submitInfo, uploadContext.UploadFence ).Verify();
		Apis.Vk.WaitForFences( logicalDevice, 1, uploadContext.UploadFence, Vk.True, 999_999_999_999 ).Verify();
		Apis.Vk.ResetFences( logicalDevice, 1, uploadContext.UploadFence ).Verify();
		Apis.Vk.ResetCommandPool( logicalDevice, uploadContext.CommandPool, CommandPoolResetFlags.None ).Verify();
	}

	private Material CreateMaterial( string name, Pipeline pipeline, PipelineLayout pipelineLayout )
	{
		if ( Materials.ContainsKey( name ) )
			throw new ArgumentException( $"A material with the name \"{name}\" already exists", nameof( name ) );

		var material = new Material( pipeline, pipelineLayout );
		Materials.Add( name, material );
		return material;
	}

	private void RemoveMaterial( string name )
	{
		if ( !Materials.ContainsKey( name ) )
			throw new ArgumentException( $"No material with the name \"{name}\" exists", nameof( name ) );

		Materials.Remove( name );
	}

	private Material GetMaterial( string name )
	{
		if ( Materials.TryGetValue( name, out var material ) )
			return material;

		throw new ArgumentException( $"A material with the name \"{name}\" does not exist", nameof( name ) );
	}

	private bool TryGetMaterial( string name, [NotNullWhen( true )] out Material? material )
	{
		return Materials.TryGetValue( name, out material );
	}

	private Mesh GetMesh( string name )
	{
		if ( Meshes.TryGetValue( name, out var mesh ) )
			return mesh;

		throw new ArgumentException( $"A mesh with the name \"{name}\" does not exist", nameof( name ) );
	}

	private bool TryGetMesh( string name, [NotNullWhen( true )] out Mesh? mesh )
	{
		return Meshes.TryGetValue( name, out mesh );
	}

	private void UploadMesh( Mesh mesh, SharingMode sharingMode = SharingMode.Exclusive )
	{
		ArgumentNullException.ThrowIfNull( allocationManager, nameof( allocationManager ) );
		ArgumentNullException.ThrowIfNull( disposalManager, nameof( disposalManager ) );

		// Vertex buffer
		{
			var bufferSize = (ulong)(mesh.Vertices.Length * Unsafe.SizeOf<Vertex>());

			var stagingBuffer = CreateBuffer( bufferSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit );
			allocationManager.SetMemory( stagingBuffer.Allocation, mesh.Vertices.AsSpan() );
			var vertexBuffer = CreateBuffer( bufferSize, BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit,
				MemoryPropertyFlags.DeviceLocalBit, sharingMode );

			ImmediateSubmit( cmd =>
			{
				var copyRegion = new BufferCopy
				{
					SrcOffset = 0,
					DstOffset = 0,
					Size = bufferSize
				};

				Apis.Vk.CmdCopyBuffer( cmd, stagingBuffer.Buffer, vertexBuffer.Buffer, 1, copyRegion );
			} );

			mesh.VertexBuffer = vertexBuffer;

			Apis.Vk.DestroyBuffer( logicalDevice, stagingBuffer.Buffer, null );
			disposalManager.Add( () => Apis.Vk.DestroyBuffer( logicalDevice, vertexBuffer.Buffer, null ) );
		}

		// Index buffer
		if ( mesh.Indices.Length == 0 )
			return;

		{
			var bufferSize = sizeof( uint ) * (ulong)mesh.Indices.Length;

			var stagingBuffer = CreateBuffer( bufferSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit );
			allocationManager.SetMemory( stagingBuffer.Allocation, mesh.Indices.AsSpan() );
			var indexBuffer = CreateBuffer( bufferSize, BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit,
				MemoryPropertyFlags.DeviceLocalBit, sharingMode );

			ImmediateSubmit( cmd =>
			{
				var copyRegion = new BufferCopy
				{
					SrcOffset = 0,
					DstOffset = 0,
					Size = bufferSize
				};

				Apis.Vk.CmdCopyBuffer( cmd, stagingBuffer.Buffer, indexBuffer.Buffer, 1, copyRegion );
			} );

			mesh.IndexBuffer = indexBuffer;

			Apis.Vk.DestroyBuffer( logicalDevice, stagingBuffer.Buffer, null );
			disposalManager.Add( () => Apis.Vk.DestroyBuffer( logicalDevice, indexBuffer.Buffer, null ) );
		}
	}
	
	private void UploadTexture( Texture texture )
	{
		ArgumentNullException.ThrowIfNull( allocationManager, nameof( allocationManager ) );
		ArgumentNullException.ThrowIfNull( disposalManager, nameof( disposalManager ) );

		var imageSize = texture.Width * texture.Height * texture.BytesPerPixel;
		var imageFormat = Format.R8G8B8A8Srgb;

		var stagingBuffer = CreateBuffer( imageSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit );
		allocationManager.SetMemory( stagingBuffer.Allocation, texture.PixelData.Span );

		var imageExtent = new Extent3D( texture.Width, texture.Height, 1 );
		var imageInfo = VkInfo.Image( imageFormat, ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit, imageExtent );
		Apis.Vk.CreateImage( logicalDevice, imageInfo, null, out var textureImage );

		var allocatedTextureImage = allocationManager.AllocateImage( textureImage, MemoryPropertyFlags.DeviceLocalBit );
		ImmediateSubmit( cmd =>
		{
			var range = new ImageSubresourceRange
			{
				AspectMask = ImageAspectFlags.ColorBit,
				BaseMipLevel = 0,
				LevelCount = 1,
				BaseArrayLayer = 0,
				LayerCount = 1
			};

			var toTransferLayout = new ImageMemoryBarrier
			{
				SType = StructureType.ImageMemoryBarrier,
				PNext = null,
				Image = textureImage,
				SubresourceRange = range,
				OldLayout = ImageLayout.Undefined,
				NewLayout = ImageLayout.TransferDstOptimal,
				SrcAccessMask = AccessFlags.None,
				DstAccessMask = AccessFlags.TransferWriteBit
			};

			Apis.Vk.CmdPipelineBarrier( cmd, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit, DependencyFlags.None,
				0, null, 0, null, 1, toTransferLayout );

			var copyRegion = new BufferImageCopy
			{
				BufferOffset = 0,
				BufferRowLength = 0,
				BufferImageHeight = 0,
				ImageExtent = imageExtent,
				ImageSubresource = new ImageSubresourceLayers
				{
					AspectMask = ImageAspectFlags.ColorBit,
					MipLevel = 0,
					BaseArrayLayer = 0,
					LayerCount = 1,
				}
			};

			Apis.Vk.CmdCopyBufferToImage( cmd, stagingBuffer.Buffer, textureImage, ImageLayout.TransferDstOptimal, 1, copyRegion );

			var toReadableFormat = new ImageMemoryBarrier
			{
				SType = StructureType.ImageMemoryBarrier,
				PNext = null,
				Image = textureImage,
				SubresourceRange = range,
				OldLayout = ImageLayout.TransferDstOptimal,
				NewLayout = ImageLayout.ShaderReadOnlyOptimal,
				SrcAccessMask = AccessFlags.TransferWriteBit,
				DstAccessMask = AccessFlags.ShaderReadBit
			};

			Apis.Vk.CmdPipelineBarrier( cmd, PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit, DependencyFlags.None,
				0, null, 0, null, 1, toReadableFormat );
		} );

		texture.GpuTexture = allocatedTextureImage;

		Apis.Vk.DestroyBuffer( logicalDevice, stagingBuffer.Buffer, null );
		disposalManager.Add( () => Apis.Vk.DestroyImage( logicalDevice, textureImage, null ) );
	}

	private AllocatedBuffer CreateBuffer( ulong size, BufferUsageFlags usageFlags, MemoryPropertyFlags memoryFlags,
		SharingMode sharingMode = SharingMode.Exclusive )
	{
		ArgumentNullException.ThrowIfNull( allocationManager, nameof( allocationManager ) );

		var createInfo = VkInfo.Buffer( size, usageFlags, sharingMode );
		Apis.Vk.CreateBuffer( logicalDevice, createInfo, null, out var buffer );

		return allocationManager.AllocateBuffer( buffer, memoryFlags );
	}

	private bool TryLoadShaderModule( ReadOnlySpan<byte> shaderBytes, out ShaderModule shaderModule )
	{
		fixed ( byte* shaderBytesPtr = shaderBytes )
		{
			var createInfo = VkInfo.ShaderModule( (nuint)shaderBytes.Length, shaderBytesPtr, ShaderModuleCreateFlags.None );
			var result = Apis.Vk.CreateShaderModule( logicalDevice, createInfo, null, out shaderModule );

			return result == Result.Success;
		}
	}

	private ulong PadUniformBufferSize( ulong currentSize )
	{
		var minimumAlignment = physicalDeviceProperties.Limits.MinUniformBufferOffsetAlignment;

		if ( minimumAlignment > 0 )
			return (currentSize + minimumAlignment - 1) & ~(minimumAlignment - 1);
		else
			return currentSize;
	}

	private void Dispose( bool disposing )
	{
		if ( disposed || !IsInitialized )
			return;

		ArgumentNullException.ThrowIfNull( view, nameof( view ) );
		view.FramebufferResize -= OnFramebufferResize;

		if ( disposing )
		{
		}

		allocationManager?.Dispose();
		descriptorAllocator?.Dispose();
		disposalManager?.Dispose();
		swapchainExtension?.Dispose();
		debugUtilsExtension?.Dispose();
		surfaceExtension?.Dispose();

		disposed = true;
	}

	public void Dispose()
	{
		Dispose( disposing: true );
		GC.SuppressFinalize( this );
	}
}

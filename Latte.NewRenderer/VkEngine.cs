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
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Mesh = Latte.NewRenderer.Temp.Mesh;

namespace Latte.NewRenderer;

internal unsafe sealed class VkEngine : IDisposable
{
	private const int MaxFramesInFlight = 2;
	private const int MaxObjects = 10_000;

	internal bool IsInitialized { get; private set; }

	private IView? view;
	private Instance instance;
	private DebugUtilsMessengerEXT debugMessenger;
	private PhysicalDevice physicalDevice;
	private VkPhysicalDeviceSelector? physicalDeviceSelector;
	private Device logicalDevice;
	private SurfaceKHR surface;
	private AllocationManager? allocationManager;
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

	private ImmutableArray<FrameData> frameData = [];
	private FrameData CurrentFrameData => frameData[frameNumber % MaxFramesInFlight];

	private DescriptorSetLayout globalSetLayout;
	private DescriptorSetLayout objectSetLayout;
	private DescriptorPool descriptorPool;

	private RenderPass renderPass;
	private ImmutableArray<Framebuffer> framebuffers;

	private readonly List<Renderable> Renderables = [];
	private readonly Dictionary<string, Material> Materials = [];
	private readonly Dictionary<string, Mesh> Meshes = [];

	private GpuSceneData sceneParameters;
	private AllocatedBuffer sceneParameterBuffer;
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
		InitializeDescriptors();
		InitializePipelines();
		LoadMeshes();
		InitializeScene();

		IsInitialized = true;
	}

	internal void Draw()
	{
		ArgumentNullException.ThrowIfNull( view, nameof( view ) );
		ArgumentNullException.ThrowIfNull( swapchainExtension, nameof( swapchainExtension ) );

		var swapchain = this.swapchain;
		var currentFrameData = CurrentFrameData;
		var renderFence = currentFrameData.RenderFence;
		var presentSemaphore = currentFrameData.PresentSemaphore;
		var renderSemaphore = currentFrameData.RenderSemaphore;
		var cmd = currentFrameData.CommandBuffer;

		Apis.Vk.WaitForFences( logicalDevice, 1, renderFence, true, 1_000_000_000 ).Verify();
		Apis.Vk.ResetFences( logicalDevice, 1, renderFence );

		uint swapchainImageIndex;
		swapchainExtension.AcquireNextImage( logicalDevice, swapchain, 1_000_000_000, presentSemaphore, default, &swapchainImageIndex );

		Apis.Vk.ResetCommandBuffer( cmd, CommandBufferResetFlags.None ).Verify();

		var beginInfo = VkInfo.BeginCommandBuffer( CommandBufferUsageFlags.OneTimeSubmitBit );
		Apis.Vk.BeginCommandBuffer( cmd, beginInfo ).Verify();

		ClearValue clearValue = default;
		var flash = MathF.Abs( MathF.Sin( frameNumber / 120f ) );
		clearValue.Color = new ClearColorValue( 0, 0, flash, 1 );

		ClearValue depthClearValue = default;
		depthClearValue.DepthStencil.Depth = 1;

		ReadOnlySpan<ClearValue> clearValues = stackalloc ClearValue[]
		{
			clearValue,
			depthClearValue
		};

		var renderArea = new Rect2D( new Offset2D( 0, 0 ), new Extent2D( (uint)view.Size.X, (uint)view.Size.Y ) );
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

		swapchainExtension.QueuePresent( presentQueue, presentInfo ).Verify();

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
		allocationManager.SetMemory( currentFrameData.CameraBuffer.Allocation, cameraData );

		var framed = frameNumber / 120f;
		sceneParameters.AmbientColor = new Vector4( MathF.Sin( framed ), 0, MathF.Cos( framed ), 1 );
		var frameIndex = frameNumber % frameData.Length;
		allocationManager.SetMemory( sceneParameterBuffer.Allocation, sceneParameters, PadUniformBufferSize( (ulong)sizeof( GpuSceneData ) ), frameIndex );

		// TODO: Cache the array
		var objectData = new GpuObjectData[count];
		for ( var i = 0; i < count; i++ )
			objectData[i] = new GpuObjectData( Renderables[first + i].Transform );

		allocationManager.SetMemory( currentFrameData.ObjectBuffer.Allocation, (ReadOnlySpan<GpuObjectData>)objectData.AsSpan() );

		Mesh? lastMesh = null;
		Material? lastMaterial = null;

		for ( var i = 0; i < count; i++ )
		{
			var obj = Renderables[first + i];

			if ( !ReferenceEquals( lastMaterial, obj.Material ) )
			{
				Apis.Vk.CmdBindPipeline( cmd, PipelineBindPoint.Graphics, obj.Material.Pipeline );
				
				var uniformOffset = (uint)(PadUniformBufferSize( (ulong)sizeof( GpuSceneData ) ) * (ulong)frameIndex);
				Apis.Vk.CmdBindDescriptorSets( cmd, PipelineBindPoint.Graphics, obj.Material.PipelineLayout, 0, 1, currentFrameData.GlobalDescriptor, 1, &uniformOffset );
				Apis.Vk.CmdBindDescriptorSets( cmd, PipelineBindPoint.Graphics, obj.Material.PipelineLayout, 1, 1, currentFrameData.ObjectDescriptor, 0, null );
				
				lastMaterial = obj.Material;
			}

			var constants = new MeshPushConstants( Vector4.Zero, obj.Transform );
			Apis.Vk.CmdPushConstants( cmd, obj.Material.PipelineLayout, ShaderStageFlags.VertexBit, 0, (uint)sizeof( MeshPushConstants ), &constants );

			if ( !ReferenceEquals( lastMesh, obj.Mesh ) )
			{
				Apis.Vk.CmdBindVertexBuffers( cmd, 0, 1, obj.Mesh.VertexBuffer.Buffer, 0 );
				if ( obj.Mesh.Indices.Length > 0 )
					Apis.Vk.CmdBindIndexBuffer( cmd, obj.Mesh.IndexBuffer.Buffer, 0, IndexType.Uint32 );

				lastMesh = obj.Mesh;
			}

			if ( lastMesh.Indices.Length > 0 )
				Apis.Vk.CmdDrawIndexed( cmd, (uint)obj.Mesh.Indices.Length, 1, 0, 0, (uint)i );
			else
				Apis.Vk.CmdDraw( cmd, (uint)obj.Mesh.Vertices.Length, 1, 0, (uint)i );
		}
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

		instance = instanceBuilderResult.Instance;
		debugMessenger = instanceBuilderResult.DebugMessenger;
		debugUtilsExtension = instanceBuilderResult.DebugUtilsExtension;

		VkInvalidHandleException.ThrowIfInvalid( instance );
		VkInvalidHandleException.ThrowIfInvalid( debugMessenger );

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
		physicalDevice = physicalDeviceSelector.Select();
		VkInvalidHandleException.ThrowIfInvalid( physicalDevice );

		var logicalDeviceBuilderResult = VkLogicalDeviceBuilder.FromPhysicalSelector( physicalDevice, physicalDeviceSelector )
			.WithExtensions( KhrSwapchain.ExtensionName )
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

		VkInvalidHandleException.ThrowIfInvalid( logicalDevice );
		VkInvalidHandleException.ThrowIfInvalid( graphicsQueue );
		VkInvalidHandleException.ThrowIfInvalid( presentQueue );

		allocationManager = new AllocationManager( physicalDevice, logicalDevice );
		physicalDeviceProperties = Apis.Vk.GetPhysicalDeviceProperties( physicalDevice );

		var frameDataBuilder = ImmutableArray.CreateBuilder<FrameData>( MaxFramesInFlight );
		for ( var i = 0; i < MaxFramesInFlight; i++ )
			frameDataBuilder.Add( new FrameData() );
		frameData = frameDataBuilder.MoveToImmutable();

		deletionQueue.Push( () => Apis.Vk.DestroyInstance( instance, null ) );
		deletionQueue.Push( () => debugUtilsExtension?.DestroyDebugUtilsMessenger( instance, debugMessenger, null ) );
		deletionQueue.Push( () => surfaceExtension.DestroySurface( instance, surface, null ) );
		deletionQueue.Push( () => Apis.Vk.DestroyDevice( logicalDevice, null ) );
	}

	private void InitializeSwapchain()
	{
		ArgumentNullException.ThrowIfNull( view, nameof( view ) );
		ArgumentNullException.ThrowIfNull( physicalDeviceSelector, nameof( physicalDeviceSelector ) );
		ArgumentNullException.ThrowIfNull( allocationManager, nameof( allocationManager ) );

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

		deletionQueue.Push( () => swapchainExtension.DestroySwapchain( logicalDevice, swapchain, null ) );
		for ( var i = 0; i < swapchainImageViews.Length; i++ )
		{
			var index = i;
			deletionQueue.Push( () => Apis.Vk.DestroyImageView( logicalDevice, swapchainImageViews[index], null ) );
		}

		deletionQueue.Push( () => Apis.Vk.DestroyImage( logicalDevice, depthImage, null ) );
		deletionQueue.Push( () => Apis.Vk.DestroyImageView( logicalDevice, depthImageView, null ) );
	}

	private void InitializeCommands()
	{
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

			deletionQueue.Push( () => Apis.Vk.DestroyCommandPool( logicalDevice, commandPool, null ) );
		}
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
		deletionQueue.Push( () => Apis.Vk.DestroyRenderPass( logicalDevice, renderPass, null ) );
	}

	private void InitializeFramebuffers()
	{
		ArgumentNullException.ThrowIfNull( view, nameof( view ) );

		var framebufferBuilder = ImmutableArray.CreateBuilder<Framebuffer>( swapchainImages.Length );
		var imageViews = stackalloc ImageView[2];
		imageViews[1] = depthImageView;

		var createInfo = VkInfo.Framebuffer( renderPass, (uint)view.Size.X, (uint)view.Size.Y );
		createInfo.AttachmentCount = 2;
		createInfo.PAttachments = imageViews;
		for ( var i = 0; i < swapchainImages.Length; i++ )
		{
			imageViews[0] = swapchainImageViews[i];
			Apis.Vk.CreateFramebuffer( logicalDevice, createInfo, null, out var framebuffer ).Verify();

			VkInvalidHandleException.ThrowIfInvalid( framebuffer );
			framebufferBuilder.Add( framebuffer );
			deletionQueue.Push( () => Apis.Vk.DestroyFramebuffer( logicalDevice, framebuffer, null ) );
		}

		framebuffers = framebufferBuilder.MoveToImmutable();
	}

	private void InitializeSynchronizationStructures()
	{
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

			deletionQueue.Push( () => Apis.Vk.DestroySemaphore( logicalDevice, renderSemaphore, null ) );
			deletionQueue.Push( () => Apis.Vk.DestroySemaphore( logicalDevice, presentSemaphore, null ) );
			deletionQueue.Push( () => Apis.Vk.DestroyFence( logicalDevice, renderFence, null ) );
		}
	}
	
	private void InitializeDescriptors()
	{
		ReadOnlySpan<DescriptorPoolSize> descriptorPoolSizes = stackalloc DescriptorPoolSize[]
		{
			new DescriptorPoolSize( DescriptorType.UniformBuffer, 10 ),
			new DescriptorPoolSize( DescriptorType.UniformBufferDynamic, 10 ),
			new DescriptorPoolSize( DescriptorType.StorageBuffer, 10 )
		};

		fixed ( DescriptorPoolSize* descriptorPoolSizesPtr = descriptorPoolSizes )
		{
			var poolCreateInfo = new DescriptorPoolCreateInfo
			{
				SType = StructureType.DescriptorPoolCreateInfo,
				PNext = null,
				MaxSets = 10,
				PoolSizeCount = (uint)descriptorPoolSizes.Length,
				PPoolSizes = descriptorPoolSizesPtr,
				Flags = DescriptorPoolCreateFlags.None
			};

			Apis.Vk.CreateDescriptorPool( logicalDevice, poolCreateInfo, null, out var descriptorPool ).Verify();
			VkInvalidHandleException.ThrowIfInvalid( descriptorPool );
			this.descriptorPool = descriptorPool;
		}

		{
			var cameraBinding = VkInfo.DescriptorSetLayoutBinding( DescriptorType.UniformBuffer, ShaderStageFlags.VertexBit, 0 );
			var sceneBinding = VkInfo.DescriptorSetLayoutBinding( DescriptorType.UniformBufferDynamic, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 1 );
			var objectBinding = VkInfo.DescriptorSetLayoutBinding( DescriptorType.StorageBuffer, ShaderStageFlags.VertexBit, 0 );

			var bindings = stackalloc DescriptorSetLayoutBinding[]
			{
				cameraBinding,
				sceneBinding
			};

			var setLayoutCreateInfo = new DescriptorSetLayoutCreateInfo
			{
				SType = StructureType.DescriptorSetLayoutCreateInfo,
				PNext = null,
				BindingCount = 2,
				PBindings = bindings,
				Flags = DescriptorSetLayoutCreateFlags.None
			};

			Apis.Vk.CreateDescriptorSetLayout( logicalDevice, setLayoutCreateInfo, null, out var tempLayout ).Verify();
			VkInvalidHandleException.ThrowIfInvalid( tempLayout );
			globalSetLayout = tempLayout;

			setLayoutCreateInfo.BindingCount = 1;
			setLayoutCreateInfo.PBindings = &objectBinding;

			Apis.Vk.CreateDescriptorSetLayout( logicalDevice, setLayoutCreateInfo, null, out tempLayout ).Verify();
			VkInvalidHandleException.ThrowIfInvalid( tempLayout );
			objectSetLayout = tempLayout;
		}

		var sceneParameterBufferSize = (ulong)frameData.Length * PadUniformBufferSize( (ulong)sizeof( GpuSceneData ) );
		sceneParameterBuffer = CreateBuffer( sceneParameterBufferSize, BufferUsageFlags.UniformBufferBit,
			MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.DeviceLocalBit );

		deletionQueue.Push( () => Apis.Vk.DestroyDescriptorPool( logicalDevice, descriptorPool, null ) );
		deletionQueue.Push( () => Apis.Vk.DestroyDescriptorSetLayout( logicalDevice, globalSetLayout, null ) );
		deletionQueue.Push( () => Apis.Vk.DestroyDescriptorSetLayout( logicalDevice, objectSetLayout, null ) );
		deletionQueue.Push( () => Apis.Vk.DestroyBuffer( logicalDevice, sceneParameterBuffer.Buffer, null ) );

		var descriptorSetAllocateInfo = new DescriptorSetAllocateInfo
		{
			SType = StructureType.DescriptorSetAllocateInfo,
			PNext = null,
			DescriptorPool = descriptorPool,
			DescriptorSetCount = 1
		};

		var descriptorWrites = stackalloc WriteDescriptorSet[3];
		for ( var i = 0; i < frameData.Length; i++ )
		{
			var setLayout = globalSetLayout;
			descriptorSetAllocateInfo.PSetLayouts = &setLayout;
			Apis.Vk.AllocateDescriptorSets( logicalDevice, descriptorSetAllocateInfo, out var descriptorSet ).Verify();
			VkInvalidHandleException.ThrowIfInvalid( descriptorSet );
			frameData[i].GlobalDescriptor = descriptorSet;

			setLayout = objectSetLayout;
			descriptorSetAllocateInfo.PSetLayouts = &setLayout;
			Apis.Vk.AllocateDescriptorSets( logicalDevice, descriptorSetAllocateInfo, out descriptorSet ).Verify();
			VkInvalidHandleException.ThrowIfInvalid( descriptorSet );
			frameData[i].ObjectDescriptor = descriptorSet;

			var index = i;
			frameData[i].CameraBuffer = CreateBuffer( (ulong)sizeof( GpuCameraData ), BufferUsageFlags.UniformBufferBit,
				MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.DeviceLocalBit );

			frameData[i].ObjectBuffer = CreateBuffer( (ulong)sizeof( GpuObjectData ) * MaxObjects, BufferUsageFlags.StorageBufferBit,
				MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.DeviceLocalBit );

			deletionQueue.Push( () => Apis.Vk.DestroyBuffer( logicalDevice, frameData[index].CameraBuffer.Buffer, null ) );
			deletionQueue.Push( () => Apis.Vk.DestroyBuffer( logicalDevice, frameData[index].ObjectBuffer.Buffer, null ) );

			var cameraBufferInfo = new DescriptorBufferInfo
			{
				Buffer = frameData[i].CameraBuffer.Buffer,
				Offset = 0,
				Range = (ulong)sizeof( GpuCameraData )
			};

			var sceneBufferInfo = new DescriptorBufferInfo
			{
				Buffer = sceneParameterBuffer.Buffer,
				Offset = 0,
				Range = (ulong)sizeof( GpuSceneData )
			};

			var objectBufferInfo = new DescriptorBufferInfo
			{
				Buffer = frameData[i].ObjectBuffer.Buffer,
				Offset = 0,
				Range = (ulong)sizeof( GpuObjectData ) * MaxObjects
			};

			var cameraWrite = VkInfo.WriteDescriptorBuffer( DescriptorType.UniformBuffer, frameData[i].GlobalDescriptor, cameraBufferInfo, 0 );
			var sceneWrite = VkInfo.WriteDescriptorBuffer( DescriptorType.UniformBufferDynamic, frameData[i].GlobalDescriptor, sceneBufferInfo, 1 );
			var objectWrite = VkInfo.WriteDescriptorBuffer( DescriptorType.StorageBuffer, frameData[i].ObjectDescriptor, objectBufferInfo, 0 );

			descriptorWrites[0] = cameraWrite;
			descriptorWrites[1] = sceneWrite;
			descriptorWrites[2] = objectWrite;
			Apis.Vk.UpdateDescriptorSets( logicalDevice, 3, descriptorWrites, 0, null );
		}
	}

	private void InitializePipelines()
	{
		ArgumentNullException.ThrowIfNull( view, nameof( view ) );

		if ( !TryLoadShaderModule( "E:\\GitHub\\Latte\\Latte.NewRenderer\\Shaders\\mesh_triangle.vert.spv", out var meshTriangleVert ) )
			throw new ApplicationException( "Failed to build mesh triangle vertex shader" );

		if ( !TryLoadShaderModule( "E:\\GitHub\\Latte\\Latte.NewRenderer\\Shaders\\default_lit.frag.spv", out var meshTriangleFrag ) )
			throw new ApplicationException( "Failed to build mesh triangle fragment shader" );

		var pushConstant = new PushConstantRange
		{
			Offset = 0,
			Size = (uint)Unsafe.SizeOf<MeshPushConstants>(),
			StageFlags = ShaderStageFlags.VertexBit
		};

		ReadOnlySpan<DescriptorSetLayout> descriptorSetLayouts = stackalloc DescriptorSetLayout[]
		{
			globalSetLayout,
			objectSetLayout
		};
		var meshPipelineCreateInfo = VkInfo.PipelineLayout( new ReadOnlySpan<PushConstantRange>( ref pushConstant ), descriptorSetLayouts );
		Apis.Vk.CreatePipelineLayout( logicalDevice, meshPipelineCreateInfo, null, out var meshPipelineLayout ).Verify();

		var meshPipeline = new VkPipelineBuilder( logicalDevice, renderPass )
			.WithPipelineLayout( meshPipelineLayout )
			.WithViewport( new Viewport( 0, 0, view.Size.X, view.Size.Y, 0, 1 ) )
			.WithScissor( new Rect2D( new Offset2D( 0, 0 ), new Extent2D( (uint)view.Size.X, (uint)view.Size.Y ) ) )
			.AddShaderStage( VkInfo.ShaderStage( ShaderStageFlags.VertexBit, meshTriangleVert ) )
			.AddShaderStage( VkInfo.ShaderStage( ShaderStageFlags.FragmentBit, meshTriangleFrag ) )
			.WithVertexInputState( VkInfo.VertexInputState( VertexInputDescription.GetVertexDescription() ) )
			.WithInputAssemblyState( VkInfo.InputAssemblyState( PrimitiveTopology.TriangleList ) )
			.WithRasterizerState( VkInfo.RasterizationState( PolygonMode.Fill ) )
			.WithMultisamplingState( VkInfo.MultisamplingState() )
			.WithColorBlendAttachmentState( VkInfo.ColorBlendAttachmentState() )
			.WithDepthStencilState( VkInfo.DepthStencilState( true, true, CompareOp.LessOrEqual ) )
			.Build();
		VkInvalidHandleException.ThrowIfInvalid( meshPipeline );

		CreateMaterial( "defaultmesh", meshPipeline, meshPipelineLayout );

		Apis.Vk.DestroyShaderModule( logicalDevice, meshTriangleVert, null );
		Apis.Vk.DestroyShaderModule( logicalDevice, meshTriangleFrag, null );

		deletionQueue.Push( () => Apis.Vk.DestroyPipelineLayout( logicalDevice, meshPipelineLayout, null ) );
		deletionQueue.Push( () => Apis.Vk.DestroyPipeline( logicalDevice, meshPipeline, null ) );
	}

	private void LoadMeshes()
	{
		var triangleMesh = new Mesh( [
			new Vertex( new Vector3( 1, 1, 0.5f ), Vector3.Zero, new Vector3( 0, 1, 0 ), Vector2.Zero ), 
			new Vertex( new Vector3( -1, 1, 0.5f ), Vector3.Zero, new Vector3( 0, 1, 0 ), Vector2.Zero ), 
			new Vertex( new Vector3( 0, -1, 0.5f ), Vector3.Zero, new Vector3( 0, 1, 0 ), Vector2.Zero ),
		], [] );

		Meshes.Add( "triangle", triangleMesh );
		UploadMesh( triangleMesh, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.DeviceLocalBit );

		var models = new string[]
		{
			"/Assets/monkey_smooth.obj",
			"/Assets/Car.obj",
			"/Assets/Car2.obj",
			"/Assets/Car3.obj",
			"/Assets/Car4.obj",
			"/Assets/Car5.obj",
			"/Assets/Car5_Police.obj",
			"/Assets/Car5_Taxi.obj",
			"/Assets/Car6.obj",
			"/Assets/Car7.obj",
		};
		foreach ( var modelPath in models )
		{
			var model = Model.FromPath( modelPath );
			var mesh = model.Meshes.First();
			var tempVertices = mesh.Vertices
				.Select( vertex => new Vertex( vertex.Position, vertex.Normal, vertex.Normal, vertex.TextureCoordinates ) )
				.ToImmutableArray();
			var monkeyMesh = new Mesh( tempVertices, mesh.Indices );

			Meshes.Add( Path.GetFileNameWithoutExtension( modelPath ), monkeyMesh );
			UploadMesh( monkeyMesh, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.DeviceLocalBit );
		}
	}

	private void InitializeScene()
	{
		var triangleMesh = GetMesh( "triangle" );
		var defaultMeshMaterial = GetMaterial( "defaultmesh" );

		foreach ( var (meshName, mesh) in Meshes )
		{
			if ( meshName == "triangle" )
				continue;

			for ( var i = 0; i < 40; i++ )
			{
				var randomMesh = new Renderable( mesh, defaultMeshMaterial );
				var x = Random.Shared.Next( -20, 21 );
				var y = Random.Shared.Next( -20, 21 );
				var translation = Matrix4x4.Identity * Matrix4x4.CreateTranslation( x * 5, 0, y * 5 );
				var scale = Matrix4x4.Identity * Matrix4x4.CreateScale( 0.2f, 0.2f, 0.2f );
				randomMesh.Transform = translation * scale;
				Renderables.Add( randomMesh );
			}
		}

		for ( var x = -20; x <= 20; x++ )
		{
			for ( var y = -20; y <= 20; y++ )
			{
				var triangle = new Renderable( triangleMesh, defaultMeshMaterial );
				var translation = Matrix4x4.Identity * Matrix4x4.CreateTranslation( x * 5, 0, y * 5 );
				var scale = Matrix4x4.Identity * Matrix4x4.CreateScale( 0.2f, 0.2f, 0.2f );
				triangle.Transform = translation * scale;

				Renderables.Add( triangle );
			}
		}
	}

	private Material CreateMaterial( string name, Pipeline pipeline, PipelineLayout pipelineLayout )
	{
		if ( Materials.ContainsKey( name ) )
			throw new ArgumentException( $"A material with the name \"{name}\" already exists", nameof( name ) );

		var material = new Material( pipeline, pipelineLayout );
		Materials.Add( name, material );
		return material;
	}

	private Material GetMaterial( string name )
	{
		if ( Materials.TryGetValue( name, out var material ) )
			return material;

		throw new ArgumentException( $"A material with the name \"{name}\" does not exist", nameof( name ) );
	}

	private Mesh GetMesh( string name )
	{
		if ( Meshes.TryGetValue( name, out var material ) )
			return material;

		throw new ArgumentException( $"A mesh with the name \"{name}\" does not exist", nameof( name ) );
	}

	private void UploadMesh( Mesh mesh, MemoryPropertyFlags memoryFlags, SharingMode sharingMode = SharingMode.Exclusive )
	{
		ArgumentNullException.ThrowIfNull( allocationManager, nameof( allocationManager ) );

		// Vertex buffer
		{
			var createInfo = VkInfo.Buffer( (ulong)(mesh.Vertices.Length * Unsafe.SizeOf<Vertex>()), BufferUsageFlags.VertexBufferBit, sharingMode );
			Apis.Vk.CreateBuffer( logicalDevice, createInfo, null, out var buffer ).Verify();

			mesh.VertexBuffer = allocationManager.AllocateBuffer( buffer, memoryFlags );
			allocationManager.SetMemory( mesh.VertexBuffer.Allocation, mesh.Vertices.AsSpan() );

			deletionQueue.Push( () => Apis.Vk.DestroyBuffer( logicalDevice, buffer, null ) );
		}

		// Index buffer
		if ( mesh.Indices.Length == 0 )
			return;

		{
			var createInfo = VkInfo.Buffer( (ulong)(sizeof( uint ) * mesh.Indices.Length), BufferUsageFlags.IndexBufferBit, sharingMode );
			Apis.Vk.CreateBuffer( logicalDevice, createInfo, null, out var buffer ).Verify();

			mesh.IndexBuffer = allocationManager.AllocateBuffer( buffer, memoryFlags );
			allocationManager.SetMemory( mesh.IndexBuffer.Allocation, mesh.Indices.AsSpan() );

			deletionQueue.Push( () => Apis.Vk.DestroyBuffer( logicalDevice, buffer, null ) );
		}
	}
	
	private AllocatedBuffer CreateBuffer( ulong size, BufferUsageFlags usageFlags, MemoryPropertyFlags memoryFlags,
		SharingMode sharingMode = SharingMode.Exclusive )
	{
		ArgumentNullException.ThrowIfNull( allocationManager, nameof( allocationManager ) );

		var createInfo = VkInfo.Buffer( size, usageFlags, sharingMode );
		Apis.Vk.CreateBuffer( logicalDevice, createInfo, null, out var buffer );

		return allocationManager.AllocateBuffer( buffer, memoryFlags );
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

		if ( disposing )
			allocationManager?.Dispose();

		while ( deletionQueue.TryPop( out var deletionCb ) )
			deletionCb();

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

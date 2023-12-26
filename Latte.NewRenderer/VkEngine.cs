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
using LatteShader = Latte.Assets.Shader;
using LatteTexture = Latte.Assets.Texture;
using Shader = Latte.NewRenderer.Temp.Shader;
using Texture = Latte.NewRenderer.Temp.Texture;
using Silk.NET.Input;
using Latte.NewRenderer.ImGui;
using Silk.NET.Core.Native;
using System.Runtime.InteropServices;
using System.Diagnostics;
using ImGuiNET;

namespace Latte.NewRenderer;

internal unsafe sealed class VkEngine : IDisposable
{
	private const int MaxFramesInFlight = 2;
	private const int MaxObjects = 10_000;
	private const string DefaultMeshMaterialName = "defaultmesh";
	private const string TexturedMeshMaterialName = "texturedmesh";
	private const string SwapchainTag = "swapchain";
	private const string WireframeTag = "wireframe";

	[MemberNotNullWhen( true, nameof( AllocationManager ), nameof( DisposalManager ), nameof( DescriptorAllocator ) )]
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

	internal string GraphicsDeviceName
	{
		get
		{
			fixed ( byte* namePtr = physicalDeviceProperties.DeviceName )
			{
				var nameStr = SilkMarshal.PtrToString( (nint)namePtr );
				if ( nameStr is null )
					throw new VkException( "Failed to get name of physical device" );

				return nameStr;
			}
		}
	}

	internal ImGuiController? ImGuiController { get; private set; }

	internal IView? View { get; private set; }
	internal bool IsVisible
	{
		get
		{
			ArgumentNullException.ThrowIfNull( View, nameof( View ) );
			return View.Size.X != 0 && View.Size.Y != 0;
		}
	}

	private Instance instance;
	private DebugUtilsMessengerEXT debugMessenger;
	internal PhysicalDevice PhysicalDevice { get; private set; }
	private VkQueueFamilyIndices queueFamilyIndices;
	internal Device LogicalDevice { get; private set; }
	private SurfaceKHR surface;
	internal AllocationManager? AllocationManager { get; private set; }
	internal DisposalManager? DisposalManager { get; private set; }
	private PhysicalDeviceProperties physicalDeviceProperties;

	private SwapchainKHR swapchain;
	internal Format SwapchainImageFormat { get; private set; }
	private ImmutableArray<Image> swapchainImages = [];
	private ImmutableArray<ImageView> swapchainImageViews = [];
	internal int SwapchainImageCount => swapchainImages.Length;

	private AllocatedImage depthImage;
	internal Format DepthFormat { get; private set; }
	private ImageView depthImageView;

	private Queue graphicsQueue;
	internal uint GraphicsQueueFamily { get; private set; }
	private Queue presentQueue;
	internal uint PresentQueueFamily { get; private set; }
	private Queue transferQueue;
	internal uint TransferQueueFamily { get; private set; }

	private ImmutableArray<FrameData> frameData = [];
	private FrameData CurrentFrameData => frameData[frameNumber % MaxFramesInFlight];

	internal DescriptorAllocator? DescriptorAllocator { get; private set; }
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

	private readonly Dictionary<string, TimeSpan> cpuPerformanceTimes = [];
	private readonly Dictionary<string, PipelineStatistics> materialPipelineStatistics = [];
	private QueryPool gpuExecuteQueryPool;
	private TimeSpan gpuExecuteTime;

	private readonly List<Renderable> Renderables = [];
	private readonly Dictionary<string, Material> Materials = [];
	private readonly Dictionary<string, Mesh> Meshes = [];
	private readonly Dictionary<string, Shader> Shaders = [];
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

	internal void Initialize( IView view, IInputContext input )
	{
		if ( IsInitialized )
			throw new VkException( $"This {nameof( VkEngine )} has already been initialized" );

		ObjectDisposedException.ThrowIf( disposed, this );

		this.View = view;
		view.FramebufferResize += OnFramebufferResize;

		InitializeVulkan();
		InitializeSwapchain();
		InitializeCommands();
		InitializeDefaultRenderPass();
		InitializeFramebuffers();
		InitializeSynchronizationStructures();
		InitializeDescriptors();
		InitializeShaders();
		InitializePipelines();
		InitializeSamplers();
		InitializeImGui( input );

		LoadImages();
		LoadMeshes();
		SetupTextureSets();
		InitializeQueries();

		InitializeScene();

		IsInitialized = true;
	}

	internal void Draw()
	{
		if ( !IsInitialized )
			throw new VkException( $"This {nameof( VkEngine )} has not been initialized" );

		var drawProfile = CpuProfile.New( "Total" );

		if ( !IsVisible )
			return;

		ObjectDisposedException.ThrowIf( disposed, this );
		ArgumentNullException.ThrowIfNull( View, nameof( View ) );
		ArgumentNullException.ThrowIfNull( swapchainExtension, nameof( swapchainExtension ) );
		ArgumentNullException.ThrowIfNull( ImGuiController, nameof( ImGuiController ) );

		var swapchain = this.swapchain;
		var currentFrameData = CurrentFrameData;
		var renderFence = currentFrameData.RenderFence;
		var presentSemaphore = currentFrameData.PresentSemaphore;
		var renderSemaphore = currentFrameData.RenderSemaphore;
		var cmd = currentFrameData.CommandBuffer;

		var waitForRenderProfile = CpuProfile.New( "Wait for last render" );
		using ( waitForRenderProfile )
			Apis.Vk.WaitForFences( LogicalDevice, 1, renderFence, true, 1_000_000_000 ).Verify();
		cpuPerformanceTimes[waitForRenderProfile.Name] = waitForRenderProfile.Time;

		uint swapchainImageIndex;
		var acquireSwapchainImageProfile = CpuProfile.New( "Acquire swapchain image" );
		using ( acquireSwapchainImageProfile )
		{
			var acquireResult = swapchainExtension.AcquireNextImage( LogicalDevice, swapchain, 1_000_000_000, presentSemaphore, default, &swapchainImageIndex );
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

			Apis.Vk.ResetFences( LogicalDevice, 1, renderFence ).Verify();
		}
		cpuPerformanceTimes[acquireSwapchainImageProfile.Name] = acquireSwapchainImageProfile.Time;

		var recordProfile = CpuProfile.New( "Record command buffer" );
		using ( recordProfile )
		{
			Apis.Vk.ResetCommandBuffer( cmd, CommandBufferResetFlags.None ).Verify();

			var beginInfo = VkInfo.BeginCommandBuffer( CommandBufferUsageFlags.OneTimeSubmitBit );
			Apis.Vk.BeginCommandBuffer( cmd, beginInfo ).Verify();

			Apis.Vk.CmdResetQueryPool( cmd, gpuExecuteQueryPool, 0, 2 );

			Apis.Vk.CmdWriteTimestamp( cmd, PipelineStageFlags.TopOfPipeBit, gpuExecuteQueryPool, 0 );

			foreach ( var (_, material) in Materials )
			{
				if ( material.PipelineQueryPool.IsValid() )
					Apis.Vk.CmdResetQueryPool( cmd, material.PipelineQueryPool, 0, 1 );
			}

			var renderArea = new Rect2D( new Offset2D( 0, 0 ), new Extent2D( (uint)View.Size.X, (uint)View.Size.Y ) );

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

			fixed ( ClearValue* clearValuesPtr = clearValues )
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

			ImGuiController.Render( cmd, framebuffers[(int)swapchainImageIndex], new Extent2D( (uint)View.Size.X, (uint)View.Size.Y ) );

			Apis.Vk.CmdWriteTimestamp( cmd, PipelineStageFlags.BottomOfPipeBit, gpuExecuteQueryPool, 1 );

			Apis.Vk.EndCommandBuffer( cmd ).Verify();
		}
		cpuPerformanceTimes[recordProfile.Name] = recordProfile.Time;

		var submitProfile = CpuProfile.New( "Submit" );
		using ( submitProfile )
		{
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
		}
		cpuPerformanceTimes[submitProfile.Name] = submitProfile.Time;

		var presentProfile = CpuProfile.New( "Present" );
		using ( presentProfile )
		{
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
		}
		cpuPerformanceTimes[presentProfile.Name] = presentProfile.Time;

		frameNumber++;
		drawProfile.Dispose();
		cpuPerformanceTimes[drawProfile.Name] = drawProfile.Time;
	}

	private void DrawObjects( CommandBuffer cmd, int first, int count )
	{
		ArgumentNullException.ThrowIfNull( this.View, nameof( this.View ) );
		ArgumentNullException.ThrowIfNull( AllocationManager, nameof( AllocationManager ) );

		var currentFrameData = CurrentFrameData;

		var view = Matrix4x4.Identity * Matrix4x4.CreateLookAt( Camera.Main.Position, Camera.Main.Position + Camera.Main.Front, Camera.Main.Up );
		var projection = Matrix4x4.CreatePerspectiveFieldOfView( Scalar.DegreesToRadians( Camera.Main.Zoom ),
			(float)this.View.Size.X / this.View.Size.Y,
			Camera.Main.ZNear, Camera.Main.ZFar );
		projection.M22 *= -1;

		var cameraData = new GpuCameraData
		{
			View = view,
			Projection = projection,
			ViewProjection = view * projection
		};
		AllocationManager.SetMemory( currentFrameData.CameraBuffer.Allocation, cameraData, true );

		var frameIndex = frameNumber % frameData.Length;
		AllocationManager.SetMemory( sceneParameterBuffer.Allocation, sceneParameters, PadUniformBufferSize( (ulong)sizeof( GpuSceneData ) ), frameIndex );

		var objectData = this.objectData.AsSpan().Slice( first, count );
		for ( var i = 0; i < count; i++ )
			objectData[i] = new GpuObjectData( Renderables[first + i].Transform );

		AllocationManager.SetMemory( currentFrameData.ObjectBuffer.Allocation, (ReadOnlySpan<GpuObjectData>)objectData, true );

		Mesh? lastMesh = null;
		Material? lastMaterial = null;

		for ( var i = 0; i < count; i++ )
		{
			var obj = Renderables[first + i];
			var mesh = GetMesh( obj.MeshName );
			var material = GetMaterial( obj.MaterialName );

			if ( !ReferenceEquals( lastMaterial, material ) )
			{
				if ( lastMaterial?.PipelineQueryPool.IsValid() ?? false )
					Apis.Vk.CmdEndQuery( cmd, lastMaterial.PipelineQueryPool, 0 );

				Apis.Vk.CmdBindPipeline( cmd, PipelineBindPoint.Graphics, material.Pipeline );
				if ( material.PipelineQueryPool.IsValid() )
					Apis.Vk.CmdBeginQuery( cmd, material.PipelineQueryPool, 0, QueryControlFlags.None );

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

		if ( lastMaterial?.PipelineQueryPool.IsValid() ?? false )
			Apis.Vk.CmdEndQuery( cmd, lastMaterial.PipelineQueryPool, 0 );
	}

	internal void WaitForIdle()
	{
		if ( !IsInitialized )
			throw new VkException( $"This {nameof( VkEngine )} has not been initialized" );

		ObjectDisposedException.ThrowIf( disposed, this );

		Apis.Vk.DeviceWaitIdle( LogicalDevice ).Verify();
	}

	internal void ImGuiShowRendererStatistics()
	{
		if ( !IsVisible )
			return;

		var overlayFlags = ImGuiWindowFlags.AlwaysAutoResize |
			ImGuiWindowFlags.NoSavedSettings |
			ImGuiWindowFlags.NoFocusOnAppearing |
			ImGuiWindowFlags.NoNav |
			ImGuiWindowFlags.NoMove;

		const float PAD = 10.0f;
		var viewport = ImGuiNET.ImGui.GetMainViewport();
		var workPos = viewport.WorkPos; // Use work area to avoid menu-bar/task-bar, if any!
		var windowPos = new Vector2( workPos.X + PAD, workPos.Y + PAD );
		var windowPivot = Vector2.Zero;

		ImGuiNET.ImGui.SetNextWindowPos( windowPos, ImGuiCond.Always, windowPivot );
		ImGuiNET.ImGui.SetNextWindowBgAlpha( 0.95f );
		if ( !ImGuiNET.ImGui.Begin( "Renderer Statistics", overlayFlags ) )
			return;

		ImGuiNET.ImGui.Text( GraphicsDeviceName );

		var stats = GetStatistics();

		ImGuiNET.ImGui.SeparatorText( "Performance" );

		if ( ImGuiNET.ImGui.CollapsingHeader( "CPU" ) && ImGuiNET.ImGui.BeginTable( "CPU Timings", 2, ImGuiTableFlags.Borders ) )
		{
			ImGuiNET.ImGui.TableSetupColumn( "Section" );
			ImGuiNET.ImGui.TableSetupColumn( "Time (ms)" );
			ImGuiNET.ImGui.TableHeadersRow();

			ImGuiNET.ImGui.TableNextColumn();
			foreach ( var (timingName, time) in stats.CpuTimings )
			{
				ImGuiNET.ImGui.Text( timingName );
				ImGuiNET.ImGui.TableNextColumn();
				ImGuiNET.ImGui.Text( $"{time.TotalMilliseconds:0.##}" );
				ImGuiNET.ImGui.TableNextColumn();
			}

			ImGuiNET.ImGui.EndTable();
		}

		if ( ImGuiNET.ImGui.CollapsingHeader( "GPU" ) && ImGuiNET.ImGui.BeginTable( "GPU Timings", 2, ImGuiTableFlags.Borders ) )
		{
			ImGuiNET.ImGui.TableSetupColumn( "Section" );
			ImGuiNET.ImGui.TableSetupColumn( "Time (ms)" );
			ImGuiNET.ImGui.TableHeadersRow();

			ImGuiNET.ImGui.TableNextColumn();

			ImGuiNET.ImGui.Text( "Execute" );
			ImGuiNET.ImGui.TableNextColumn();
			ImGuiNET.ImGui.Text( $"{stats.GpuExecuteTime.TotalMilliseconds:0.##}" );
			ImGuiNET.ImGui.TableNextColumn();

			ImGuiNET.ImGui.EndTable();
		}
		ImGuiNET.ImGui.SeparatorText( "Pipeline Stats" );
		foreach ( var (materialName, materialStats) in stats.MaterialStatistics )
		{
			if ( !ImGuiNET.ImGui.CollapsingHeader( materialName ) )
				continue;

			ImGuiNET.ImGui.Text( "Input assembly vertex count        : " + materialStats.InputAssemblyVertexCount );
			ImGuiNET.ImGui.Text( "Input assembly primitives count    : " + materialStats.InputAssemblyPrimitivesCount );
			ImGuiNET.ImGui.Text( "Vertex shader invocations          : " + materialStats.VertexShaderInvocationCount );
			ImGuiNET.ImGui.Text( "Clipping stage primitives processed: " + materialStats.ClippingStagePrimitivesProcessed );
			ImGuiNET.ImGui.Text( "Clipping stage primitives output   : " + materialStats.ClippingStagePrimitivesOutput );
			ImGuiNET.ImGui.Text( "Fragment shader invocations        : " + materialStats.FragmentShaderInvocations );
		}


		ImGuiNET.ImGui.End();
	}

	private void RecreateSwapchain()
	{
		ArgumentNullException.ThrowIfNull( DisposalManager, nameof( DisposalManager ) );
		ArgumentNullException.ThrowIfNull( View, nameof( View ) );

		WaitForIdle();

		DisposalManager.Dispose( SwapchainTag );
		if ( View.Size.X <= 0 || View.Size.Y <= 0 )
			return;

		InitializeSwapchain();
		InitializeFramebuffers();
		InitializePipelines();
		SetupTextureSets();
		InitializeQueries();
	}

	private void RecreateWireframe()
	{
		ArgumentNullException.ThrowIfNull( DisposalManager, nameof( DisposalManager ) );

		WaitForIdle();

		DisposalManager.Dispose( WireframeTag );
		InitializePipelines();
		SetupTextureSets();
		InitializeQueries();
	}

	private void InitializeVulkan()
	{
		ArgumentNullException.ThrowIfNull( View, nameof( View ) );

		var instanceBuilderResult = new VkInstanceBuilder()
			.WithName( "Example" )
			.WithView( View )
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
		surface = View.VkSurface!.Create<AllocationCallbacks>( instance.ToHandle(), null ).ToSurface();

		var physicalDeviceSelectorResult = new VkPhysicalDeviceSelector( instance )
			.RequireDiscreteDevice( true )
			.RequireVersion( 1, 1, 0 )
			.WithSurface( surface, surfaceExtension )
			.RequireUniqueGraphicsQueue( true )
			.RequireUniquePresentQueue( true )
			.RequireUniqueTransferQueue( true )
			.Select();
		PhysicalDevice = physicalDeviceSelectorResult.PhysicalDevice;
		queueFamilyIndices = physicalDeviceSelectorResult.QueueFamilyIndices;
		VkInvalidHandleException.ThrowIfInvalid( PhysicalDevice );

		var logicalDeviceBuilderResult = new VkLogicalDeviceBuilder( PhysicalDevice )
			.WithSurface( surface, surfaceExtension )
			.WithQueueFamilyIndices( queueFamilyIndices )
			.WithExtensions( KhrSwapchain.ExtensionName )
			.WithFeatures( new PhysicalDeviceFeatures
			{
				FillModeNonSolid = Vk.True,
				PipelineStatisticsQuery = Vk.True
			} )
			.WithPNext( new PhysicalDeviceShaderDrawParametersFeatures
			{
				SType = StructureType.PhysicalDeviceShaderDrawParametersFeatures,
				PNext = null,
				ShaderDrawParameters = Vk.True
			} )
			.Build();

		LogicalDevice = logicalDeviceBuilderResult.LogicalDevice;
		graphicsQueue = logicalDeviceBuilderResult.GraphicsQueue;
		GraphicsQueueFamily = logicalDeviceBuilderResult.GraphicsQueueFamily;
		presentQueue = logicalDeviceBuilderResult.PresentQueue;
		PresentQueueFamily = logicalDeviceBuilderResult.PresentQueueFamily;
		transferQueue = logicalDeviceBuilderResult.TransferQueue;
		TransferQueueFamily = logicalDeviceBuilderResult.TransferQueueFamily;

		VkInvalidHandleException.ThrowIfInvalid( LogicalDevice );
		VkInvalidHandleException.ThrowIfInvalid( graphicsQueue );
		VkInvalidHandleException.ThrowIfInvalid( presentQueue );
		VkInvalidHandleException.ThrowIfInvalid( transferQueue );

		AllocationManager = new AllocationManager( PhysicalDevice, LogicalDevice );
		DisposalManager = new DisposalManager();
		physicalDeviceProperties = Apis.Vk.GetPhysicalDeviceProperties( PhysicalDevice );

		var frameDataBuilder = ImmutableArray.CreateBuilder<FrameData>( MaxFramesInFlight );
		for ( var i = 0; i < MaxFramesInFlight; i++ )
			frameDataBuilder.Add( new FrameData() );
		frameData = frameDataBuilder.MoveToImmutable();

		DisposalManager.Add( () => Apis.Vk.DestroyInstance( instance, null ) );
		DisposalManager.Add( () => debugUtilsExtension?.DestroyDebugUtilsMessenger( instance, debugMessenger, null ) );
		DisposalManager.Add( () => surfaceExtension.DestroySurface( instance, surface, null ) );
		DisposalManager.Add( () => Apis.Vk.DestroyDevice( LogicalDevice, null ) );
	}

	private void InitializeSwapchain()
	{
		ArgumentNullException.ThrowIfNull( View, nameof( View ) );
		ArgumentNullException.ThrowIfNull( AllocationManager, nameof( AllocationManager ) );
		ArgumentNullException.ThrowIfNull( DisposalManager, nameof( DisposalManager ) );

		var result = new VkSwapchainBuilder( instance, PhysicalDevice, LogicalDevice )
			.WithSurface( surface, surfaceExtension )
			.WithQueueFamilyIndices( queueFamilyIndices )
			.UseDefaultFormat()
			.SetPresentMode( PresentModeKHR.FifoKhr )
			.SetExtent( (uint)View.Size.X, (uint)View.Size.Y )
			.Build();

		swapchain = result.Swapchain;
		swapchainExtension = result.SwapchainExtension;
		swapchainImages = result.SwapchainImages;
		swapchainImageViews = result.SwapchainImageViews;
		SwapchainImageFormat = result.SwapchainImageFormat;

		var depthExtent = new Extent3D
		{
			Width = (uint)View.Size.X,
			Height = (uint)View.Size.Y,
			Depth = 1
		};

		DepthFormat = Format.D32Sfloat;

		var depthImageInfo = VkInfo.Image( DepthFormat, ImageUsageFlags.DepthStencilAttachmentBit, depthExtent );
		Apis.Vk.CreateImage( LogicalDevice, depthImageInfo, null, out var depthImage ).Verify();
		VkInvalidHandleException.ThrowIfInvalid( depthImage );
		this.depthImage = AllocationManager.AllocateImage( depthImage, MemoryPropertyFlags.DeviceLocalBit );

		var depthImageViewInfo = VkInfo.ImageView( DepthFormat, depthImage, ImageAspectFlags.DepthBit );
		Apis.Vk.CreateImageView( LogicalDevice, depthImageViewInfo, null, out var depthImageView ).Verify();
		VkInvalidHandleException.ThrowIfInvalid( depthImageView );
		this.depthImageView = depthImageView;

		DisposalManager.Add( () => swapchainExtension.DestroySwapchain( LogicalDevice, swapchain, null ), SwapchainTag );
		for ( var i = 0; i < swapchainImageViews.Length; i++ )
		{
			var index = i;
			DisposalManager.Add( () => Apis.Vk.DestroyImageView( LogicalDevice, swapchainImageViews[index], null ), SwapchainTag );
		}

		DisposalManager.Add( () => Apis.Vk.DestroyImage( LogicalDevice, depthImage, null ), SwapchainTag );
		DisposalManager.Add( () => Apis.Vk.DestroyImageView( LogicalDevice, depthImageView, null ), SwapchainTag );
	}

	private void InitializeCommands()
	{
		ArgumentNullException.ThrowIfNull( DisposalManager, nameof( DisposalManager ) );

		var poolCreateInfo = VkInfo.CommandPool( GraphicsQueueFamily, CommandPoolCreateFlags.ResetCommandBufferBit );

		for ( var i = 0; i < MaxFramesInFlight; i++ )
		{
			Apis.Vk.CreateCommandPool( LogicalDevice, poolCreateInfo, null, out var commandPool ).Verify();
			var bufferAllocateInfo = VkInfo.AllocateCommandBuffer( commandPool, 1, CommandBufferLevel.Primary );
			Apis.Vk.AllocateCommandBuffers( LogicalDevice, bufferAllocateInfo, out var commandBuffer ).Verify();

			VkInvalidHandleException.ThrowIfInvalid( commandPool );
			VkInvalidHandleException.ThrowIfInvalid( commandBuffer );

			frameData[i].CommandPool = commandPool;
			frameData[i].CommandBuffer = commandBuffer;

			DisposalManager.Add( () => Apis.Vk.DestroyCommandPool( LogicalDevice, commandPool, null ) );
		}

		Apis.Vk.CreateCommandPool( LogicalDevice, poolCreateInfo, null, out var uploadCommandPool ).Verify();
		VkInvalidHandleException.ThrowIfInvalid( uploadCommandPool );
		uploadContext.CommandPool = uploadCommandPool;

		var uploadBufferAllocateInfo = VkInfo.AllocateCommandBuffer( uploadCommandPool, 1, CommandBufferLevel.Primary );
		Apis.Vk.AllocateCommandBuffers( LogicalDevice, uploadBufferAllocateInfo, out var uploadCommandBuffer ).Verify();
		VkInvalidHandleException.ThrowIfInvalid( uploadCommandBuffer );
		uploadContext.CommandBuffer = uploadCommandBuffer;

		DisposalManager.Add( () => Apis.Vk.DestroyCommandPool( LogicalDevice, uploadCommandPool, null ) );
	}

	private void InitializeDefaultRenderPass()
	{
		ArgumentNullException.ThrowIfNull( DisposalManager, nameof( DisposalManager ) );

		var colorAttachment = new AttachmentDescription
		{
			Format = SwapchainImageFormat,
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
			Format = DepthFormat,
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

		Apis.Vk.CreateRenderPass( LogicalDevice, createInfo, null, out var renderPass ).Verify();

		VkInvalidHandleException.ThrowIfInvalid( renderPass );
		this.renderPass = renderPass;
		DisposalManager.Add( () => Apis.Vk.DestroyRenderPass( LogicalDevice, renderPass, null ) );
	}

	private void InitializeFramebuffers()
	{
		ArgumentNullException.ThrowIfNull( View, nameof( View ) );
		ArgumentNullException.ThrowIfNull( DisposalManager, nameof( DisposalManager ) );

		var framebufferBuilder = ImmutableArray.CreateBuilder<Framebuffer>( swapchainImages.Length );
		Span<ImageView> imageViews = stackalloc ImageView[2];
		imageViews[1] = depthImageView;

		var createInfo = VkInfo.Framebuffer( renderPass, (uint)View.Size.X, (uint)View.Size.Y, imageViews );
		for ( var i = 0; i < swapchainImages.Length; i++ )
		{
			imageViews[0] = swapchainImageViews[i];
			Apis.Vk.CreateFramebuffer( LogicalDevice, createInfo, null, out var framebuffer ).Verify();

			VkInvalidHandleException.ThrowIfInvalid( framebuffer );
			framebufferBuilder.Add( framebuffer );
			DisposalManager.Add( () => Apis.Vk.DestroyFramebuffer( LogicalDevice, framebuffer, null ), SwapchainTag );
		}

		framebuffers = framebufferBuilder.MoveToImmutable();
	}

	private void InitializeSynchronizationStructures()
	{
		ArgumentNullException.ThrowIfNull( DisposalManager, nameof( DisposalManager ) );

		var fenceCreateInfo = VkInfo.Fence( FenceCreateFlags.SignaledBit );
		var semaphoreCreateInfo = VkInfo.Semaphore();

		for ( var i = 0; i < frameData.Length; i++ )
		{
			Apis.Vk.CreateFence( LogicalDevice, fenceCreateInfo, null, out var renderFence ).Verify();
			Apis.Vk.CreateSemaphore( LogicalDevice, semaphoreCreateInfo, null, out var presentSemaphore ).Verify();
			Apis.Vk.CreateSemaphore( LogicalDevice, semaphoreCreateInfo, null, out var renderSemaphore ).Verify();

			VkInvalidHandleException.ThrowIfInvalid( renderFence );
			VkInvalidHandleException.ThrowIfInvalid( presentSemaphore );
			VkInvalidHandleException.ThrowIfInvalid( renderSemaphore );

			frameData[i].RenderFence = renderFence;
			frameData[i].PresentSemaphore = presentSemaphore;
			frameData[i].RenderSemaphore = renderSemaphore;

			DisposalManager.Add( () => Apis.Vk.DestroySemaphore( LogicalDevice, renderSemaphore, null ) );
			DisposalManager.Add( () => Apis.Vk.DestroySemaphore( LogicalDevice, presentSemaphore, null ) );
			DisposalManager.Add( () => Apis.Vk.DestroyFence( LogicalDevice, renderFence, null ) );
		}

		fenceCreateInfo.Flags = FenceCreateFlags.None;
		Apis.Vk.CreateFence( LogicalDevice, fenceCreateInfo, null, out var uploadFence ).Verify();
		VkInvalidHandleException.ThrowIfInvalid( uploadFence );
		uploadContext.UploadFence = uploadFence;
		DisposalManager.Add( () => Apis.Vk.DestroyFence( LogicalDevice, uploadFence, null ) );
	}

	private void InitializeDescriptors()
	{
		ArgumentNullException.ThrowIfNull( DisposalManager, nameof( DisposalManager ) );

		DescriptorAllocator = new DescriptorAllocator( LogicalDevice, 100,
		[
			new( DescriptorType.UniformBuffer, 2 ),
			new( DescriptorType.UniformBufferDynamic, 1 ),
			new( DescriptorType.StorageBuffer, 1 ),
			new( DescriptorType.CombinedImageSampler, 4 )
		] );

		var layoutBuilder = new VkDescriptorSetLayoutBuilder( LogicalDevice, 3 );

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

		DisposalManager.Add( () => Apis.Vk.DestroyDescriptorSetLayout( LogicalDevice, frameSetLayout, null ) );
		DisposalManager.Add( () => Apis.Vk.DestroyDescriptorSetLayout( LogicalDevice, singleTextureSetLayout, null ) );
		DisposalManager.Add( () => Apis.Vk.DestroyBuffer( LogicalDevice, sceneParameterBuffer.Buffer, null ) );

		for ( var i = 0; i < frameData.Length; i++ )
		{
			frameData[i].FrameDescriptor = DescriptorAllocator.Allocate( new ReadOnlySpan<DescriptorSetLayout>( ref frameSetLayout ) );
			VkInvalidHandleException.ThrowIfInvalid( frameData[i].FrameDescriptor );

			frameData[i].CameraBuffer = CreateBuffer( (ulong)sizeof( GpuCameraData ), BufferUsageFlags.UniformBufferBit,
				MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.DeviceLocalBit );

			frameData[i].ObjectBuffer = CreateBuffer( (ulong)sizeof( GpuObjectData ) * MaxObjects, BufferUsageFlags.StorageBufferBit,
				MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.DeviceLocalBit );

			var index = i;
			DisposalManager.Add( () => Apis.Vk.DestroyBuffer( LogicalDevice, frameData[index].CameraBuffer.Buffer, null ) );
			DisposalManager.Add( () => Apis.Vk.DestroyBuffer( LogicalDevice, frameData[index].ObjectBuffer.Buffer, null ) );

			new VkDescriptorUpdater( LogicalDevice, 3 )
				.WriteBuffer( 0, DescriptorType.UniformBuffer, frameData[i].CameraBuffer.Buffer, 0, (ulong)sizeof( GpuCameraData ) )
				.WriteBuffer( 1, DescriptorType.UniformBufferDynamic, sceneParameterBuffer.Buffer, 0, (ulong)sizeof( GpuSceneData ) )
				.WriteBuffer( 2, DescriptorType.StorageBuffer, frameData[i].ObjectBuffer.Buffer, 0, (ulong)sizeof( GpuObjectData ) * MaxObjects )
				.Update( frameData[i].FrameDescriptor )
				.Dispose();
		}
	}

	private void InitializeShaders()
	{
		ArgumentNullException.ThrowIfNull( DisposalManager, nameof( DisposalManager ) );

		var meshTriangleShader = CreateShader( "mesh_triangle.vert", LatteShader.FromPath( "/Assets/Shaders/mesh_triangle.vert.spv" ) );
		var defaultLitShader = CreateShader( "default_lit.frag", LatteShader.FromPath( "/Assets/Shaders/default_lit.frag.spv" ) );
		var texturedLitShader = CreateShader( "textured_lit.frag", LatteShader.FromPath( "/Assets/Shaders/textured_lit.frag.spv" ) );

		DisposalManager.Add( meshTriangleShader.Dispose );
		DisposalManager.Add( defaultLitShader.Dispose );
		DisposalManager.Add( texturedLitShader.Dispose );
	}

	private void InitializePipelines()
	{
		ArgumentNullException.ThrowIfNull( View, nameof( View ) );
		ArgumentNullException.ThrowIfNull( DisposalManager, nameof( DisposalManager ) );

		var meshTriangleShader = GetShader( "mesh_triangle.vert" );
		var defaultLitShader = GetShader( "default_lit.frag" );
		var texturedLitShader = GetShader( "textured_lit.frag" );

		var pipelineLayoutBuilder = new VkPipelineLayoutBuilder( LogicalDevice, 0, 2 );
		var meshPipelineLayout = pipelineLayoutBuilder
			.AddDescriptorSetLayout( frameSetLayout )
			.Build();
		VkInvalidHandleException.ThrowIfInvalid( meshPipelineLayout );

		var pipelineBuilder = new VkPipelineBuilder( LogicalDevice, renderPass )
			.WithPipelineLayout( meshPipelineLayout )
			.WithViewport( new Viewport( 0, 0, View.Size.X, View.Size.Y, 0, 1 ) )
			.WithScissor( new Rect2D( new Offset2D( 0, 0 ), new Extent2D( (uint)View.Size.X, (uint)View.Size.Y ) ) )
			.AddShaderStage( VkInfo.PipelineShaderStage( ShaderStageFlags.VertexBit, meshTriangleShader.Module, (byte*)meshTriangleShader.EntryPointPtr ) )
			.AddShaderStage( VkInfo.PipelineShaderStage( ShaderStageFlags.FragmentBit, defaultLitShader.Module, (byte*)defaultLitShader.EntryPointPtr ) )
			.WithVertexInputState( VkInfo.PipelineVertexInputState( VertexInputDescription.GetLatteVertexDescription() ) )
			.WithInputAssemblyState( VkInfo.PipelineInputAssemblyState( PrimitiveTopology.TriangleList ) )
			.WithRasterizerState( VkInfo.PipelineRasterizationState( WireframeEnabled ? PolygonMode.Line : PolygonMode.Fill ) )
			.WithMultisamplingState( VkInfo.PipelineMultisamplingState() )
			.WithColorBlendAttachmentState( VkInfo.PipelineColorBlendAttachmentState() )
			.WithDepthStencilState( VkInfo.PipelineDepthStencilState( true, true, CompareOp.LessOrEqual ) );
		var meshPipeline = pipelineBuilder.Build();
		VkInvalidHandleException.ThrowIfInvalid( meshPipeline );

		var texturedPipelineLayout = pipelineLayoutBuilder
			.AddDescriptorSetLayout( singleTextureSetLayout )
			.Build();
		VkInvalidHandleException.ThrowIfInvalid( texturedPipelineLayout );

		var texturedMeshPipeline = pipelineBuilder
			.WithPipelineLayout( texturedPipelineLayout )
			.ClearShaderStages()
			.AddShaderStage( VkInfo.PipelineShaderStage( ShaderStageFlags.VertexBit, meshTriangleShader.Module, (byte*)meshTriangleShader.EntryPointPtr ) )
			.AddShaderStage( VkInfo.PipelineShaderStage( ShaderStageFlags.FragmentBit, texturedLitShader.Module, (byte*)texturedLitShader.EntryPointPtr ) )
			.Build();
		VkInvalidHandleException.ThrowIfInvalid( texturedMeshPipeline );

		var defaultMeshMaterial = CreateMaterial( DefaultMeshMaterialName, meshPipeline, meshPipelineLayout );
		var texturedMeshMaterial = CreateMaterial( TexturedMeshMaterialName, texturedMeshPipeline, texturedPipelineLayout );
		DisposalManager.Add( () => RemoveMaterial( DefaultMeshMaterialName ), SwapchainTag, WireframeTag );
		DisposalManager.Add( () => RemoveMaterial( TexturedMeshMaterialName ), SwapchainTag, WireframeTag );

		DisposalManager.Add( () => Apis.Vk.DestroyPipelineLayout( LogicalDevice, meshPipelineLayout, null ), SwapchainTag, WireframeTag );
		DisposalManager.Add( () => Apis.Vk.DestroyPipeline( LogicalDevice, meshPipeline, null ), SwapchainTag, WireframeTag );
		DisposalManager.Add( () => Apis.Vk.DestroyPipelineLayout( LogicalDevice, texturedPipelineLayout, null ), SwapchainTag, WireframeTag );
		DisposalManager.Add( () => Apis.Vk.DestroyPipeline( LogicalDevice, texturedMeshPipeline, null ), SwapchainTag, WireframeTag );
	}

	private void InitializeSamplers()
	{
		ArgumentNullException.ThrowIfNull( DisposalManager, nameof( DisposalManager ) );

		Apis.Vk.CreateSampler( LogicalDevice, VkInfo.Sampler( Filter.Linear ), null, out var linearSampler ).Verify();
		VkInvalidHandleException.ThrowIfInvalid( linearSampler );
		this.linearSampler = linearSampler;

		Apis.Vk.CreateSampler( LogicalDevice, VkInfo.Sampler( Filter.Nearest ), null, out var nearestSampler ).Verify();
		VkInvalidHandleException.ThrowIfInvalid( nearestSampler );
		this.nearestSampler = nearestSampler;

		DisposalManager.Add( () => Apis.Vk.DestroySampler( LogicalDevice, linearSampler, null ) );
		DisposalManager.Add( () => Apis.Vk.DestroySampler( LogicalDevice, nearestSampler, null ) );
	}

	private void InitializeImGui( IInputContext input )
	{
		ImGuiController = new ImGuiController( this, input );
	}

	private void LoadImages()
	{
		void LoadTexture( string texturePath )
		{
			ArgumentNullException.ThrowIfNull( DisposalManager, nameof( DisposalManager ) );

			var latteTexture = LatteTexture.FromPath( texturePath );
			var texture = new Texture( (uint)latteTexture.Width, (uint)latteTexture.Height, (uint)latteTexture.BytesPerPixel, latteTexture.PixelData );
			UploadTexture( texture );

			var imageViewInfo = VkInfo.ImageView( Format.R8G8B8A8Srgb, texture.GpuTexture.Image, ImageAspectFlags.ColorBit );
			Apis.Vk.CreateImageView( LogicalDevice, imageViewInfo, null, out var imageView ).Verify();
			VkInvalidHandleException.ThrowIfInvalid( imageView );
			texture.TextureView = imageView;

			Textures.Add( Path.GetFileNameWithoutExtension( texturePath ).ToLower(), texture );

			DisposalManager.Add( () => Apis.Vk.DestroyImageView( LogicalDevice, imageView, null ) );
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
		ArgumentNullException.ThrowIfNull( DescriptorAllocator, nameof( DescriptorAllocator ) );
		ArgumentNullException.ThrowIfNull( DisposalManager, nameof( DisposalManager ) );

		var defaultMaterial = GetMaterial( TexturedMeshMaterialName );
		foreach ( var (textureName, texture) in Textures )
		{
			var texturedMaterial = defaultMaterial.Clone();
			texturedMaterial.TextureSet = DescriptorAllocator.Allocate( new ReadOnlySpan<DescriptorSetLayout>( ref singleTextureSetLayout ) );
			VkInvalidHandleException.ThrowIfInvalid( texturedMaterial.TextureSet );

			new VkDescriptorUpdater( LogicalDevice, 1 )
				.WriteImage( 0, DescriptorType.CombinedImageSampler, texture.TextureView, nearestSampler, ImageLayout.ShaderReadOnlyOptimal )
				.Update( texturedMaterial.TextureSet )
				.Dispose();

			Materials.Add( textureName, texturedMaterial );
			DisposalManager.Add( () => RemoveMaterial( textureName ), SwapchainTag, WireframeTag );
		}
	}

	private void InitializeQueries()
	{
		ArgumentNullException.ThrowIfNull( DisposalManager, nameof( DisposalManager ) );

		var gpuExecutePoolInfo = new QueryPoolCreateInfo
		{
			SType = StructureType.QueryPoolCreateInfo,
			PNext = null,
			QueryCount = 2,
			QueryType = QueryType.Timestamp,
			PipelineStatistics = QueryPipelineStatisticFlags.None,
			Flags = 0
		};

		Apis.Vk.CreateQueryPool( LogicalDevice, gpuExecutePoolInfo, null, out var queryPool );
		VkInvalidHandleException.ThrowIfInvalid( queryPool );
		gpuExecuteQueryPool = queryPool;

		ImmediateSubmit( cmd =>
		{
			Apis.Vk.CmdResetQueryPool( cmd, queryPool, 0, 2 );
		} );

		DisposalManager.Add( () => Apis.Vk.DestroyQueryPool( LogicalDevice, gpuExecuteQueryPool, null ), SwapchainTag, WireframeTag );

		foreach ( var (_, material) in Materials )
		{
			var poolInfo = new QueryPoolCreateInfo
			{
				SType = StructureType.QueryPoolCreateInfo,
				PNext = null,
				QueryCount = 1,
				QueryType = QueryType.PipelineStatistics,
				PipelineStatistics =
					QueryPipelineStatisticFlags.InputAssemblyVerticesBit |
					QueryPipelineStatisticFlags.InputAssemblyPrimitivesBit |
					QueryPipelineStatisticFlags.VertexShaderInvocationsBit |
					QueryPipelineStatisticFlags.FragmentShaderInvocationsBit |
					QueryPipelineStatisticFlags.ClippingInvocationsBit |
					QueryPipelineStatisticFlags.ClippingPrimitivesBit,
				Flags = 0,
			};

			Apis.Vk.CreateQueryPool( LogicalDevice, poolInfo, null, out queryPool );
			VkInvalidHandleException.ThrowIfInvalid( queryPool );
			material.PipelineQueryPool = queryPool;

			ImmediateSubmit( cmd =>
			{
				Apis.Vk.CmdResetQueryPool( cmd, queryPool, 0, 1 );
			} );

			DisposalManager.Add( () => Apis.Vk.DestroyQueryPool( LogicalDevice, material.PipelineQueryPool, null ), SwapchainTag, WireframeTag );
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
		Apis.Vk.WaitForFences( LogicalDevice, 1, uploadContext.UploadFence, Vk.True, 999_999_999_999 ).Verify();
		Apis.Vk.ResetFences( LogicalDevice, 1, uploadContext.UploadFence ).Verify();
		Apis.Vk.ResetCommandPool( LogicalDevice, uploadContext.CommandPool, CommandPoolResetFlags.None ).Verify();
	}

	internal Shader CreateShader( string name, LatteShader latteShader )
	{
		if ( Shaders.ContainsKey( name ) )
			throw new ArgumentException( $"A shader with the name \"{name}\" already exists", nameof( name ) );

		var shader = new Shader( LogicalDevice, latteShader.Code, latteShader.EntryPoint );
		if ( !TryLoadShaderModule( shader.Code.Span, out var shaderModule ) )
			throw new VkException( $"Failed to load {name} shader" );

		shader.Module = shaderModule;
		Shaders.Add( name, shader );
		return shader;
	}

	internal Shader GetShader( string name )
	{
		if ( Shaders.TryGetValue( name, out var shader ) )
			return shader;

		throw new ArgumentException( $"A shader with the name \"{name}\" does not exist", nameof( name ) );
	}

	internal Material CreateMaterial( string name, Pipeline pipeline, PipelineLayout pipelineLayout )
	{
		if ( Materials.ContainsKey( name ) )
			throw new ArgumentException( $"A material with the name \"{name}\" already exists", nameof( name ) );

		var material = new Material( pipeline, pipelineLayout );
		Materials.Add( name, material );
		return material;
	}

	internal void RemoveMaterial( string name )
	{
		if ( !Materials.ContainsKey( name ) )
			throw new ArgumentException( $"No material with the name \"{name}\" exists", nameof( name ) );

		Materials.Remove( name );
	}

	internal Material GetMaterial( string name )
	{
		if ( Materials.TryGetValue( name, out var material ) )
			return material;

		throw new ArgumentException( $"A material with the name \"{name}\" does not exist", nameof( name ) );
	}

	internal bool TryGetMaterial( string name, [NotNullWhen( true )] out Material? material )
	{
		return Materials.TryGetValue( name, out material );
	}

	internal Mesh GetMesh( string name )
	{
		if ( Meshes.TryGetValue( name, out var mesh ) )
			return mesh;

		throw new ArgumentException( $"A mesh with the name \"{name}\" does not exist", nameof( name ) );
	}

	internal bool TryGetMesh( string name, [NotNullWhen( true )] out Mesh? mesh )
	{
		return Meshes.TryGetValue( name, out mesh );
	}

	private VkStatistics GetStatistics()
	{
		var statsStorage = (ulong*)Marshal.AllocHGlobal( 6 * sizeof( ulong ) );

		var result = Apis.Vk.GetQueryPoolResults( LogicalDevice, gpuExecuteQueryPool, 0, 2,
			2 * sizeof( ulong ),
			statsStorage,
			sizeof( ulong ),
			QueryResultFlags.Result64Bit );

		if ( result == Result.Success )
			gpuExecuteTime = TimeSpan.FromMicroseconds( (statsStorage[1] - statsStorage[0]) * physicalDeviceProperties.Limits.TimestampPeriod / 1000 );
		else if ( result != Result.NotReady )
			result.Verify();

		foreach ( var (materialName, material) in Materials )
		{
			result = Apis.Vk.GetQueryPoolResults( LogicalDevice, material.PipelineQueryPool, 0, 1,
				6 * sizeof( ulong ),
				statsStorage,
				6 * sizeof( ulong ),
				QueryResultFlags.Result64Bit );

			if ( result == Result.NotReady )
				continue;
			else
				result.Verify();

			materialPipelineStatistics[materialName] = new PipelineStatistics(
				statsStorage[0],
				statsStorage[1],
				statsStorage[2],
				statsStorage[3],
				statsStorage[4],
				statsStorage[5] );
		}

		Marshal.FreeHGlobal( (nint)statsStorage );
		return new VkStatistics( cpuPerformanceTimes, gpuExecuteTime, materialPipelineStatistics );
	}

	private void UploadMesh( Mesh mesh, SharingMode sharingMode = SharingMode.Exclusive )
	{
		ArgumentNullException.ThrowIfNull( AllocationManager, nameof( AllocationManager ) );
		ArgumentNullException.ThrowIfNull( DisposalManager, nameof( DisposalManager ) );

		// Vertex buffer
		{
			var bufferSize = (ulong)(mesh.Vertices.Length * Unsafe.SizeOf<Vertex>());

			var stagingBuffer = CreateBuffer( bufferSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit );
			AllocationManager.SetMemory( stagingBuffer.Allocation, mesh.Vertices.AsSpan() );
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

			Apis.Vk.DestroyBuffer( LogicalDevice, stagingBuffer.Buffer, null );
			DisposalManager.Add( () => Apis.Vk.DestroyBuffer( LogicalDevice, vertexBuffer.Buffer, null ) );
		}

		// Index buffer
		if ( mesh.Indices.Length == 0 )
			return;

		{
			var bufferSize = sizeof( uint ) * (ulong)mesh.Indices.Length;

			var stagingBuffer = CreateBuffer( bufferSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit );
			AllocationManager.SetMemory( stagingBuffer.Allocation, mesh.Indices.AsSpan() );
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

			Apis.Vk.DestroyBuffer( LogicalDevice, stagingBuffer.Buffer, null );
			DisposalManager.Add( () => Apis.Vk.DestroyBuffer( LogicalDevice, indexBuffer.Buffer, null ) );
		}
	}

	private void UploadTexture( Texture texture )
	{
		ArgumentNullException.ThrowIfNull( AllocationManager, nameof( AllocationManager ) );
		ArgumentNullException.ThrowIfNull( DisposalManager, nameof( DisposalManager ) );

		var imageSize = texture.Width * texture.Height * texture.BytesPerPixel;
		var imageFormat = Format.R8G8B8A8Srgb;

		var stagingBuffer = CreateBuffer( imageSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit );
		AllocationManager.SetMemory( stagingBuffer.Allocation, texture.PixelData.Span );

		var imageExtent = new Extent3D( texture.Width, texture.Height, 1 );
		var imageInfo = VkInfo.Image( imageFormat, ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit, imageExtent );
		Apis.Vk.CreateImage( LogicalDevice, imageInfo, null, out var textureImage );

		var allocatedTextureImage = AllocationManager.AllocateImage( textureImage, MemoryPropertyFlags.DeviceLocalBit );
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

		Apis.Vk.DestroyBuffer( LogicalDevice, stagingBuffer.Buffer, null );
		DisposalManager.Add( () => Apis.Vk.DestroyImage( LogicalDevice, textureImage, null ) );
	}

	private AllocatedBuffer CreateBuffer( ulong size, BufferUsageFlags usageFlags, MemoryPropertyFlags memoryFlags,
		SharingMode sharingMode = SharingMode.Exclusive )
	{
		ArgumentNullException.ThrowIfNull( AllocationManager, nameof( AllocationManager ) );

		var createInfo = VkInfo.Buffer( size, usageFlags, sharingMode );
		Apis.Vk.CreateBuffer( LogicalDevice, createInfo, null, out var buffer );

		return AllocationManager.AllocateBuffer( buffer, memoryFlags );
	}

	private bool TryLoadShaderModule( ReadOnlySpan<byte> shaderBytes, out ShaderModule shaderModule )
	{
		fixed ( byte* shaderBytesPtr = shaderBytes )
		{
			var createInfo = VkInfo.ShaderModule( (nuint)shaderBytes.Length, shaderBytesPtr, ShaderModuleCreateFlags.None );
			var result = Apis.Vk.CreateShaderModule( LogicalDevice, createInfo, null, out shaderModule );

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

		ArgumentNullException.ThrowIfNull( View, nameof( View ) );
		View.FramebufferResize -= OnFramebufferResize;

		if ( disposing )
		{
		}

		AllocationManager?.Dispose();
		DescriptorAllocator?.Dispose();
		ImGuiController?.Dispose();
		DisposalManager?.Dispose();
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

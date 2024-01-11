using ImGuiNET;
using Latte.Assets;
using Latte.NewRenderer.Vulkan.Allocations;
using Latte.NewRenderer.Vulkan.Builders;
using Latte.NewRenderer.Vulkan.Exceptions;
using Latte.NewRenderer.Vulkan.Extensions;
using Latte.NewRenderer.Vulkan.ImGui;
using Latte.NewRenderer.Vulkan.Temp;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
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
using System.Runtime.InteropServices;
using LatteShader = Latte.Assets.Shader;
using LatteTexture = Latte.Assets.Texture;
using Mesh = Latte.NewRenderer.Vulkan.Temp.Mesh;
using Shader = Latte.NewRenderer.Vulkan.Temp.Shader;
using Texture = Latte.NewRenderer.Vulkan.Temp.Texture;

namespace Latte.NewRenderer.Vulkan;

internal unsafe sealed class VkEngine : IDisposable
{
	private const int MaxFramesInFlight = 2;
	private const int MaxObjects = 10_000;
	private const int MaxLights = 10;
	private const string DefaultMeshMaterialName = "defaultmesh";
	private const string TexturedMeshMaterialName = "texturedmesh";
	private const string BillboardMaterialName = "billboard";
	private const string SwapchainTag = "swapchain";
	private const string WireframeTag = "wireframe";

	internal bool WireframeEnabled
	{
		get => wireframeEnabled;
		set
		{
			if ( wireframeEnabled == value )
				return;

			wireframeEnabled = value;
			RecreateWireframe();
		}
	}
	private bool wireframeEnabled;

	internal bool VsyncEnabled
	{
		get => vsyncEnabled;
		set
		{
			if ( vsyncEnabled == value )
				return;

			vsyncEnabled = value;
			RecreateVsync();
		}
	}
	private bool vsyncEnabled = true;

	internal bool IsVisible => View.Size.X != 0 && View.Size.Y != 0;

	internal ImGuiController ImGuiController { get; private set; } = null!;

	internal IView View { get; private set; } = null!;
	private SurfaceKHR surface;
	private DisposalManager disposalManager = null!;

	private SwapchainKHR swapchain;
	internal Format SwapchainImageFormat { get; private set; }
	private ImmutableArray<Image> swapchainImages = [];
	private ImmutableArray<ImageView> swapchainImageViews = [];
	internal int SwapchainImageCount => swapchainImages.Length;

	private AllocatedImage depthImage;
	internal Format DepthFormat { get; private set; }
	private ImageView depthImageView;

	private ImmutableArray<FrameData> frameData = [];
	private FrameData CurrentFrameData => frameData[frameNumber % MaxFramesInFlight];

	internal DescriptorAllocator DescriptorAllocator { get; private set; } = null!;
	private DescriptorSetLayout frameSetLayout;
	private DescriptorSetLayout singleTextureSetLayout;

	private RenderPass renderPass;
	private ImmutableArray<Framebuffer> framebuffers;

	private Sampler linearSampler;
	private Sampler nearestSampler;

	private GpuSceneData sceneParameters;
	private AllocatedBuffer sceneParameterBuffer;
	private int frameNumber;

	private readonly Dictionary<string, TimeSpan> initializationStageTimes = [];
	private readonly Dictionary<string, TimeSpan> cpuPerformanceTimes = [];
	private readonly Dictionary<string, VkPipelineStatistics> materialPipelineStatistics = [];
	private QueryPool gpuExecuteQueryPool;
	private TimeSpan gpuExecuteTime;

	private readonly List<Light> Lights = [];
	private readonly List<Renderable> Renderables = [];
	private readonly Dictionary<string, Material> Materials = [];
	private readonly Dictionary<string, Mesh> Meshes = [];
	private readonly Dictionary<string, Shader> Shaders = [];
	private readonly Dictionary<string, Texture> Textures = [];
	private readonly Dictionary<VkQueue, UploadContext> uploadContexts = [];
	private readonly GpuObjectData[] objectData = new GpuObjectData[MaxObjects];
	private readonly GpuLightData[] lightData = new GpuLightData[MaxLights];

	private bool waitingForIdle;
	private bool disposed;

	internal VkEngine( IView view, IInputContext input )
	{
		Initialize( view, input );
	}

	~VkEngine()
	{
		Dispose( disposing: false );
	}

	private void Initialize( IView view, IInputContext input )
	{
		View = view;
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
	}

	internal void Draw()
	{
		ObjectDisposedException.ThrowIf( disposed, this );

		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		var drawProfile = CpuProfile.New( "Total" );

		if ( !IsVisible || waitingForIdle )
			return;

		var swapchain = this.swapchain;
		var currentFrameData = CurrentFrameData;
		var renderFence = currentFrameData.RenderFence;
		var presentSemaphore = currentFrameData.PresentSemaphore;
		var renderSemaphore = currentFrameData.RenderSemaphore;
		var cmd = currentFrameData.CommandBuffer;

		var waitForRenderProfile = CpuProfile.New( "Wait for last render" );
		using ( waitForRenderProfile )
			Apis.Vk.WaitForFences( VkContext.LogicalDevice, 1, renderFence, true, 1_000_000_000 ).AssertSuccess();
		cpuPerformanceTimes[waitForRenderProfile.Name] = waitForRenderProfile.Time;

		uint swapchainImageIndex;
		var acquireSwapchainImageProfile = CpuProfile.New( "Acquire swapchain image" );
		using ( acquireSwapchainImageProfile )
		{
			if ( !VkContext.Extensions.TryGetExtension<KhrSwapchain>( out var swapchainExtension ) )
				throw new VkException( $"Failed to get {KhrSwapchain.ExtensionName} extension" );

			var acquireResult = swapchainExtension.AcquireNextImage( VkContext.LogicalDevice, swapchain, 1_000_000_000, presentSemaphore, default, &swapchainImageIndex );
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

			Apis.Vk.ResetFences( VkContext.LogicalDevice, 1, renderFence ).AssertSuccess();
		}
		cpuPerformanceTimes[acquireSwapchainImageProfile.Name] = acquireSwapchainImageProfile.Time;

		var recordProfile = CpuProfile.New( "Record command buffer" );
		using ( recordProfile )
		{
			Apis.Vk.ResetCommandBuffer( cmd, CommandBufferResetFlags.None ).AssertSuccess();

			var beginInfo = VkInfo.BeginCommandBuffer( CommandBufferUsageFlags.OneTimeSubmitBit );
			Apis.Vk.BeginCommandBuffer( cmd, beginInfo ).AssertSuccess();

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

			ImGuiController.Render( cmd, framebuffers[(int)swapchainImageIndex], new Extent2D( (uint)View.Size.X, (uint)View.Size.Y ) );

			Apis.Vk.CmdEndRenderPass( cmd );

			Apis.Vk.CmdWriteTimestamp( cmd, PipelineStageFlags.BottomOfPipeBit, gpuExecuteQueryPool, 1 );

			Apis.Vk.EndCommandBuffer( cmd ).AssertSuccess();
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

			VkContext.GraphicsQueue.SubmitAndWait( submitInfo, renderFence );
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

			// FIXME: How the fuck does this work.
			VkContext.PresentQueue.SubmitPresentAndWait( presentInfo );
		}
		cpuPerformanceTimes[presentProfile.Name] = presentProfile.Time;

		frameNumber++;
		drawProfile.Dispose();
		cpuPerformanceTimes[drawProfile.Name] = drawProfile.Time;
	}

	private void DrawObjects( CommandBuffer cmd, int first, int count )
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		var currentFrameData = CurrentFrameData;

		var view = Matrix4x4.Identity * Matrix4x4.CreateLookAt( Camera.Main.Position, Camera.Main.Position + Camera.Main.Front, Camera.Main.Up );
		var projection = Matrix4x4.CreatePerspectiveFieldOfView( Scalar.DegreesToRadians( Camera.Main.Zoom ),
			(float)View.Size.X / View.Size.Y,
			Camera.Main.ZNear, Camera.Main.ZFar );
		projection.M22 *= -1;

		var cameraData = new GpuCameraData
		{
			View = view,
			Projection = projection,
			ViewProjection = view * projection
		};
		VkContext.AllocationManager.SetMemory( currentFrameData.CameraBuffer.Allocation, cameraData, true );

		var frameIndex = frameNumber % frameData.Length;
		sceneParameters.LightCount = Lights.Count;
		VkContext.AllocationManager.SetMemory( sceneParameterBuffer.Allocation, sceneParameters, PadUniformBufferSize( (ulong)sizeof( GpuSceneData ) ), frameIndex );

		var objectData = this.objectData.AsSpan().Slice( first, count );
		for ( var i = 0; i < count; i++ )
			objectData[i] = new GpuObjectData( Renderables[first + i].Transform );

		VkContext.AllocationManager.SetMemory( currentFrameData.ObjectBuffer.Allocation, (ReadOnlySpan<GpuObjectData>)objectData, true );

		for ( var i = 0; i < Lights.Count; i++ )
		{
			var light = Lights[i];
			lightData[i] = new GpuLightData( light.Position, light.Color );
		}

		VkContext.AllocationManager.SetMemory( currentFrameData.LightBuffer.Allocation, (ReadOnlySpan<GpuLightData>)lightData, true );

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

				if ( material.PipelineQueryPool.IsValid() )
					Apis.Vk.CmdBeginQuery( cmd, material.PipelineQueryPool, 0, QueryControlFlags.None );

				if ( lastMaterial?.Pipeline.Handle != material.Pipeline.Handle )
				{
					Apis.Vk.CmdBindPipeline( cmd, PipelineBindPoint.Graphics, material.Pipeline );

					var uniformOffset = (uint)(PadUniformBufferSize( (ulong)sizeof( GpuSceneData ) ) * (ulong)frameIndex);
					Apis.Vk.CmdBindDescriptorSets( cmd, PipelineBindPoint.Graphics, material.PipelineLayout, 0, 1, currentFrameData.FrameDescriptor, 1, &uniformOffset );
				}

				if ( material.TextureSet.IsValid() && lastMaterial?.TextureSet.Handle != material.TextureSet.Handle )
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
		ObjectDisposedException.ThrowIf( disposed, this );

		waitingForIdle = true;
		Span<Fence> waitFences = stackalloc Fence[frameData.Length];
		for ( var i = 0; i < frameData.Length; i++ )
			waitFences[i] = frameData[i].RenderFence;

		Apis.Vk.WaitForFences( VkContext.LogicalDevice, waitFences, Vk.True, ulong.MaxValue ).AssertSuccess();
		waitingForIdle = false;
	}

	internal void ImGuiShowRendererStatistics()
	{
		ObjectDisposedException.ThrowIf( disposed, this );

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

		ImGuiNET.ImGui.Text( VkContext.PhysicalDeviceInfo.Name );
		ImGuiNET.ImGui.Text( $"Renderables: {Renderables.Count}/{MaxObjects}" );
		ImGuiNET.ImGui.Text( $"Lights: {Lights.Count}/{MaxLights}" );

		ImGuiNET.ImGui.SeparatorText( "Options" );

		var wireframeEnabled = WireframeEnabled;
		ImGuiNET.ImGui.Checkbox( "Wireframe Enabled", ref wireframeEnabled );
		WireframeEnabled = wireframeEnabled;

		var vsyncEnabled = VsyncEnabled;
		ImGuiNET.ImGui.Checkbox( "VSync Enabled", ref vsyncEnabled );
		VsyncEnabled = vsyncEnabled;

		var stats = GetStatistics();

		ImGuiNET.ImGui.SeparatorText( "Performance" );

		if ( ImGuiNET.ImGui.CollapsingHeader( "Initialization" ) && ImGuiNET.ImGui.BeginTable( "Initialization Timings", 2, ImGuiTableFlags.Borders ) )
		{
			ImGuiNET.ImGui.TableSetupColumn( "Section" );
			ImGuiNET.ImGui.TableSetupColumn( "Time (ms)" );
			ImGuiNET.ImGui.TableHeadersRow();

			ImGuiNET.ImGui.TableNextColumn();
			foreach ( var (timingName, time) in stats.InitializationTimings )
			{
				ImGuiNET.ImGui.Text( timingName );
				ImGuiNET.ImGui.TableNextColumn();
				ImGuiNET.ImGui.Text( $"{time.TotalMilliseconds:0.##}" );
				ImGuiNET.ImGui.TableNextColumn();
			}

			ImGuiNET.ImGui.EndTable();
		}

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
		
		if ( ImGuiNET.ImGui.CollapsingHeader( "Memory" ) )
		{
			if ( ImGuiNET.ImGui.BeginTable( "Allocation Counts", 2, ImGuiTableFlags.Borders ) )
			{
				ImGuiNET.ImGui.TableSetupColumn( "Memory Type Index" );
				ImGuiNET.ImGui.TableSetupColumn( "Allocation Count" );
				ImGuiNET.ImGui.TableHeadersRow();

				ImGuiNET.ImGui.TableNextColumn();

				for ( var i = 0; i < stats.MemoryTypeAllocationCounts.Length; i++ )
				{
					ImGuiNET.ImGui.Text( i.ToString() );
					ImGuiNET.ImGui.TableNextColumn();
					ImGuiNET.ImGui.Text( stats.MemoryTypeAllocationCounts[i].ToString() );
					ImGuiNET.ImGui.TableNextColumn();
				}

				ImGuiNET.ImGui.Text( "Total" );
				ImGuiNET.ImGui.TableNextColumn();
				ImGuiNET.ImGui.Text( stats.AllocationCount.ToString() );
				ImGuiNET.ImGui.TableNextColumn();

				ImGuiNET.ImGui.EndTable();
			}

			if ( ImGuiNET.ImGui.BeginTable( "Memory Usages", 2, ImGuiTableFlags.Borders ) )
			{
				ImGuiNET.ImGui.TableSetupColumn( "Memory Type Index" );
				ImGuiNET.ImGui.TableSetupColumn( "Allocated Size" );
				ImGuiNET.ImGui.TableHeadersRow();

				ImGuiNET.ImGui.TableNextColumn();

				var totalSize = 0ul;
				for ( var i = 0; i < stats.MemoryTypeAllocationSizes.Length; i++ )
				{
					ImGuiNET.ImGui.Text( i.ToString() );
					ImGuiNET.ImGui.TableNextColumn();
					ImGuiNET.ImGui.Text( stats.MemoryTypeAllocationSizes[i].ToDataSize( 2 ) );
					ImGuiNET.ImGui.TableNextColumn();

					totalSize += stats.MemoryTypeAllocationSizes[i];
				}

				ImGuiNET.ImGui.Text( "Total" );
				ImGuiNET.ImGui.TableNextColumn();
				ImGuiNET.ImGui.Text( totalSize.ToDataSize( 2 ) );
				ImGuiNET.ImGui.TableNextColumn();

				ImGuiNET.ImGui.EndTable();
			}
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
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		WaitForIdle();

		disposalManager.Dispose( SwapchainTag );
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
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		WaitForIdle();

		disposalManager.Dispose( WireframeTag );
		InitializePipelines();
		SetupTextureSets();
		InitializeQueries();
	}

	private void RecreateVsync() => RecreateSwapchain();

	private void InitializeVulkan()
	{
		var initializationProfile = CpuProfile.New( nameof( InitializeVulkan ) );

		surface = VkContext.Initialize( View );
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} failed to initialize" );

		disposalManager = new DisposalManager();

		var frameDataBuilder = ImmutableArray.CreateBuilder<FrameData>( MaxFramesInFlight );
		for ( var i = 0; i < MaxFramesInFlight; i++ )
			frameDataBuilder.Add( new FrameData() );
		frameData = frameDataBuilder.MoveToImmutable();

		foreach ( var queue in VkContext.GetAllQueues() )
			uploadContexts.Add( queue, new UploadContext() );

		if ( VkContext.Extensions.TryGetExtension<KhrSurface>( out var surfaceExtension ) )
			disposalManager.Add( () => surfaceExtension.DestroySurface( VkContext.Instance, surface, null ) );

		initializationProfile.Dispose();
		if ( !initializationStageTimes.ContainsKey( initializationProfile.Name ) )
			initializationStageTimes.Add( initializationProfile.Name, initializationProfile.Time );
	}

	private void InitializeSwapchain()
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		if ( !VkContext.Extensions.TryGetExtension<KhrSurface>( out var surfaceExtension ) )
			throw new VkException( $"Attempted to initialize swapchain while the {KhrSurface.ExtensionName} extension is disabled" );

		var initializationProfile = CpuProfile.New( nameof( InitializeSwapchain ) );

		var result = new VkSwapchainBuilder( VkContext.Instance, VkContext.PhysicalDevice, VkContext.LogicalDevice )
			.WithSurface( surface, surfaceExtension )
			.WithQueueFamilyIndices( VkContext.QueueFamilyIndices )
			.UseDefaultFormat()
			.SetPresentMode( VsyncEnabled ? PresentModeKHR.FifoKhr : PresentModeKHR.MailboxKhr )
			.SetExtent( (uint)View.Size.X, (uint)View.Size.Y )
			.Build();

		swapchain = result.Swapchain;
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
		Apis.Vk.CreateImage( VkContext.LogicalDevice, depthImageInfo, null, out var depthImage ).AssertSuccess();
		VkInvalidHandleException.ThrowIfInvalid( depthImage );
		this.depthImage = VkContext.AllocationManager.AllocateImage( depthImage, MemoryPropertyFlags.DeviceLocalBit );

		var depthImageViewInfo = VkInfo.ImageView( DepthFormat, depthImage, ImageAspectFlags.DepthBit );
		Apis.Vk.CreateImageView( VkContext.LogicalDevice, depthImageViewInfo, null, out var depthImageView ).AssertSuccess();
		VkInvalidHandleException.ThrowIfInvalid( depthImageView );
		this.depthImageView = depthImageView;

		if ( VkContext.Extensions.TryGetExtension<KhrSwapchain>( out var swapchainExtension ) )
			disposalManager.Add( () => swapchainExtension.DestroySwapchain( VkContext.LogicalDevice, swapchain, null ), SwapchainTag );

		for ( var i = 0; i < swapchainImageViews.Length; i++ )
		{
			var index = i;
			disposalManager.Add( () => Apis.Vk.DestroyImageView( VkContext.LogicalDevice, swapchainImageViews[index], null ), SwapchainTag );
		}

		disposalManager.Add( () => VkContext.AllocationManager.Free( this.depthImage.Allocation ), SwapchainTag );
		disposalManager.Add( () => Apis.Vk.DestroyImage( VkContext.LogicalDevice, depthImage, null ), SwapchainTag );
		disposalManager.Add( () => Apis.Vk.DestroyImageView( VkContext.LogicalDevice, depthImageView, null ), SwapchainTag );

		initializationProfile.Dispose();
		if ( !initializationStageTimes.ContainsKey( initializationProfile.Name ) )
			initializationStageTimes.Add( initializationProfile.Name, initializationProfile.Time );
	}

	private void InitializeCommands()
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		var initializationProfile = CpuProfile.New( nameof( InitializeCommands ) );

		var poolCreateInfo = VkInfo.CommandPool( VkContext.QueueFamilyIndices.GraphicsQueue, CommandPoolCreateFlags.ResetCommandBufferBit );

		for ( var i = 0; i < MaxFramesInFlight; i++ )
		{
			Apis.Vk.CreateCommandPool( VkContext.LogicalDevice, poolCreateInfo, null, out var commandPool ).AssertSuccess();
			var bufferAllocateInfo = VkInfo.AllocateCommandBuffer( commandPool, 1, CommandBufferLevel.Primary );
			Apis.Vk.AllocateCommandBuffers( VkContext.LogicalDevice, bufferAllocateInfo, out var commandBuffer ).AssertSuccess();

			VkInvalidHandleException.ThrowIfInvalid( commandPool );
			VkInvalidHandleException.ThrowIfInvalid( commandBuffer );

			frameData[i].CommandPool = commandPool;
			frameData[i].CommandBuffer = commandBuffer;

			disposalManager.Add( () => Apis.Vk.DestroyCommandPool( VkContext.LogicalDevice, commandPool, null ) );
		}

		foreach ( var queue in VkContext.GetAllQueues() )
		{
			Apis.Vk.CreateCommandPool( VkContext.LogicalDevice, poolCreateInfo, null, out var uploadCommandPool ).AssertSuccess();
			VkInvalidHandleException.ThrowIfInvalid( uploadCommandPool );
			uploadContexts[queue].CommandPool = uploadCommandPool;

			var uploadBufferAllocateInfo = VkInfo.AllocateCommandBuffer( uploadCommandPool, 1, CommandBufferLevel.Primary );
			Apis.Vk.AllocateCommandBuffers( VkContext.LogicalDevice, uploadBufferAllocateInfo, out var uploadCommandBuffer ).AssertSuccess();
			VkInvalidHandleException.ThrowIfInvalid( uploadCommandBuffer );
			uploadContexts[queue].CommandBuffer = uploadCommandBuffer;

			disposalManager.Add( () => Apis.Vk.DestroyCommandPool( VkContext.LogicalDevice, uploadCommandPool, null ) );
		}

		initializationProfile.Dispose();
		if ( !initializationStageTimes.ContainsKey( initializationProfile.Name ) )
			initializationStageTimes.Add( initializationProfile.Name, initializationProfile.Time );
	}

	private void InitializeDefaultRenderPass()
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		var initializationProfile = CpuProfile.New( nameof( InitializeDefaultRenderPass ) );

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

		Apis.Vk.CreateRenderPass( VkContext.LogicalDevice, createInfo, null, out var renderPass ).AssertSuccess();

		VkInvalidHandleException.ThrowIfInvalid( renderPass );
		this.renderPass = renderPass;
		disposalManager.Add( () => Apis.Vk.DestroyRenderPass( VkContext.LogicalDevice, renderPass, null ) );

		initializationProfile.Dispose();
		if ( !initializationStageTimes.ContainsKey( initializationProfile.Name ) )
			initializationStageTimes.Add( initializationProfile.Name, initializationProfile.Time );
	}

	private void InitializeFramebuffers()
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		var initializationProfile = CpuProfile.New( nameof( InitializeFramebuffers ) );

		var framebufferBuilder = ImmutableArray.CreateBuilder<Framebuffer>( swapchainImages.Length );
		Span<ImageView> imageViews = stackalloc ImageView[2];
		imageViews[1] = depthImageView;

		var createInfo = VkInfo.Framebuffer( renderPass, (uint)View.Size.X, (uint)View.Size.Y, imageViews );
		for ( var i = 0; i < swapchainImages.Length; i++ )
		{
			imageViews[0] = swapchainImageViews[i];
			Apis.Vk.CreateFramebuffer( VkContext.LogicalDevice, createInfo, null, out var framebuffer ).AssertSuccess();

			VkInvalidHandleException.ThrowIfInvalid( framebuffer );
			framebufferBuilder.Add( framebuffer );
			disposalManager.Add( () => Apis.Vk.DestroyFramebuffer( VkContext.LogicalDevice, framebuffer, null ), SwapchainTag );
		}

		framebuffers = framebufferBuilder.MoveToImmutable();

		initializationProfile.Dispose();
		if ( !initializationStageTimes.ContainsKey( initializationProfile.Name ) )
			initializationStageTimes.Add( initializationProfile.Name, initializationProfile.Time );
	}

	private void InitializeSynchronizationStructures()
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		var initializationProfile = CpuProfile.New( nameof( InitializeSynchronizationStructures ) );

		var fenceCreateInfo = VkInfo.Fence( FenceCreateFlags.SignaledBit );
		var semaphoreCreateInfo = VkInfo.Semaphore();

		for ( var i = 0; i < frameData.Length; i++ )
		{
			Apis.Vk.CreateFence( VkContext.LogicalDevice, fenceCreateInfo, null, out var renderFence ).AssertSuccess();
			Apis.Vk.CreateSemaphore( VkContext.LogicalDevice, semaphoreCreateInfo, null, out var presentSemaphore ).AssertSuccess();
			Apis.Vk.CreateSemaphore( VkContext.LogicalDevice, semaphoreCreateInfo, null, out var renderSemaphore ).AssertSuccess();

			VkInvalidHandleException.ThrowIfInvalid( renderFence );
			VkInvalidHandleException.ThrowIfInvalid( presentSemaphore );
			VkInvalidHandleException.ThrowIfInvalid( renderSemaphore );

			frameData[i].RenderFence = renderFence;
			frameData[i].PresentSemaphore = presentSemaphore;
			frameData[i].RenderSemaphore = renderSemaphore;

			disposalManager.Add( () => Apis.Vk.DestroySemaphore( VkContext.LogicalDevice, renderSemaphore, null ) );
			disposalManager.Add( () => Apis.Vk.DestroySemaphore( VkContext.LogicalDevice, presentSemaphore, null ) );
			disposalManager.Add( () => Apis.Vk.DestroyFence( VkContext.LogicalDevice, renderFence, null ) );
		}

		fenceCreateInfo.Flags = FenceCreateFlags.None;
		foreach ( var queue in VkContext.GetAllQueues() )
		{
			Apis.Vk.CreateFence( VkContext.LogicalDevice, fenceCreateInfo, null, out var uploadFence ).AssertSuccess();
			VkInvalidHandleException.ThrowIfInvalid( uploadFence );
			uploadContexts[queue].UploadFence = uploadFence;

			disposalManager.Add( () => Apis.Vk.DestroyFence( VkContext.LogicalDevice, uploadFence, null ) );
		}

		initializationProfile.Dispose();
		if ( !initializationStageTimes.ContainsKey( initializationProfile.Name ) )
			initializationStageTimes.Add( initializationProfile.Name, initializationProfile.Time );
	}

	private void InitializeDescriptors()
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		var initializationProfile = CpuProfile.New( nameof( InitializeDescriptors ) );

		DescriptorAllocator = new DescriptorAllocator( VkContext.LogicalDevice, 100,
		[
			new( DescriptorType.UniformBuffer, 2 ),
			new( DescriptorType.UniformBufferDynamic, 1 ),
			new( DescriptorType.StorageBuffer, 1 ),
			new( DescriptorType.CombinedImageSampler, 4 )
		] );

		var layoutBuilder = new VkDescriptorSetLayoutBuilder( VkContext.LogicalDevice, 4 );

		frameSetLayout = layoutBuilder
			.AddBinding( 0, DescriptorType.UniformBuffer, ShaderStageFlags.VertexBit )
			.AddBinding( 1, DescriptorType.UniformBufferDynamic, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit )
			.AddBinding( 2, DescriptorType.StorageBuffer, ShaderStageFlags.VertexBit )
			.AddBinding( 3, DescriptorType.StorageBuffer, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit )
			.Build();
		VkInvalidHandleException.ThrowIfInvalid( frameSetLayout );

		singleTextureSetLayout = layoutBuilder.Clear()
			.AddBinding( 0, DescriptorType.CombinedImageSampler, ShaderStageFlags.FragmentBit )
			.Build();
		VkInvalidHandleException.ThrowIfInvalid( singleTextureSetLayout );

		var sceneParameterBufferSize = (ulong)frameData.Length * PadUniformBufferSize( (ulong)sizeof( GpuSceneData ) );
		sceneParameterBuffer = CreateBuffer( sceneParameterBufferSize, BufferUsageFlags.UniformBufferBit,
			MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.DeviceLocalBit );

		disposalManager.Add( () => Apis.Vk.DestroyDescriptorSetLayout( VkContext.LogicalDevice, frameSetLayout, null ) );
		disposalManager.Add( () => Apis.Vk.DestroyDescriptorSetLayout( VkContext.LogicalDevice, singleTextureSetLayout, null ) );
		disposalManager.Add( () => VkContext.AllocationManager.Free( sceneParameterBuffer.Allocation ) );
		disposalManager.Add( () => Apis.Vk.DestroyBuffer( VkContext.LogicalDevice, sceneParameterBuffer.Buffer, null ) );

		for ( var i = 0; i < frameData.Length; i++ )
		{
			frameData[i].FrameDescriptor = DescriptorAllocator.Allocate( new ReadOnlySpan<DescriptorSetLayout>( ref frameSetLayout ) );
			VkInvalidHandleException.ThrowIfInvalid( frameData[i].FrameDescriptor );

			frameData[i].CameraBuffer = CreateBuffer( (ulong)sizeof( GpuCameraData ), BufferUsageFlags.UniformBufferBit,
				MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.DeviceLocalBit );

			frameData[i].ObjectBuffer = CreateBuffer( (ulong)sizeof( GpuObjectData ) * MaxObjects, BufferUsageFlags.StorageBufferBit,
				MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.DeviceLocalBit );

			frameData[i].LightBuffer = CreateBuffer( (ulong)sizeof( GpuLightData ) * MaxLights, BufferUsageFlags.StorageBufferBit,
				MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.DeviceLocalBit );

			var index = i;
			disposalManager.Add( () => VkContext.AllocationManager.Free( frameData[index].CameraBuffer.Allocation ) );
			disposalManager.Add( () => Apis.Vk.DestroyBuffer( VkContext.LogicalDevice, frameData[index].CameraBuffer.Buffer, null ) );
			disposalManager.Add( () => VkContext.AllocationManager.Free( frameData[index].ObjectBuffer.Allocation ) );
			disposalManager.Add( () => Apis.Vk.DestroyBuffer( VkContext.LogicalDevice, frameData[index].ObjectBuffer.Buffer, null ) );
			disposalManager.Add( () => VkContext.AllocationManager.Free( frameData[index].LightBuffer.Allocation ) );
			disposalManager.Add( () => Apis.Vk.DestroyBuffer( VkContext.LogicalDevice, frameData[index].LightBuffer.Buffer, null ) );

			new VkDescriptorUpdater( VkContext.LogicalDevice, 4 )
				.WriteBuffer( 0, DescriptorType.UniformBuffer, frameData[i].CameraBuffer.Buffer, 0, (ulong)sizeof( GpuCameraData ) )
				.WriteBuffer( 1, DescriptorType.UniformBufferDynamic, sceneParameterBuffer.Buffer, 0, (ulong)sizeof( GpuSceneData ) )
				.WriteBuffer( 2, DescriptorType.StorageBuffer, frameData[i].ObjectBuffer.Buffer, 0, (ulong)sizeof( GpuObjectData ) * MaxObjects )
				.WriteBuffer( 3, DescriptorType.StorageBuffer, frameData[i].LightBuffer.Buffer, 0, (ulong)sizeof( GpuLightData ) * MaxLights )
				.Update( frameData[i].FrameDescriptor )
				.Dispose();
		}

		initializationProfile.Dispose();
		if ( !initializationStageTimes.ContainsKey( initializationProfile.Name ) )
			initializationStageTimes.Add( initializationProfile.Name, initializationProfile.Time );
	}

	private void InitializeShaders()
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		var initializationProfile = CpuProfile.New( nameof( InitializeShaders ) );

		var meshTriangleShader = CreateShader( "mesh_triangle.vert", LatteShader.FromPath( "/Assets/Shaders/mesh_triangle.vert.spv" ) );
		var defaultLitShader = CreateShader( "default_lit.frag", LatteShader.FromPath( "/Assets/Shaders/default_lit.frag.spv" ) );
		var texturedLitShader = CreateShader( "textured_lit.frag", LatteShader.FromPath( "/Assets/Shaders/textured_lit.frag.spv" ) );
		var billboardVertShader = CreateShader( "default_billboard.vert", LatteShader.FromPath( "/Assets/Shaders/default_billboard.vert.spv" ) );
		var billboardFragShader = CreateShader( "default_billboard.frag", LatteShader.FromPath( "/Assets/Shaders/default_billboard.frag.spv" ) );

		disposalManager.Add( meshTriangleShader.Dispose );
		disposalManager.Add( defaultLitShader.Dispose );
		disposalManager.Add( texturedLitShader.Dispose );
		disposalManager.Add( billboardVertShader.Dispose );
		disposalManager.Add( billboardFragShader.Dispose );

		initializationProfile.Dispose();
		if ( !initializationStageTimes.ContainsKey( initializationProfile.Name ) )
			initializationStageTimes.Add( initializationProfile.Name, initializationProfile.Time );
	}

	private void InitializePipelines()
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		var initializationProfile = CpuProfile.New( nameof( InitializePipelines ) );

		var meshTriangleShader = GetShader( "mesh_triangle.vert" );
		var defaultLitShader = GetShader( "default_lit.frag" );
		var texturedLitShader = GetShader( "textured_lit.frag" );
		var billboardVertShader = GetShader( "default_billboard.vert" );
		var billboardFragShader = GetShader( "default_billboard.frag" );

		var pipelineLayoutBuilder = new VkPipelineLayoutBuilder( VkContext.LogicalDevice, 0, 2 );
		var meshPipelineLayout = pipelineLayoutBuilder
			.AddDescriptorSetLayout( frameSetLayout )
			.Build();
		VkInvalidHandleException.ThrowIfInvalid( meshPipelineLayout );

		var shaderSpecializationEntries = stackalloc SpecializationMapEntry[]
		{
			// MaxObjects
			new SpecializationMapEntry
			{
				ConstantID = 0,
				Offset = 0,
				Size = sizeof( int )
			},
			// MaxLights
			new SpecializationMapEntry
			{
				ConstantID = 1,
				Offset = sizeof( int ),
				Size = sizeof( int )
			}
		};

		var shaderSpecializationData = stackalloc int[]
		{
			MaxObjects,
			MaxLights
		};

		var shaderSpecializationInfo = stackalloc SpecializationInfo[]
		{
			new SpecializationInfo
			{
				MapEntryCount = 2,
				PMapEntries = shaderSpecializationEntries,
				DataSize = sizeof( int ) * 2,
				PData = shaderSpecializationData
			}
		};

		var pipelineBuilder = new VkPipelineBuilder( VkContext.LogicalDevice, renderPass )
			.WithPipelineLayout( meshPipelineLayout )
			.WithViewport( new Viewport( 0, 0, View.Size.X, View.Size.Y, 0, 1 ) )
			.WithScissor( new Rect2D( new Offset2D( 0, 0 ), new Extent2D( (uint)View.Size.X, (uint)View.Size.Y ) ) )
			.AddShaderStage( VkInfo.PipelineShaderStage( ShaderStageFlags.VertexBit, meshTriangleShader.Module,
				(byte*)meshTriangleShader.EntryPointPtr, shaderSpecializationInfo ) )
			.AddShaderStage( VkInfo.PipelineShaderStage( ShaderStageFlags.FragmentBit, defaultLitShader.Module,
				(byte*)defaultLitShader.EntryPointPtr, shaderSpecializationInfo ) )
			.WithVertexInputState( VkInfo.PipelineVertexInputState( VkVertexInputDescription.GetLatteVertexDescription() ) )
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
			.AddShaderStage( VkInfo.PipelineShaderStage( ShaderStageFlags.VertexBit, meshTriangleShader.Module,
				(byte*)meshTriangleShader.EntryPointPtr, shaderSpecializationInfo ) )
			.AddShaderStage( VkInfo.PipelineShaderStage( ShaderStageFlags.FragmentBit, texturedLitShader.Module,
				(byte*)texturedLitShader.EntryPointPtr, shaderSpecializationInfo ) )
			.Build();
		VkInvalidHandleException.ThrowIfInvalid( texturedMeshPipeline );

		var billboardPipeline = pipelineBuilder
			.WithPipelineLayout( meshPipelineLayout )
			.ClearShaderStages()
			.AddShaderStage( VkInfo.PipelineShaderStage( ShaderStageFlags.VertexBit, billboardVertShader.Module,
				(byte*)billboardVertShader.EntryPointPtr, shaderSpecializationInfo ) )
			.AddShaderStage( VkInfo.PipelineShaderStage( ShaderStageFlags.FragmentBit, billboardFragShader.Module,
				(byte*)billboardFragShader.EntryPointPtr, shaderSpecializationInfo ) )
			.Build();
		VkInvalidHandleException.ThrowIfInvalid( billboardPipeline );

		var defaultMeshMaterial = CreateMaterial( DefaultMeshMaterialName, meshPipeline, meshPipelineLayout );
		var texturedMeshMaterial = CreateMaterial( TexturedMeshMaterialName, texturedMeshPipeline, texturedPipelineLayout );
		var billboardMaterial = CreateMaterial( BillboardMaterialName, billboardPipeline, meshPipelineLayout );
		disposalManager.Add( () => RemoveMaterial( DefaultMeshMaterialName ), SwapchainTag, WireframeTag );
		disposalManager.Add( () => RemoveMaterial( TexturedMeshMaterialName ), SwapchainTag, WireframeTag );
		disposalManager.Add( () => RemoveMaterial( BillboardMaterialName ), SwapchainTag, WireframeTag );

		disposalManager.Add( () => Apis.Vk.DestroyPipelineLayout( VkContext.LogicalDevice, meshPipelineLayout, null ), SwapchainTag, WireframeTag );
		disposalManager.Add( () => Apis.Vk.DestroyPipeline( VkContext.LogicalDevice, meshPipeline, null ), SwapchainTag, WireframeTag );
		disposalManager.Add( () => Apis.Vk.DestroyPipelineLayout( VkContext.LogicalDevice, texturedPipelineLayout, null ), SwapchainTag, WireframeTag );
		disposalManager.Add( () => Apis.Vk.DestroyPipeline( VkContext.LogicalDevice, texturedMeshPipeline, null ), SwapchainTag, WireframeTag );
		disposalManager.Add( () => Apis.Vk.DestroyPipeline( VkContext.LogicalDevice, billboardPipeline, null ), SwapchainTag, WireframeTag );

		initializationProfile.Dispose();
		if ( !initializationStageTimes.ContainsKey( initializationProfile.Name ) )
			initializationStageTimes.Add( initializationProfile.Name, initializationProfile.Time );
	}

	private void InitializeSamplers()
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		var initializationProfile = CpuProfile.New( nameof( InitializeSamplers ) );

		Apis.Vk.CreateSampler( VkContext.LogicalDevice, VkInfo.Sampler( Filter.Linear ), null, out var linearSampler ).AssertSuccess();
		VkInvalidHandleException.ThrowIfInvalid( linearSampler );
		this.linearSampler = linearSampler;

		Apis.Vk.CreateSampler( VkContext.LogicalDevice, VkInfo.Sampler( Filter.Nearest ), null, out var nearestSampler ).AssertSuccess();
		VkInvalidHandleException.ThrowIfInvalid( nearestSampler );
		this.nearestSampler = nearestSampler;

		disposalManager.Add( () => Apis.Vk.DestroySampler( VkContext.LogicalDevice, linearSampler, null ) );
		disposalManager.Add( () => Apis.Vk.DestroySampler( VkContext.LogicalDevice, nearestSampler, null ) );

		initializationProfile.Dispose();
		if ( !initializationStageTimes.ContainsKey( initializationProfile.Name ) )
			initializationStageTimes.Add( initializationProfile.Name, initializationProfile.Time );
	}

	private void InitializeImGui( IInputContext input )
	{
		var initializationProfile = CpuProfile.New( nameof( InitializeImGui ) );

		ImGuiController = new ImGuiController( this, input, renderPass );

		initializationProfile.Dispose();
		if ( !initializationStageTimes.ContainsKey( initializationProfile.Name ) )
			initializationStageTimes.Add( initializationProfile.Name, initializationProfile.Time );
	}

	private void LoadImages()
	{
		var initializationProfile = CpuProfile.New( nameof( LoadImages ) );

		void LoadTexture( string texturePath )
		{
			if ( !VkContext.IsInitialized )
				throw new VkException( $"{nameof( VkContext )} has not been initialized" );

			var latteTexture = LatteTexture.FromPath( texturePath );
			var texture = new Texture( (uint)latteTexture.Width, (uint)latteTexture.Height, (uint)latteTexture.BytesPerPixel, latteTexture.PixelData );
			UploadTexture( texture );

			var imageViewInfo = VkInfo.ImageView( Format.R8G8B8A8Srgb, texture.GpuTexture.Image, ImageAspectFlags.ColorBit );
			Apis.Vk.CreateImageView( VkContext.LogicalDevice, imageViewInfo, null, out var imageView ).AssertSuccess();
			VkInvalidHandleException.ThrowIfInvalid( imageView );
			texture.TextureView = imageView;

			Textures.Add( Path.GetFileNameWithoutExtension( texturePath ).ToLower(), texture );

			disposalManager.Add( () => Apis.Vk.DestroyImageView( VkContext.LogicalDevice, imageView, null ) );
		}

		LoadTexture( "/Assets/Models/Car 05/car5.png" );
		LoadTexture( "/Assets/Models/Car 05/car5_green.png" );
		LoadTexture( "/Assets/Models/Car 05/car5_grey.png" );
		LoadTexture( "/Assets/Models/Car 05/car5_police.png" );
		LoadTexture( "/Assets/Models/Car 05/car5_taxi.png" );

		initializationProfile.Dispose();
		if ( !initializationStageTimes.ContainsKey( initializationProfile.Name ) )
			initializationStageTimes.Add( initializationProfile.Name, initializationProfile.Time );
	}

	private void LoadMeshes()
	{
		var initializationProfile = CpuProfile.New( nameof( LoadMeshes ) );

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

		initializationProfile.Dispose();
		if ( !initializationStageTimes.ContainsKey( initializationProfile.Name ) )
			initializationStageTimes.Add( initializationProfile.Name, initializationProfile.Time );
	}

	private void SetupTextureSets()
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		var initializationProfile = CpuProfile.New( nameof( SetupTextureSets ) );

		var defaultMaterial = GetMaterial( TexturedMeshMaterialName );
		foreach ( var (textureName, texture) in Textures )
		{
			var texturedMaterial = defaultMaterial.Clone();
			texturedMaterial.TextureSet = DescriptorAllocator.Allocate( new ReadOnlySpan<DescriptorSetLayout>( ref singleTextureSetLayout ) );
			VkInvalidHandleException.ThrowIfInvalid( texturedMaterial.TextureSet );

			new VkDescriptorUpdater( VkContext.LogicalDevice, 1 )
				.WriteImage( 0, DescriptorType.CombinedImageSampler, texture.TextureView, nearestSampler, ImageLayout.ShaderReadOnlyOptimal )
				.Update( texturedMaterial.TextureSet )
				.Dispose();

			Materials.Add( textureName, texturedMaterial );
			disposalManager.Add( () => RemoveMaterial( textureName ), SwapchainTag, WireframeTag );
		}

		initializationProfile.Dispose();
		if ( !initializationStageTimes.ContainsKey( initializationProfile.Name ) )
			initializationStageTimes.Add( initializationProfile.Name, initializationProfile.Time );
	}

	private void InitializeQueries()
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		var initializationProfile = CpuProfile.New( nameof( InitializeQueries ) );

		var gpuExecutePoolInfo = new QueryPoolCreateInfo
		{
			SType = StructureType.QueryPoolCreateInfo,
			PNext = null,
			QueryCount = 2,
			QueryType = QueryType.Timestamp,
			PipelineStatistics = QueryPipelineStatisticFlags.None,
			Flags = 0
		};

		Apis.Vk.CreateQueryPool( VkContext.LogicalDevice, gpuExecutePoolInfo, null, out var queryPool );
		VkInvalidHandleException.ThrowIfInvalid( queryPool );
		gpuExecuteQueryPool = queryPool;

		ImmediateSubmit( VkContext.GraphicsQueue, cmd =>
		{
			Apis.Vk.CmdResetQueryPool( cmd, queryPool, 0, 2 );
		} );

		disposalManager.Add( () => Apis.Vk.DestroyQueryPool( VkContext.LogicalDevice, gpuExecuteQueryPool, null ), SwapchainTag, WireframeTag );

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

			Apis.Vk.CreateQueryPool( VkContext.LogicalDevice, poolInfo, null, out queryPool );
			VkInvalidHandleException.ThrowIfInvalid( queryPool );
			material.PipelineQueryPool = queryPool;

			ImmediateSubmit( VkContext.GraphicsQueue, cmd =>
			{
				Apis.Vk.CmdResetQueryPool( cmd, queryPool, 0, 1 );
			} );

			disposalManager.Add( () => Apis.Vk.DestroyQueryPool( VkContext.LogicalDevice, material.PipelineQueryPool, null ), SwapchainTag, WireframeTag );
		}

		initializationProfile.Dispose();
		if ( !initializationStageTimes.ContainsKey( initializationProfile.Name ) )
			initializationStageTimes.Add( initializationProfile.Name, initializationProfile.Time );
	}

	private void InitializeScene()
	{
		var initializationProfile = CpuProfile.New( nameof( InitializeScene ) );

		sceneParameters.AmbientLightColor = new Vector4( 1, 1, 1, 0.02f );

		for ( var i = 0; i < MaxLights; i++ )
		{
			var light = new Light
			{
				Position = new Vector3( Random.Shared.Next( -125, 126 ), 10, Random.Shared.Next( -125, 126 ) ),
				Color = new Vector4( Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle(), 1000 )
			};

			Lights.Add( light );
			Renderables.Add( new Renderable( "quad", BillboardMaterialName )
			{
				Transform = Matrix4x4.CreateTranslation( light.Position )
			} );
		}

		Renderables.Add( new Renderable( "quad", DefaultMeshMaterialName )
		{
			Transform = Matrix4x4.CreateScale( 1000, 0, 1000 )
		} );

		foreach ( var (materialName, _) in Materials.Skip( 3 ) )
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

		initializationProfile.Dispose();
		if ( !initializationStageTimes.ContainsKey( initializationProfile.Name ) )
			initializationStageTimes.Add( initializationProfile.Name, initializationProfile.Time );
	}

	private void OnFramebufferResize( Vector2D<int> newSize )
	{
		RecreateSwapchain();
	}

	private void ImmediateSubmit( VkQueue queue, Action<CommandBuffer> cb )
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		if ( !uploadContexts.TryGetValue( queue, out var uploadContext ) )
			throw new VkException( $"The queue {queue} does not have an upload context" );

		var cmd = uploadContext.CommandBuffer;
		var beginInfo = VkInfo.BeginCommandBuffer( CommandBufferUsageFlags.OneTimeSubmitBit );

		Apis.Vk.BeginCommandBuffer( cmd, beginInfo ).AssertSuccess();
		cb( cmd );
		Apis.Vk.EndCommandBuffer( cmd ).AssertSuccess();

		var submitInfo = VkInfo.SubmitInfo( new ReadOnlySpan<CommandBuffer>( ref cmd ) );
		VkContext.GraphicsQueue.SubmitAndWait( submitInfo, uploadContext.UploadFence );
		Apis.Vk.WaitForFences( VkContext.LogicalDevice, 1, uploadContext.UploadFence, Vk.True, 999_999_999_999 ).AssertSuccess();
		Apis.Vk.ResetFences( VkContext.LogicalDevice, 1, uploadContext.UploadFence ).AssertSuccess();
		Apis.Vk.ResetCommandPool( VkContext.LogicalDevice, uploadContext.CommandPool, CommandPoolResetFlags.None ).AssertSuccess();
	}

	internal Shader CreateShader( string name, LatteShader latteShader )
	{
		if ( Shaders.ContainsKey( name ) )
			throw new ArgumentException( $"A shader with the name \"{name}\" already exists", nameof( name ) );

		var shader = new Shader( VkContext.LogicalDevice, latteShader.Code, latteShader.EntryPoint );
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
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		var statsStorage = (ulong*)Marshal.AllocHGlobal( 6 * sizeof( ulong ) );

		var result = Apis.Vk.GetQueryPoolResults( VkContext.LogicalDevice, gpuExecuteQueryPool, 0, 2,
			2 * sizeof( ulong ),
			statsStorage,
			sizeof( ulong ),
			QueryResultFlags.Result64Bit );

		if ( result == Result.Success )
			gpuExecuteTime = TimeSpan.FromMicroseconds( (statsStorage[1] - statsStorage[0]) * VkContext.PhysicalDeviceInfo.Properties.Limits.TimestampPeriod / 1000 );
		else if ( result != Result.NotReady )
			result.AssertSuccess();

		foreach ( var (materialName, material) in Materials )
		{
			result = Apis.Vk.GetQueryPoolResults( VkContext.LogicalDevice, material.PipelineQueryPool, 0, 1,
				6 * sizeof( ulong ),
				statsStorage,
				6 * sizeof( ulong ),
				QueryResultFlags.Result64Bit );

			if ( result == Result.NotReady )
				continue;
			else
				result.AssertSuccess();

			materialPipelineStatistics[materialName] = new VkPipelineStatistics(
				statsStorage[0],
				statsStorage[1],
				statsStorage[2],
				statsStorage[3],
				statsStorage[4],
				statsStorage[5] );
		}

		var typeCount = VkContext.PhysicalDeviceInfo.MemoryProperties.MemoryTypeCount;
		var memoryAllocationCountBuilder = ImmutableArray.CreateBuilder<int>( (int)typeCount );
		var memoryAllocationSizeBuilder = ImmutableArray.CreateBuilder<ulong>( (int)typeCount );
		for ( uint i = 0; i < typeCount; i++ )
		{
			memoryAllocationCountBuilder.Add( VkContext.AllocationManager.GetAllocationCount( i ) );
			memoryAllocationSizeBuilder.Add( VkContext.AllocationManager.GetAllocationSize( i ) );
		}

		Marshal.FreeHGlobal( (nint)statsStorage );
		return new VkStatistics( initializationStageTimes,
			cpuPerformanceTimes,
			gpuExecuteTime,
			materialPipelineStatistics,
			VkContext.AllocationManager.TotalAllocationCount,
			memoryAllocationCountBuilder.MoveToImmutable(),
			memoryAllocationSizeBuilder.MoveToImmutable() );
	}

	private void UploadMesh( Mesh mesh, SharingMode sharingMode = SharingMode.Exclusive )
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		// Vertex buffer
		{
			var bufferSize = (ulong)(mesh.Vertices.Length * Unsafe.SizeOf<Vertex>());

			var stagingBuffer = CreateBuffer( bufferSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit );
			VkContext.AllocationManager.SetMemory( stagingBuffer.Allocation, mesh.Vertices.AsSpan() );
			var vertexBuffer = CreateBuffer( bufferSize, BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit,
				MemoryPropertyFlags.DeviceLocalBit, sharingMode );

			ImmediateSubmit( VkContext.TransferQueue, cmd =>
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

			Apis.Vk.DestroyBuffer( VkContext.LogicalDevice, stagingBuffer.Buffer, null );
			VkContext.AllocationManager.Free( stagingBuffer.Allocation );
			disposalManager.Add( () => VkContext.AllocationManager.Free( vertexBuffer.Allocation ) );
			disposalManager.Add( () => Apis.Vk.DestroyBuffer( VkContext.LogicalDevice, vertexBuffer.Buffer, null ) );
		}

		// Index buffer
		if ( mesh.Indices.Length == 0 )
			return;

		{
			var bufferSize = sizeof( uint ) * (ulong)mesh.Indices.Length;

			var stagingBuffer = CreateBuffer( bufferSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit );
			VkContext.AllocationManager.SetMemory( stagingBuffer.Allocation, mesh.Indices.AsSpan() );
			var indexBuffer = CreateBuffer( bufferSize, BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit,
				MemoryPropertyFlags.DeviceLocalBit, sharingMode );

			ImmediateSubmit( VkContext.TransferQueue, cmd =>
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

			Apis.Vk.DestroyBuffer( VkContext.LogicalDevice, stagingBuffer.Buffer, null );
			VkContext.AllocationManager.Free( stagingBuffer.Allocation );
			disposalManager.Add( () => VkContext.AllocationManager.Free( indexBuffer.Allocation ) );
			disposalManager.Add( () => Apis.Vk.DestroyBuffer( VkContext.LogicalDevice, indexBuffer.Buffer, null ) );
		}
	}

	private void UploadTexture( Texture texture )
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		var imageSize = texture.Width * texture.Height * texture.BytesPerPixel;
		var imageFormat = Format.R8G8B8A8Srgb;

		var stagingBuffer = CreateBuffer( imageSize, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit );
		VkContext.AllocationManager.SetMemory( stagingBuffer.Allocation, texture.PixelData.Span );

		var imageExtent = new Extent3D( texture.Width, texture.Height, 1 );
		var imageInfo = VkInfo.Image( imageFormat, ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit, imageExtent );
		Apis.Vk.CreateImage( VkContext.LogicalDevice, imageInfo, null, out var textureImage );

		var allocatedTextureImage = VkContext.AllocationManager.AllocateImage( textureImage, MemoryPropertyFlags.DeviceLocalBit );
		ImmediateSubmit( VkContext.TransferQueue, cmd =>
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

		Apis.Vk.DestroyBuffer( VkContext.LogicalDevice, stagingBuffer.Buffer, null );
		VkContext.AllocationManager.Free( stagingBuffer.Allocation );
		disposalManager.Add( () => VkContext.AllocationManager.Free( allocatedTextureImage.Allocation ) );
		disposalManager.Add( () => Apis.Vk.DestroyImage( VkContext.LogicalDevice, textureImage, null ) );
	}

	private static AllocatedBuffer CreateBuffer( ulong size, BufferUsageFlags usageFlags, MemoryPropertyFlags memoryFlags,
		SharingMode sharingMode = SharingMode.Exclusive )
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		var createInfo = VkInfo.Buffer( size, usageFlags, sharingMode );
		Apis.Vk.CreateBuffer( VkContext.LogicalDevice, createInfo, null, out var buffer );

		return VkContext.AllocationManager.AllocateBuffer( buffer, memoryFlags );
	}

	private static bool TryLoadShaderModule( ReadOnlySpan<byte> shaderBytes, out ShaderModule shaderModule )
	{
		fixed ( byte* shaderBytesPtr = shaderBytes )
		{
			var createInfo = VkInfo.ShaderModule( (nuint)shaderBytes.Length, shaderBytesPtr, ShaderModuleCreateFlags.None );
			var result = Apis.Vk.CreateShaderModule( VkContext.LogicalDevice, createInfo, null, out shaderModule );

			return result == Result.Success;
		}
	}

	private static ulong PadUniformBufferSize( ulong currentSize )
	{
		var minimumAlignment = VkContext.PhysicalDeviceInfo.Properties.Limits.MinUniformBufferOffsetAlignment;

		if ( minimumAlignment > 0 )
			return currentSize + minimumAlignment - 1 & ~(minimumAlignment - 1);
		else
			return currentSize;
	}

	private void Dispose( bool disposing )
	{
		if ( disposed )
			return;

		View.FramebufferResize -= OnFramebufferResize;

		if ( disposing )
		{
		}

		ImGuiController?.Dispose();
		DescriptorAllocator?.Dispose();
		disposalManager?.Dispose();

		disposed = true;
	}

	public void Dispose()
	{
		Dispose( disposing: true );
		GC.SuppressFinalize( this );
	}

	private class UploadContext
	{
		internal Fence UploadFence;
		internal CommandPool CommandPool;
		internal CommandBuffer CommandBuffer;
	}
}

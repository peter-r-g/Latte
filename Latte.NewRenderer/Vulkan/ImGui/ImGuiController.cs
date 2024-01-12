using ImGuiNET;
using Latte.NewRenderer.Extensions;
using Latte.NewRenderer.Vulkan.Allocations;
using Latte.NewRenderer.Vulkan.Builders;
using Latte.NewRenderer.Vulkan.Exceptions;
using Latte.NewRenderer.Vulkan.Extensions;
using Silk.NET.Input;
using Silk.NET.Input.Extensions;
using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using VMASharp;
using Buffer = Silk.NET.Vulkan.Buffer;
using LatteShader = Latte.Assets.Shader;

namespace Latte.NewRenderer.Vulkan.ImGui;

public sealed class ImGuiController : IDisposable
{
	private VkEngine engine = null!;
	private IInputContext input = null!;
	private IKeyboard keyboard = null!;
	private DisposalManager disposalManager = null!;
	private RenderPass renderPass;

	private Sampler fontSampler;
	private DescriptorSetLayout descriptorSetLayout;
	private DescriptorSet descriptorSet;
	private PipelineLayout pipelineLayout;
	private Pipeline pipeline;
	private AllocatedImage fontImage;
	private ImageView fontView;

	private bool frameBegun;

	private readonly List<char> pressedChars = [];
	private readonly WindowRenderBuffers mainWindowRenderBuffers = new();

	/// <summary>
	/// Constructs a new ImGuiController.
	/// </summary>
	/// <param name="view">Window view</param>
	/// <param name="input">Input context</param>
	/// <param name="physicalDevice">The physical device instance in use</param>
	/// <param name="graphicsFamilyIndex">The graphics family index corresponding to the graphics queue</param>
	/// <param name="swapChainImageCt">The number of images used in the swap chain</param>
	/// <param name="swapChainFormat">The image format used by the swap chain</param>
	/// <param name="depthBufferFormat">The image formate used by the depth buffer, or null if no depth buffer is used</param>
	internal ImGuiController( VkEngine engine, IInputContext input, RenderPass renderPass )
	{
		var context = ImGuiNET.ImGui.CreateContext();
		ImGuiNET.ImGui.SetCurrentContext( context );

		// Use the default font
		var io = ImGuiNET.ImGui.GetIO();
		io.Fonts.AddFontDefault();
		io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;

		Init( engine, input, renderPass );

		SetPerFrameImGuiData( 1f / 60f );

		BeginFrame();
	}

	/// <summary>
	/// Constructs a new ImGuiController.
	/// </summary>
	/// <param name="view">Window view</param>
	/// <param name="input">Input context</param>
	/// <param name="imGuiFontConfig">A custom ImGui configuration</param>
	/// <param name="physicalDevice">The physical device instance in use</param>
	/// <param name="graphicsFamilyIndex">The graphics family index corresponding to the graphics queue</param>
	/// <param name="swapChainImageCt">The number of images used in the swap chain</param>
	/// <param name="swapChainFormat">The image format used by the swap chain</param>
	/// <param name="depthBufferFormat">The image formate used by the depth buffer, or null if no depth buffer is used</param>
	internal unsafe ImGuiController( VkEngine engine, IInputContext input, RenderPass renderPass, ImGuiFontConfig imGuiFontConfig )
	{
		var context = ImGuiNET.ImGui.CreateContext();
		ImGuiNET.ImGui.SetCurrentContext( context );

		// Upload custom ImGui font
		var io = ImGuiNET.ImGui.GetIO();
		io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
		if ( io.Fonts.AddFontFromFileTTF( imGuiFontConfig.FontPath, imGuiFontConfig.FontSize ).NativePtr == default )
		{
			throw new Exception( $"Failed to load ImGui font" );
		}

		Init( engine, input, renderPass );

		SetPerFrameImGuiData( 1f / 60f );

		BeginFrame();
	}

	private unsafe void Init( VkEngine engine, IInputContext input, RenderPass renderPass )
	{
		this.engine = engine;
		this.input = input;
		disposalManager = new DisposalManager();
		this.renderPass = renderPass;

		if ( engine.SwapchainImageCount < 2 )
			throw new Exception( $"Swap chain image count must be >= 2" );

		InitializeStyle();
		InitializeSampler();
		InitializeDescriptors();
		InitializePipeline();
		UploadDefaultFontAtlas( VkContext.QueueFamilyIndices.GraphicsQueue );
	}

	/// <summary>
	/// Updates ImGui input and IO configuration state. Call Update() before drawing and rendering.
	/// </summary>
	public void Update( float deltaSeconds )
	{
		if ( frameBegun )
			ImGuiNET.ImGui.Render();

		SetPerFrameImGuiData( deltaSeconds );
		UpdateImGuiInput();

		frameBegun = true;
		ImGuiNET.ImGui.NewFrame();
	}

	/// <summary>
	/// Renders the ImGui draw list data.
	/// </summary>
	public void Render( CommandBuffer commandBuffer, Framebuffer framebuffer, Extent2D swapchainExtent )
	{
		if ( !frameBegun )
			return;

		frameBegun = false;
		ImGuiNET.ImGui.Render();
		RenderImDrawData( ImGuiNET.ImGui.GetDrawData(), commandBuffer, framebuffer, swapchainExtent );
	}

	internal void PressChar( char keyChar )
	{
		pressedChars.Add( keyChar );
	}

	private static void InitializeStyle()
	{
		var style = ImGuiNET.ImGui.GetStyle();
		style.WindowPadding = new Vector2( 8.00f, 8.00f );
		style.FramePadding = new Vector2( 12.00f, 6.00f );
		style.CellPadding = new Vector2( 4.00f, 4.00f );
		style.ItemSpacing = new Vector2( 4.00f, 4.00f );
		style.ItemInnerSpacing = new Vector2( 2.00f, 2.00f );
		style.TouchExtraPadding = new Vector2( 0.00f, 0.00f );
		style.IndentSpacing = 25;
		style.ScrollbarSize = 12;
		style.GrabMinSize = 12;
		style.WindowBorderSize = 1;
		style.ChildBorderSize = 0;
		style.PopupBorderSize = 0;
		style.FrameBorderSize = 0;
		style.TabBorderSize = 0;
		style.WindowRounding = 6;
		style.ChildRounding = 4;
		style.FrameRounding = 3;
		style.PopupRounding = 4;
		style.ScrollbarRounding = 9;
		style.GrabRounding = 3;
		style.LogSliderDeadzone = 4;
		style.TabRounding = 4;
		style.WindowTitleAlign = new Vector2( 0.5f, 0.5f );
		style.WindowMenuButtonPosition = ImGuiDir.None;
		style.AntiAliasedLinesUseTex = false;

		var colors = style.Colors;
		colors[(int)ImGuiCol.Text] = new Vector4( 1.00f, 1.00f, 1.00f, 1.00f );
		colors[(int)ImGuiCol.TextDisabled] = new Vector4( 0.50f, 0.50f, 0.50f, 1.00f );
		colors[(int)ImGuiCol.WindowBg] = new Vector4( 0.17f, 0.17f, 0.18f, 1.00f );
		colors[(int)ImGuiCol.ChildBg] = new Vector4( 0.10f, 0.11f, 0.11f, 1.00f );
		colors[(int)ImGuiCol.PopupBg] = new Vector4( 0.24f, 0.24f, 0.25f, 1.00f );
		colors[(int)ImGuiCol.Border] = new Vector4( 0.00f, 0.00f, 0.00f, 0.5f );
		colors[(int)ImGuiCol.BorderShadow] = new Vector4( 0.00f, 0.00f, 0.00f, 0.24f );
		colors[(int)ImGuiCol.FrameBg] = new Vector4( 0.10f, 0.11f, 0.11f, 1.00f );
		colors[(int)ImGuiCol.FrameBgHovered] = new Vector4( 0.19f, 0.19f, 0.19f, 0.54f );
		colors[(int)ImGuiCol.FrameBgActive] = new Vector4( 0.20f, 0.22f, 0.23f, 1.00f );
		colors[(int)ImGuiCol.TitleBg] = new Vector4( 0.0f, 0.0f, 0.0f, 1.00f );
		colors[(int)ImGuiCol.TitleBgActive] = new Vector4( 0.00f, 0.00f, 0.00f, 1.00f );
		colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4( 0.00f, 0.00f, 0.00f, 1.00f );
		colors[(int)ImGuiCol.MenuBarBg] = new Vector4( 0.14f, 0.14f, 0.14f, 1.00f );
		colors[(int)ImGuiCol.ScrollbarBg] = new Vector4( 0.05f, 0.05f, 0.05f, 0.54f );
		colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4( 0.34f, 0.34f, 0.34f, 0.54f );
		colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4( 0.40f, 0.40f, 0.40f, 0.54f );
		colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4( 0.56f, 0.56f, 0.56f, 0.54f );
		colors[(int)ImGuiCol.CheckMark] = new Vector4( 0.33f, 0.67f, 0.86f, 1.00f );
		colors[(int)ImGuiCol.SliderGrab] = new Vector4( 0.34f, 0.34f, 0.34f, 0.54f );
		colors[(int)ImGuiCol.SliderGrabActive] = new Vector4( 0.56f, 0.56f, 0.56f, 0.54f );
		colors[(int)ImGuiCol.Button] = new Vector4( 0.24f, 0.24f, 0.25f, 1.00f );
		colors[(int)ImGuiCol.ButtonHovered] = new Vector4( 0.19f, 0.19f, 0.19f, 0.54f );
		colors[(int)ImGuiCol.ButtonActive] = new Vector4( 0.20f, 0.22f, 0.23f, 1.00f );
		colors[(int)ImGuiCol.Header] = new Vector4( 0.00f, 0.00f, 0.00f, 0.52f );
		colors[(int)ImGuiCol.HeaderHovered] = new Vector4( 0.00f, 0.00f, 0.00f, 0.36f );
		colors[(int)ImGuiCol.HeaderActive] = new Vector4( 0.20f, 0.22f, 0.23f, 0.33f );
		colors[(int)ImGuiCol.Separator] = new Vector4( 0.0f, 0.0f, 0.0f, 1.0f );
		colors[(int)ImGuiCol.SeparatorHovered] = new Vector4( 0.44f, 0.44f, 0.44f, 0.29f );
		colors[(int)ImGuiCol.SeparatorActive] = new Vector4( 0.40f, 0.44f, 0.47f, 1.00f );
		colors[(int)ImGuiCol.ResizeGrip] = new Vector4( 0.28f, 0.28f, 0.28f, 0.29f );
		colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4( 0.44f, 0.44f, 0.44f, 0.29f );
		colors[(int)ImGuiCol.ResizeGripActive] = new Vector4( 0.40f, 0.44f, 0.47f, 1.00f );
		colors[(int)ImGuiCol.Tab] = new Vector4( 0.08f, 0.08f, 0.09f, 1.00f );
		colors[(int)ImGuiCol.TabHovered] = new Vector4( 0.14f, 0.14f, 0.14f, 1.00f );
		colors[(int)ImGuiCol.TabActive] = new Vector4( 0.17f, 0.17f, 0.18f, 1.00f );
		colors[(int)ImGuiCol.TabUnfocused] = new Vector4( 0.08f, 0.08f, 0.09f, 1.00f );
		colors[(int)ImGuiCol.TabUnfocusedActive] = new Vector4( 0.14f, 0.14f, 0.14f, 1.00f );
		colors[(int)ImGuiCol.DockingPreview] = new Vector4( 0.33f, 0.67f, 0.86f, 1.00f );
		colors[(int)ImGuiCol.DockingEmptyBg] = new Vector4( 0.33f, 0.67f, 0.86f, 1.00f );
		colors[(int)ImGuiCol.PlotLines] = new Vector4( 0.33f, 0.67f, 0.86f, 1.00f );
		colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4( 0.33f, 0.67f, 0.86f, 1.00f );
		colors[(int)ImGuiCol.PlotHistogram] = new Vector4( 0.33f, 0.67f, 0.86f, 1.00f );
		colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4( 0.33f, 0.67f, 0.86f, 1.00f );
		colors[(int)ImGuiCol.TableHeaderBg] = new Vector4( 0.00f, 0.00f, 0.00f, 0.52f );
		colors[(int)ImGuiCol.TableBorderStrong] = new Vector4( 0.00f, 0.00f, 0.00f, 0.52f );
		colors[(int)ImGuiCol.TableBorderLight] = new Vector4( 0.28f, 0.28f, 0.28f, 0.29f );
		colors[(int)ImGuiCol.TableRowBg] = new Vector4( 0.00f, 0.00f, 0.00f, 0.00f );
		colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4( 1.00f, 1.00f, 1.00f, 0.06f );
		colors[(int)ImGuiCol.TextSelectedBg] = new Vector4( 0.20f, 0.22f, 0.23f, 1.00f );
		colors[(int)ImGuiCol.DragDropTarget] = new Vector4( 0.33f, 0.67f, 0.86f, 1.00f );
		colors[(int)ImGuiCol.NavHighlight] = new Vector4( 0.33f, 0.67f, 0.86f, 1.00f );
		colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4( 0.33f, 0.67f, 0.86f, 1.00f );
		colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4( 0.33f, 0.67f, 0.86f, 1.00f );
		colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4( 0.33f, 0.67f, 0.86f, 1.00f );
	}

	private unsafe void InitializeSampler()
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		var info = new SamplerCreateInfo
		{
			SType = StructureType.SamplerCreateInfo,
			MagFilter = Filter.Linear,
			MinFilter = Filter.Linear,
			MipmapMode = SamplerMipmapMode.Linear,
			AddressModeU = SamplerAddressMode.Repeat,
			AddressModeV = SamplerAddressMode.Repeat,
			AddressModeW = SamplerAddressMode.Repeat,
			MinLod = -1000,
			MaxLod = 1000,
			MaxAnisotropy = 1.0f
		};

		Apis.Vk.CreateSampler( VkContext.LogicalDevice, info, default, out fontSampler ).AssertSuccess();
		VkInvalidHandleException.ThrowIfInvalid( fontSampler );
		disposalManager.Add( () => Apis.Vk.DestroySampler( VkContext.LogicalDevice, fontSampler, null ) );
	}

	private unsafe void InitializeDescriptors()
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		var binding = new DescriptorSetLayoutBinding
		{
			DescriptorType = DescriptorType.CombinedImageSampler,
			DescriptorCount = 1,
			StageFlags = ShaderStageFlags.FragmentBit,
			PImmutableSamplers = (Sampler*)Unsafe.AsPointer( ref fontSampler )
		};

		var descriptorInfo = new DescriptorSetLayoutCreateInfo
		{
			SType = StructureType.DescriptorSetLayoutCreateInfo,
			BindingCount = 1,
			PBindings = (DescriptorSetLayoutBinding*)Unsafe.AsPointer( ref binding )
		};

		Apis.Vk.CreateDescriptorSetLayout( VkContext.LogicalDevice, descriptorInfo, default, out descriptorSetLayout ).AssertSuccess();
		VkInvalidHandleException.ThrowIfInvalid( descriptorSetLayout );

		descriptorSet = engine.DescriptorAllocator.Allocate( new ReadOnlySpan<DescriptorSetLayout>( ref descriptorSetLayout ) );
		VkInvalidHandleException.ThrowIfInvalid( descriptorSet );

		disposalManager.Add( () => Apis.Vk.DestroyDescriptorSetLayout( VkContext.LogicalDevice, descriptorSetLayout, null ) );
	}

	private unsafe void InitializePipeline()
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		pipelineLayout = new VkPipelineLayoutBuilder( VkContext.LogicalDevice, 1, 1 )
			.AddPushConstantRange( new PushConstantRange
			{
				StageFlags = ShaderStageFlags.VertexBit,
				Offset = sizeof( float ) * 0,
				Size = sizeof( float ) * 4
			} )
			.AddDescriptorSetLayout( descriptorSetLayout )
			.Build();
		VkInvalidHandleException.ThrowIfInvalid( pipelineLayout );

		var imguiVert = engine.CreateShader( "imgui.vert", LatteShader.FromPath( "/Assets/Shaders/imgui.vert.spv" ) );
		var imguiFrag = engine.CreateShader( "imgui.frag", LatteShader.FromPath( "/Assets/Shaders/imgui.frag.spv" ) );

		pipeline = new VkPipelineBuilder( VkContext.LogicalDevice, renderPass )
			.WithPipelineLayout( pipelineLayout )
			.AddDynamicState( DynamicState.Viewport )
			.AddDynamicState( DynamicState.Scissor )
			.AddShaderStage( VkInfo.PipelineShaderStage( ShaderStageFlags.VertexBit, imguiVert.Module, (byte*)imguiVert.EntryPointPtr ) )
			.AddShaderStage( VkInfo.PipelineShaderStage( ShaderStageFlags.FragmentBit, imguiFrag.Module, (byte*)imguiFrag.EntryPointPtr ) )
			.WithVertexInputState( VkInfo.PipelineVertexInputState( VkVertexInputDescription.GetImGuiVertexDescription() ) )
			.WithInputAssemblyState( VkInfo.PipelineInputAssemblyState( PrimitiveTopology.TriangleList ) )
			.WithRasterizerState( new PipelineRasterizationStateCreateInfo
			{
				SType = StructureType.PipelineRasterizationStateCreateInfo,
				PolygonMode = PolygonMode.Fill,
				CullMode = CullModeFlags.None,
				FrontFace = FrontFace.CounterClockwise,
				LineWidth = 1
			} )
			.WithMultisamplingState( VkInfo.PipelineMultisamplingState() )
			.WithColorBlendAttachmentState( new PipelineColorBlendAttachmentState
			{
				BlendEnable = Vk.True,
				SrcColorBlendFactor = BlendFactor.SrcAlpha,
				DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
				ColorBlendOp = BlendOp.Add,
				SrcAlphaBlendFactor = BlendFactor.One,
				DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
				AlphaBlendOp = BlendOp.Add,
				ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit
			} )
			.WithDepthStencilState( VkInfo.PipelineDepthStencilState( false, false, CompareOp.Never ) )
			.Build();
		VkInvalidHandleException.ThrowIfInvalid( pipeline );

		disposalManager.Add( imguiVert.Dispose );
		disposalManager.Add( imguiFrag.Dispose );
		disposalManager.Add( () => Apis.Vk.DestroyPipelineLayout( VkContext.LogicalDevice, pipelineLayout, null ) );
		disposalManager.Add( () => Apis.Vk.DestroyPipeline( VkContext.LogicalDevice, pipeline, null ) );
	}

	private unsafe void UploadDefaultFontAtlas( uint graphicsFamilyIndex )
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		// Initialise ImGui Vulkan adapter
		var io = ImGuiNET.ImGui.GetIO();
		io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
		io.Fonts.GetTexDataAsRGBA32( out nint pixels, out int width, out int height );
		ulong uploadSize = (ulong)(width * height * 4 * sizeof( byte ));

		// Submit one-time command to create the fonts texture
		var poolInfo = VkInfo.CommandPool( graphicsFamilyIndex );
		Apis.Vk.CreateCommandPool( VkContext.LogicalDevice, poolInfo, null, out var commandPool ).AssertSuccess();
		VkInvalidHandleException.ThrowIfInvalid( commandPool );

		var allocInfo = VkInfo.AllocateCommandBuffer( commandPool, 1 );
		Apis.Vk.AllocateCommandBuffers( VkContext.LogicalDevice, allocInfo, out var commandBuffer ).AssertSuccess();
		VkInvalidHandleException.ThrowIfInvalid( commandBuffer );

		var beginInfo = VkInfo.BeginCommandBuffer( CommandBufferUsageFlags.OneTimeSubmitBit );
		Apis.Vk.BeginCommandBuffer( commandBuffer, beginInfo ).AssertSuccess();

		var imageInfo = new ImageCreateInfo
		{
			SType = StructureType.ImageCreateInfo,
			PNext = null,
			Format = Format.R8G8B8A8Unorm,
			Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
			Extent = new Extent3D( (uint)width, (uint)height, 1 ),
			ImageType = ImageType.Type2D,
			MipLevels = 1,
			ArrayLayers = 1,
			Samples = SampleCountFlags.Count1Bit,
			Tiling = ImageTiling.Optimal,
			SharingMode = SharingMode.Exclusive,
			InitialLayout = ImageLayout.Undefined,
			Flags = ImageCreateFlags.None
		};

		var unallocatedFontImage = VkContext.AllocationManager.CreateImage( imageInfo, new AllocationCreateInfo
		{
			RequiredFlags = MemoryPropertyFlags.DeviceLocalBit,
			Usage = MemoryUsage.GPU_Only
		}, out var fontImageAllocation );
		VkInvalidHandleException.ThrowIfInvalid( unallocatedFontImage );
		fontImage = new AllocatedImage( unallocatedFontImage, fontImageAllocation );

		var imageViewInfo = VkInfo.ImageView( Format.R8G8B8A8Unorm, fontImage.Image, ImageAspectFlags.ColorBit );
		Apis.Vk.CreateImageView( VkContext.LogicalDevice, &imageViewInfo, default, out fontView ).AssertSuccess();
		VkInvalidHandleException.ThrowIfInvalid( fontView );

		new VkDescriptorUpdater( VkContext.LogicalDevice, 1 )
			.WriteImage( 0, DescriptorType.CombinedImageSampler, fontView, fontSampler, ImageLayout.ShaderReadOnlyOptimal )
			.Update( descriptorSet )
			.Dispose();

		// Create the Upload Buffer:
		var bufferInfo = VkInfo.Buffer( uploadSize, BufferUsageFlags.TransferSrcBit, SharingMode.Exclusive );
		var unallocatedStagingBuffer = VkContext.AllocationManager.CreateBuffer( bufferInfo, new AllocationCreateInfo
		{
			RequiredFlags = MemoryPropertyFlags.HostVisibleBit,
			Usage = MemoryUsage.CPU_To_GPU
		}, out var stagingBufferAllocation );
		VkInvalidHandleException.ThrowIfInvalid( unallocatedStagingBuffer );
		var stagingBuffer = new AllocatedBuffer( unallocatedStagingBuffer, stagingBufferAllocation );
		stagingBufferAllocation.SetMemory( pixels, uploadSize );

		const uint VK_QUEUE_FAMILY_IGNORED = ~0U;

		// TODO: Add to VkInfo
		var copyBarrier = new ImageMemoryBarrier
		{
			SType = StructureType.ImageMemoryBarrier,
			PNext = null,
			SrcAccessMask = AccessFlags.None,
			DstAccessMask = AccessFlags.TransferWriteBit,
			OldLayout = ImageLayout.Undefined,
			NewLayout = ImageLayout.TransferDstOptimal,
			SrcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
			DstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
			Image = fontImage.Image,
			SubresourceRange = new ImageSubresourceRange
			{
				AspectMask = ImageAspectFlags.ColorBit,
				LayerCount = 1,
				LevelCount = 1
			}
		};
		Apis.Vk.CmdPipelineBarrier( commandBuffer, PipelineStageFlags.HostBit, PipelineStageFlags.TransferBit, 0, 0, default, 0, default, 1, copyBarrier );

		var region = new BufferImageCopy
		{
			ImageSubresource = new ImageSubresourceLayers
			{
				AspectMask = ImageAspectFlags.ColorBit,
				LayerCount = 1
			},
			ImageExtent = new Extent3D
			{
				Width = (uint)width,
				Height = (uint)height,
				Depth = 1
			}
		};
		Apis.Vk.CmdCopyBufferToImage( commandBuffer, unallocatedStagingBuffer, fontImage.Image, ImageLayout.TransferDstOptimal, 1, &region );

		// TODO: Add to VkInfo
		var useBarrier = new ImageMemoryBarrier
		{
			SType = StructureType.ImageMemoryBarrier,
			PNext = null,
			SrcAccessMask = AccessFlags.TransferWriteBit,
			DstAccessMask = AccessFlags.ShaderReadBit,
			OldLayout = ImageLayout.TransferDstOptimal,
			NewLayout = ImageLayout.ShaderReadOnlyOptimal,
			SrcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
			DstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
			Image = fontImage.Image,
			SubresourceRange = new ImageSubresourceRange
			{
				AspectMask = ImageAspectFlags.ColorBit,
				LayerCount = 1,
				LevelCount = 1
			}
		};
		Apis.Vk.CmdPipelineBarrier( commandBuffer, PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit, 0, 0, default, 0, default, 1, useBarrier );

		// Store our identifier
		io.Fonts.SetTexID( (nint)fontImage.Image.Handle );

		Apis.Vk.EndCommandBuffer( commandBuffer ).AssertSuccess();
		Apis.Vk.GetDeviceQueue( VkContext.LogicalDevice, graphicsFamilyIndex, 0, out var graphicsQueue );

		var submitInfo = VkInfo.SubmitInfo( new ReadOnlySpan<CommandBuffer>( ref commandBuffer ) );
		Apis.Vk.QueueSubmit( graphicsQueue, 1, submitInfo, default ).AssertSuccess();
		Apis.Vk.QueueWaitIdle( graphicsQueue ).AssertSuccess();

		Apis.Vk.DestroyBuffer( VkContext.LogicalDevice, unallocatedStagingBuffer, default );
		stagingBuffer.Allocation.Dispose();
		Apis.Vk.DestroyCommandPool( VkContext.LogicalDevice, commandPool, default );

		disposalManager.Add( fontImageAllocation.Dispose );
		disposalManager.Add( () => Apis.Vk.DestroyImage( VkContext.LogicalDevice, fontImage.Image, null ) );
		disposalManager.Add( () => Apis.Vk.DestroyImageView( VkContext.LogicalDevice, fontView, null ) );
	}

	private void BeginFrame()
	{
		ImGuiNET.ImGui.NewFrame();
		frameBegun = true;
		keyboard = input.Keyboards[0];
		keyboard.KeyChar += OnKeyChar;
	}

	private void SetPerFrameImGuiData( float deltaSeconds )
	{
		var io = ImGuiNET.ImGui.GetIO();
		var view = engine.View!;
		var width = view.Size.X;
		var height = view.Size.Y;
		io.DisplaySize = new Vector2( width, height );

		if ( width > 0 && height > 0 )
		{
			io.DisplayFramebufferScale = new Vector2( (float)view.FramebufferSize.X / width, (float)view.FramebufferSize.Y / height );
		}

		io.DeltaTime = deltaSeconds;
	}

	private void UpdateImGuiInput()
	{
		var io = ImGuiNET.ImGui.GetIO();

		var mouseState = input.Mice[0].CaptureState();
		var keyboardState = input.Keyboards[0];

		io.MouseDown[0] = mouseState.IsButtonPressed( MouseButton.Left );
		io.MouseDown[1] = mouseState.IsButtonPressed( MouseButton.Right );
		io.MouseDown[2] = mouseState.IsButtonPressed( MouseButton.Middle );
		io.MousePos = new Vector2( (int)mouseState.Position.X, (int)mouseState.Position.Y );

		var wheel = mouseState.GetScrollWheels()[0];
		io.MouseWheel = wheel.Y;
		io.MouseWheelH = wheel.X;

		foreach ( Key key in Enum.GetValues( typeof( Key ) ) )
		{
			if ( key == Key.Unknown )
			{
				continue;
			}
			io.AddKeyEvent( key.ToImGui(), keyboardState.IsKeyPressed( key ) );
		}

		foreach ( var c in pressedChars )
		{
			io.AddInputCharacter( c );
		}

		pressedChars.Clear();

		io.KeyCtrl = keyboardState.IsKeyPressed( Key.ControlLeft ) || keyboardState.IsKeyPressed( Key.ControlRight );
		io.KeyAlt = keyboardState.IsKeyPressed( Key.AltLeft ) || keyboardState.IsKeyPressed( Key.AltRight );
		io.KeyShift = keyboardState.IsKeyPressed( Key.ShiftLeft ) || keyboardState.IsKeyPressed( Key.ShiftRight );
		io.KeySuper = keyboardState.IsKeyPressed( Key.SuperLeft ) || keyboardState.IsKeyPressed( Key.SuperRight );
	}

	private unsafe void RenderImDrawData( in ImDrawDataPtr drawDataPtr, in CommandBuffer commandBuffer, in Framebuffer framebuffer, in Extent2D swapchainExtent )
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		var drawData = *drawDataPtr.NativePtr;

		// Avoid rendering when minimized, scale coordinates for retina displays (screen coordinates != framebuffer coordinates)
		int fbWidth = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
		int fbHeight = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);
		if ( fbWidth <= 0 || fbHeight <= 0 )
		{
			return;
		}

		// Allocate array to store enough vertex/index buffers
		if ( mainWindowRenderBuffers.FrameRenderBuffers is null )
		{
			mainWindowRenderBuffers.Index = 0;
			mainWindowRenderBuffers.Count = (uint)engine.SwapchainImageCount;
			mainWindowRenderBuffers.FrameRenderBuffers = new FrameRenderBuffer[(int)mainWindowRenderBuffers.Count];

			for ( var i = 0; i < mainWindowRenderBuffers.Count; i++ )
			{
				mainWindowRenderBuffers.FrameRenderBuffers[i] = new FrameRenderBuffer();
				mainWindowRenderBuffers.FrameRenderBuffers[i].VertexBuffer.Buffer.Handle = 0;
				mainWindowRenderBuffers.FrameRenderBuffers[i].IndexBuffer.Buffer.Handle = 0;
			}
		}
		mainWindowRenderBuffers.Index = (mainWindowRenderBuffers.Index + 1) % mainWindowRenderBuffers.Count;

		ref FrameRenderBuffer frameRenderBuffer = ref mainWindowRenderBuffers.FrameRenderBuffers[mainWindowRenderBuffers.Index];

		if ( drawData.TotalVtxCount > 0 )
		{
			// Create or resize the vertex/index buffers
			long vertexSize = (long)drawData.TotalVtxCount * (long)sizeof( ImDrawVert );
			long indexSize = (long)drawData.TotalIdxCount * sizeof( ushort );
			if ( frameRenderBuffer.VertexBuffer.Buffer.Handle == default || frameRenderBuffer.VertexBuffer.Allocation.Size < vertexSize )
				CreateOrResizeBuffer( ref frameRenderBuffer.VertexBuffer, (ulong)vertexSize, BufferUsageFlags.VertexBufferBit );
			if ( frameRenderBuffer.IndexBuffer.Buffer.Handle == default || frameRenderBuffer.IndexBuffer.Allocation.Size < indexSize )
				CreateOrResizeBuffer( ref frameRenderBuffer.IndexBuffer, (ulong)indexSize, BufferUsageFlags.IndexBufferBit );

			// Upload vertex/index data into a single contiguous GPU buffer
			uint vtxOffset = 0;
			uint idxOffset = 0;

			for ( var i = 0; i < drawData.CmdListsCount; i++ )
			{
				ImDrawList* cmdList = drawDataPtr.CmdLists[i];
				var vertexByteSize = (uint)cmdList->VtxBuffer.Size * (uint)sizeof( ImDrawVert );
				var indexByteSize = (uint)cmdList->IdxBuffer.Size * sizeof( ushort );

				frameRenderBuffer.VertexBuffer.Allocation.SetMemory(
					cmdList->VtxBuffer.Data,
					vertexByteSize,
					(nint)vtxOffset );

				frameRenderBuffer.IndexBuffer.Allocation.SetMemory(
					cmdList->IdxBuffer.Data,
					indexByteSize,
					(nint)idxOffset );

				vtxOffset += vertexByteSize;
				idxOffset += indexByteSize;
			}
		}

		// Setup desired Vulkan state
		Apis.Vk.CmdBindPipeline( commandBuffer, PipelineBindPoint.Graphics, pipeline );
		Apis.Vk.CmdBindDescriptorSets( commandBuffer, PipelineBindPoint.Graphics, pipelineLayout, 0, 1, descriptorSet, 0, null );

		// Bind Vertex And Index Buffer:
		if ( drawData.TotalVtxCount > 0 )
		{
			var vertexBuffers = stackalloc Buffer[] { frameRenderBuffer.VertexBuffer.Buffer };
			Apis.Vk.CmdBindVertexBuffers( commandBuffer, 0, 1, vertexBuffers, 0 );
			Apis.Vk.CmdBindIndexBuffer( commandBuffer, frameRenderBuffer.IndexBuffer.Buffer, 0, sizeof( ushort ) == 2 ? IndexType.Uint16 : IndexType.Uint32 );
		}

		// Setup viewport:
		Viewport viewport;
		viewport.X = 0;
		viewport.Y = 0;
		viewport.Width = fbWidth;
		viewport.Height = fbHeight;
		viewport.MinDepth = 0.0f;
		viewport.MaxDepth = 1.0f;
		Apis.Vk.CmdSetViewport( commandBuffer, 0, 1, &viewport );

		// Setup scale and translation:
		// Our visible imgui space lies from draw_data.DisplayPps (top left) to draw_data.DisplayPos+data_data.DisplaySize (bottom right). DisplayPos is (0,0) for single viewport apps.
		Span<float> scale = stackalloc float[2];
		scale[0] = 2.0f / drawData.DisplaySize.X;
		scale[1] = 2.0f / drawData.DisplaySize.Y;
		Span<float> translate = stackalloc float[2];
		translate[0] = -1.0f - drawData.DisplayPos.X * scale[0];
		translate[1] = -1.0f - drawData.DisplayPos.Y * scale[1];
		Apis.Vk.CmdPushConstants( commandBuffer, pipelineLayout, ShaderStageFlags.VertexBit, sizeof( float ) * 0, sizeof( float ) * 2, scale );
		Apis.Vk.CmdPushConstants( commandBuffer, pipelineLayout, ShaderStageFlags.VertexBit, sizeof( float ) * 2, sizeof( float ) * 2, translate );

		// Will project scissor/clipping rectangles into framebuffer space
		Vector2 clipOff = drawData.DisplayPos;         // (0,0) unless using multi-viewports
		Vector2 clipScale = drawData.FramebufferScale; // (1,1) unless using retina display which are often (2,2)

		// Render command lists
		// (Because we merged all buffers into a single one, we maintain our own offset into them)
		int vertexOffset = 0;
		int indexOffset = 0;
		for ( int n = 0; n < drawData.CmdListsCount; n++ )
		{
			ImDrawList* cmdList = drawDataPtr.CmdLists[n];
			for ( int cmd_i = 0; cmd_i < cmdList->CmdBuffer.Size; cmd_i++ )
			{
				ref ImDrawCmd pcmd = ref cmdList->CmdBuffer.Ref<ImDrawCmd>( cmd_i );

				// Project scissor/clipping rectangles into framebuffer space
				Vector4 clipRect;
				clipRect.X = (pcmd.ClipRect.X - clipOff.X) * clipScale.X;
				clipRect.Y = (pcmd.ClipRect.Y - clipOff.Y) * clipScale.Y;
				clipRect.Z = (pcmd.ClipRect.Z - clipOff.X) * clipScale.X;
				clipRect.W = (pcmd.ClipRect.W - clipOff.Y) * clipScale.Y;

				if ( clipRect.X < fbWidth && clipRect.Y < fbHeight && clipRect.Z >= 0.0f && clipRect.W >= 0.0f )
				{
					// Negative offsets are illegal for vkCmdSetScissor
					if ( clipRect.X < 0.0f )
						clipRect.X = 0.0f;
					if ( clipRect.Y < 0.0f )
						clipRect.Y = 0.0f;

					// Apply scissor/clipping rectangle
					var scissor = new Rect2D();
					scissor.Offset.X = (int)clipRect.X;
					scissor.Offset.Y = (int)clipRect.Y;
					scissor.Extent.Width = (uint)(clipRect.Z - clipRect.X);
					scissor.Extent.Height = (uint)(clipRect.W - clipRect.Y);
					Apis.Vk.CmdSetScissor( commandBuffer, 0, 1, &scissor );

					// Draw
					Apis.Vk.CmdDrawIndexed( commandBuffer, pcmd.ElemCount, 1, pcmd.IdxOffset + (uint)indexOffset, (int)pcmd.VtxOffset + vertexOffset, 0 );
				}
			}
			indexOffset += cmdList->IdxBuffer.Size;
			vertexOffset += cmdList->VtxBuffer.Size;
		}
	}

	private unsafe void CreateOrResizeBuffer( ref AllocatedBuffer allocatedBuffer, ulong newSize, BufferUsageFlags usage )
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		if ( allocatedBuffer.Buffer.Handle != default )
		{
			Apis.Vk.DestroyBuffer( VkContext.LogicalDevice, allocatedBuffer.Buffer, default );
			allocatedBuffer.Allocation.Dispose();
		}

		var bufferInfo = VkInfo.Buffer( newSize, usage, SharingMode.Exclusive );
		var buffer = VkContext.AllocationManager.CreateBuffer( bufferInfo, new AllocationCreateInfo
		{
			RequiredFlags = MemoryPropertyFlags.HostVisibleBit,
			Usage = MemoryUsage.CPU_To_GPU
		}, out var bufferAllocation );
		VkInvalidHandleException.ThrowIfInvalid( buffer );
		allocatedBuffer = new AllocatedBuffer( buffer, bufferAllocation );
	}

	private void OnKeyChar( IKeyboard keyboard, char key )
	{
		pressedChars.Add( key );
	}

	/// <summary>
	/// Frees all graphics resources used by the renderer.
	/// </summary>
	public unsafe void Dispose()
	{
		if ( !VkContext.IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		keyboard.KeyChar -= OnKeyChar;

		if ( mainWindowRenderBuffers.FrameRenderBuffers is not null )
		{
			for ( var i = 0; i < mainWindowRenderBuffers.Count; i++ )
			{
				var frameRenderBuffer = mainWindowRenderBuffers.FrameRenderBuffers[i];

				Apis.Vk.DestroyBuffer( VkContext.LogicalDevice, frameRenderBuffer.VertexBuffer.Buffer, null );
				frameRenderBuffer.VertexBuffer.Allocation.Dispose();
				Apis.Vk.DestroyBuffer( VkContext.LogicalDevice, frameRenderBuffer.IndexBuffer.Buffer, null );
				frameRenderBuffer.IndexBuffer.Allocation.Dispose();
			}
		}

		disposalManager?.Dispose();

		ImGuiNET.ImGui.DestroyContext();
		GC.SuppressFinalize( this );
	}

	private class FrameRenderBuffer
	{
		public AllocatedBuffer VertexBuffer;
		public AllocatedBuffer IndexBuffer;
	};

	private sealed class WindowRenderBuffers
	{
		public uint Index;
		public uint Count;
		public FrameRenderBuffer[]? FrameRenderBuffers;
	};
}

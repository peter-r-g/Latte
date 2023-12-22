using ImGuiNET;
using Latte.NewRenderer.Exceptions;
using Latte.NewRenderer.Extensions;
using Silk.NET.Core.Native;
using Silk.NET.Input;
using Silk.NET.Input.Extensions;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Latte.NewRenderer.ImGui;

public sealed class ImGuiController : IDisposable
{
	private readonly List<char> pressedChars = [];

	private VkEngine engine = null!;
	private IInputContext input = null!;
	private IKeyboard keyboard = null!;
	private bool frameBegun;
	private DescriptorPool descriptorPool;
	private RenderPass renderPass;
	private Sampler fontSampler;
	private DescriptorSetLayout descriptorSetLayout;
	private DescriptorSet descriptorSet;
	private PipelineLayout pipelineLayout;
	private ShaderModule shaderModuleVert;
	private ShaderModule shaderModuleFrag;
	private Pipeline pipeline;
	private WindowRenderBuffers mainWindowRenderBuffers;
	private GlobalMemory? frameRenderBuffers;
	private DeviceMemory fontMemory;
	private Image fontImage;
	private ImageView fontView;
	private ulong bufferMemoryAlignment = 256;

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
	internal ImGuiController( VkEngine engine, IInputContext input )
	{
		var context = ImGuiNET.ImGui.CreateContext();
		ImGuiNET.ImGui.SetCurrentContext( context );

		// Use the default font
		var io = ImGuiNET.ImGui.GetIO();
		io.Fonts.AddFontDefault();
		io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;

		Init( engine, input );

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
	internal unsafe ImGuiController( VkEngine engine, IInputContext input, ImGuiFontConfig imGuiFontConfig )
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

		Init( engine, input );

		SetPerFrameImGuiData( 1f / 60f );

		BeginFrame();
	}

	private unsafe void Init( VkEngine engine, IInputContext input )
	{
		this.engine = engine;
		this.input = input;

		if ( engine.SwapchainImageCount < 2 )
			throw new Exception( $"Swap chain image count must be >= 2" );

		InitializeStyle();
		InitializeRenderPass( engine.SwapchainImageFormat, engine.DepthFormat );
		InitializeSampler();
		InitializeDescriptors();
		InitializePipeline();
		UploadDefaultFontAtlas( engine.GraphicsQueueFamily );
	}

	/// <summary>
	/// Updates ImGui input and IO configuration state. Call Update() before drawing and rendering.
	/// </summary>
	public void Update( float deltaSeconds )
	{
		if ( frameBegun )
		{
			ImGuiNET.ImGui.Render();
		}

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

	private unsafe void InitializeRenderPass( Format swapchainFormat, Format? depthBufferFormat )
	{
		// Create the render pass
		var colorAttachment = new AttachmentDescription
		{
			Format = swapchainFormat,
			Samples = SampleCountFlags.Count1Bit,
			LoadOp = AttachmentLoadOp.Load,
			StoreOp = AttachmentStoreOp.Store,
			StencilLoadOp = AttachmentLoadOp.DontCare,
			StencilStoreOp = AttachmentStoreOp.DontCare,
			InitialLayout = AttachmentLoadOp.Load == AttachmentLoadOp.Clear ? ImageLayout.Undefined : ImageLayout.PresentSrcKhr,
			FinalLayout = ImageLayout.PresentSrcKhr
		};

		var colorAttachmentRef = new AttachmentReference
		{
			Attachment = 0,
			Layout = ImageLayout.ColorAttachmentOptimal
		};

		var subpass = new SubpassDescription
		{
			PipelineBindPoint = PipelineBindPoint.Graphics,
			ColorAttachmentCount = 1,
			PColorAttachments = (AttachmentReference*)Unsafe.AsPointer( ref colorAttachmentRef )
		};

		Span<AttachmentDescription> attachments = stackalloc AttachmentDescription[] { colorAttachment };
		var depthAttachment = new AttachmentDescription();
		var depthAttachmentRef = new AttachmentReference();
		if ( depthBufferFormat.HasValue )
		{
			depthAttachment.Format = depthBufferFormat.Value;
			depthAttachment.Samples = SampleCountFlags.Count1Bit;
			depthAttachment.LoadOp = AttachmentLoadOp.Load;
			depthAttachment.StoreOp = AttachmentStoreOp.Store;
			depthAttachment.StencilLoadOp = AttachmentLoadOp.DontCare;
			depthAttachment.StencilStoreOp = AttachmentStoreOp.DontCare;
			depthAttachment.InitialLayout = AttachmentLoadOp.Load == AttachmentLoadOp.Clear ? ImageLayout.Undefined : ImageLayout.DepthStencilAttachmentOptimal;
			depthAttachment.FinalLayout = ImageLayout.DepthStencilAttachmentOptimal;

			depthAttachmentRef.Attachment = 1;
			depthAttachmentRef.Layout = ImageLayout.DepthStencilAttachmentOptimal;

			subpass.PDepthStencilAttachment = (AttachmentReference*)Unsafe.AsPointer( ref depthAttachmentRef );

			attachments = stackalloc AttachmentDescription[] { colorAttachment, depthAttachment };
		}

		var dependency = new SubpassDependency
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

		Span<SubpassDependency> dependencies = stackalloc SubpassDependency[] { dependency, depthDependency };

		var renderPassInfo = new RenderPassCreateInfo
		{
			SType = StructureType.RenderPassCreateInfo,
			AttachmentCount = (uint)attachments.Length,
			PAttachments = (AttachmentDescription*)Unsafe.AsPointer( ref attachments.GetPinnableReference() ),
			SubpassCount = 1,
			PSubpasses = (SubpassDescription*)Unsafe.AsPointer( ref subpass ),
			DependencyCount = (uint)dependencies.Length,
			PDependencies = (SubpassDependency*)Unsafe.AsPointer( ref dependencies.GetPinnableReference() )
		};

		if ( Apis.Vk.CreateRenderPass( engine.LogicalDevice, renderPassInfo, default, out renderPass ) != Result.Success )
		{
			throw new Exception( $"Failed to create render pass" );
		}
	}

	private unsafe void InitializeSampler()
	{
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

		if ( Apis.Vk.CreateSampler( engine.LogicalDevice, info, default, out fontSampler ) != Result.Success )
		{
			throw new Exception( $"Unable to create sampler" );
		}
	}

	private unsafe void InitializeDescriptors()
	{
		var logicalDevice = engine.LogicalDevice;

		// Create the descriptor pool for ImGui
		Span<DescriptorPoolSize> poolSizes = stackalloc DescriptorPoolSize[] { new DescriptorPoolSize( DescriptorType.CombinedImageSampler, 1 ) };
		var descriptorPool = new DescriptorPoolCreateInfo
		{
			SType = StructureType.DescriptorPoolCreateInfo,
			PoolSizeCount = (uint)poolSizes.Length,
			PPoolSizes = (DescriptorPoolSize*)Unsafe.AsPointer( ref poolSizes.GetPinnableReference() ),
			MaxSets = 1
		};

		if ( Apis.Vk.CreateDescriptorPool( logicalDevice, descriptorPool, default, out this.descriptorPool ) != Result.Success )
		{
			throw new Exception( $"Unable to create descriptor pool" );
		}

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

		if ( Apis.Vk.CreateDescriptorSetLayout( logicalDevice, descriptorInfo, default, out descriptorSetLayout ) != Result.Success )
		{
			throw new Exception( $"Unable to create descriptor set layout" );
		}

		fixed ( DescriptorSetLayout* pg_DescriptorSetLayout = &descriptorSetLayout )
		{
			var alloc_info = new DescriptorSetAllocateInfo
			{
				SType = StructureType.DescriptorSetAllocateInfo,
				DescriptorPool = this.descriptorPool,
				DescriptorSetCount = 1,
				PSetLayouts = pg_DescriptorSetLayout
			};
			if ( Apis.Vk.AllocateDescriptorSets( logicalDevice, alloc_info, out descriptorSet ) != Result.Success )
			{
				throw new Exception( $"Unable to create descriptor sets" );
			}
		}
	}

	private unsafe void InitializePipeline()
	{
		var logicalDevice = engine.LogicalDevice;

		var vertPushConst = new PushConstantRange
		{
			StageFlags = ShaderStageFlags.VertexBit,
			Offset = sizeof( float ) * 0,
			Size = sizeof( float ) * 4
		};

		var set_layout = descriptorSetLayout;
		var layout_info = new PipelineLayoutCreateInfo
		{
			SType = StructureType.PipelineLayoutCreateInfo,
			SetLayoutCount = 1,
			PSetLayouts = (DescriptorSetLayout*)Unsafe.AsPointer( ref set_layout ),
			PushConstantRangeCount = 1,
			PPushConstantRanges = (PushConstantRange*)Unsafe.AsPointer( ref vertPushConst )
		};
		if ( Apis.Vk.CreatePipelineLayout( logicalDevice, layout_info, default, out pipelineLayout ) != Result.Success )
		{
			throw new Exception( $"Unable to create the descriptor set layout" );
		}

		// Create the shader modules
		if ( shaderModuleVert.Handle == default )
		{
			fixed ( uint* vertShaderBytes = &Shaders.VertexShader[0] )
			{
				var vert_info = new ShaderModuleCreateInfo
				{
					SType = StructureType.ShaderModuleCreateInfo,
					CodeSize = (nuint)Shaders.VertexShader.Length * sizeof( uint ),
					PCode = vertShaderBytes
				};
				if ( Apis.Vk.CreateShaderModule( logicalDevice, vert_info, default, out shaderModuleVert ) != Result.Success )
				{
					throw new Exception( $"Unable to create the vertex shader" );
				}
			}
		}
		if ( shaderModuleFrag.Handle == default )
		{
			fixed ( uint* fragShaderBytes = &Shaders.FragmentShader[0] )
			{
				var frag_info = new ShaderModuleCreateInfo
				{
					SType = StructureType.ShaderModuleCreateInfo,
					CodeSize = (nuint)Shaders.FragmentShader.Length * sizeof( uint ),
					PCode = fragShaderBytes
				};
				if ( Apis.Vk.CreateShaderModule( logicalDevice, frag_info, default, out shaderModuleFrag ) != Result.Success )
				{
					throw new Exception( $"Unable to create the fragment shader" );
				}
			}
		}

		// Create the pipeline
		Span<PipelineShaderStageCreateInfo> stage = stackalloc PipelineShaderStageCreateInfo[2];
		stage[0].SType = StructureType.PipelineShaderStageCreateInfo;
		stage[0].Stage = ShaderStageFlags.VertexBit;
		stage[0].Module = shaderModuleVert;
		stage[0].PName = (byte*)SilkMarshal.StringToPtr( "main" );
		stage[1].SType = StructureType.PipelineShaderStageCreateInfo;
		stage[1].Stage = ShaderStageFlags.FragmentBit;
		stage[1].Module = shaderModuleFrag;
		stage[1].PName = (byte*)SilkMarshal.StringToPtr( "main" );

		var binding_desc = new VertexInputBindingDescription
		{
			Stride = (uint)Unsafe.SizeOf<ImDrawVert>(),
			InputRate = VertexInputRate.Vertex
		};

		Span<VertexInputAttributeDescription> attribute_desc = stackalloc VertexInputAttributeDescription[3];
		attribute_desc[0].Location = 0;
		attribute_desc[0].Binding = binding_desc.Binding;
		attribute_desc[0].Format = Format.R32G32Sfloat;
		attribute_desc[0].Offset = (uint)Marshal.OffsetOf<ImDrawVert>( nameof( ImDrawVert.pos ) );
		attribute_desc[1].Location = 1;
		attribute_desc[1].Binding = binding_desc.Binding;
		attribute_desc[1].Format = Format.R32G32Sfloat;
		attribute_desc[1].Offset = (uint)Marshal.OffsetOf<ImDrawVert>( nameof( ImDrawVert.uv ) );
		attribute_desc[2].Location = 2;
		attribute_desc[2].Binding = binding_desc.Binding;
		attribute_desc[2].Format = Format.R8G8B8A8Unorm;
		attribute_desc[2].Offset = (uint)Marshal.OffsetOf<ImDrawVert>( nameof( ImDrawVert.col ) );

		var vertex_info = new PipelineVertexInputStateCreateInfo
		{
			SType = StructureType.PipelineVertexInputStateCreateInfo,
			VertexBindingDescriptionCount = 1,
			PVertexBindingDescriptions = (VertexInputBindingDescription*)Unsafe.AsPointer( ref binding_desc ),
			VertexAttributeDescriptionCount = 3,
			PVertexAttributeDescriptions = (VertexInputAttributeDescription*)Unsafe.AsPointer( ref attribute_desc[0] )
		};

		var ia_info = new PipelineInputAssemblyStateCreateInfo
		{
			SType = StructureType.PipelineInputAssemblyStateCreateInfo,
			Topology = PrimitiveTopology.TriangleList
		};

		var viewport_info = new PipelineViewportStateCreateInfo
		{
			SType = StructureType.PipelineViewportStateCreateInfo,
			ViewportCount = 1,
			ScissorCount = 1
		};

		var raster_info = new PipelineRasterizationStateCreateInfo
		{
			SType = StructureType.PipelineRasterizationStateCreateInfo,
			PolygonMode = PolygonMode.Fill,
			CullMode = CullModeFlags.None,
			FrontFace = FrontFace.CounterClockwise,
			LineWidth = 1.0f
		};

		var ms_info = new PipelineMultisampleStateCreateInfo
		{
			SType = StructureType.PipelineMultisampleStateCreateInfo,
			RasterizationSamples = SampleCountFlags.Count1Bit
		};

		var color_attachment = new PipelineColorBlendAttachmentState
		{
			BlendEnable = new Silk.NET.Core.Bool32( true ),
			SrcColorBlendFactor = BlendFactor.SrcAlpha,
			DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
			ColorBlendOp = BlendOp.Add,
			SrcAlphaBlendFactor = BlendFactor.One,
			DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
			AlphaBlendOp = BlendOp.Add,
			ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit
		};

		var depth_info = new PipelineDepthStencilStateCreateInfo
		{
			SType = StructureType.PipelineDepthStencilStateCreateInfo
		};

		var blend_info = new PipelineColorBlendStateCreateInfo
		{
			SType = StructureType.PipelineColorBlendStateCreateInfo,
			AttachmentCount = 1,
			PAttachments = (PipelineColorBlendAttachmentState*)Unsafe.AsPointer( ref color_attachment )
		};

		Span<DynamicState> dynamic_states = stackalloc DynamicState[] { DynamicState.Viewport, DynamicState.Scissor };
		var dynamic_state = new PipelineDynamicStateCreateInfo
		{
			SType = StructureType.PipelineDynamicStateCreateInfo,
			DynamicStateCount = (uint)dynamic_states.Length,
			PDynamicStates = (DynamicState*)Unsafe.AsPointer( ref dynamic_states[0] )
		};

		var pipelineInfo = new GraphicsPipelineCreateInfo
		{
			SType = StructureType.GraphicsPipelineCreateInfo,
			Flags = default,
			StageCount = 2,
			PStages = (PipelineShaderStageCreateInfo*)Unsafe.AsPointer( ref stage[0] ),
			PVertexInputState = (PipelineVertexInputStateCreateInfo*)Unsafe.AsPointer( ref vertex_info ),
			PInputAssemblyState = (PipelineInputAssemblyStateCreateInfo*)Unsafe.AsPointer( ref ia_info ),
			PViewportState = (PipelineViewportStateCreateInfo*)Unsafe.AsPointer( ref viewport_info ),
			PRasterizationState = (PipelineRasterizationStateCreateInfo*)Unsafe.AsPointer( ref raster_info ),
			PMultisampleState = (PipelineMultisampleStateCreateInfo*)Unsafe.AsPointer( ref ms_info ),
			PDepthStencilState = (PipelineDepthStencilStateCreateInfo*)Unsafe.AsPointer( ref depth_info ),
			PColorBlendState = (PipelineColorBlendStateCreateInfo*)Unsafe.AsPointer( ref blend_info ),
			PDynamicState = (PipelineDynamicStateCreateInfo*)Unsafe.AsPointer( ref dynamic_state ),
			Layout = pipelineLayout,
			RenderPass = renderPass,
			Subpass = 0
		};

		if ( Apis.Vk.CreateGraphicsPipelines( logicalDevice, default, 1, pipelineInfo, default, out pipeline ) != Result.Success )
		{
			throw new Exception( $"Unable to create the pipeline" );
		}

		SilkMarshal.Free( (nint)stage[0].PName );
		SilkMarshal.Free( (nint)stage[1].PName );
	}

	private unsafe void UploadDefaultFontAtlas( uint graphicsFamilyIndex )
	{
		var logicalDevice = engine.LogicalDevice;

		// Initialise ImGui Vulkan adapter
		var io = ImGuiNET.ImGui.GetIO();
		io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
		io.Fonts.GetTexDataAsRGBA32( out IntPtr pixels, out int width, out int height );
		ulong upload_size = (ulong)(width * height * 4 * sizeof( byte ));

		// Submit one-time command to create the fonts texture
		var poolInfo = new CommandPoolCreateInfo
		{
			SType = StructureType.CommandPoolCreateInfo,
			QueueFamilyIndex = graphicsFamilyIndex
		};
		if ( Apis.Vk.CreateCommandPool( logicalDevice, poolInfo, null, out var commandPool ) != Result.Success )
		{
			throw new Exception( "failed to create command pool!" );
		}

		var allocInfo = new CommandBufferAllocateInfo
		{
			SType = StructureType.CommandBufferAllocateInfo,
			CommandPool = commandPool,
			Level = CommandBufferLevel.Primary,
			CommandBufferCount = 1
		};
		if ( Apis.Vk.AllocateCommandBuffers( logicalDevice, allocInfo, out var commandBuffer ) != Result.Success )
		{
			throw new Exception( $"Unable to allocate command buffers" );
		}

		var beginInfo = new CommandBufferBeginInfo
		{
			SType = StructureType.CommandBufferBeginInfo,
			Flags = CommandBufferUsageFlags.OneTimeSubmitBit
		};
		if ( Apis.Vk.BeginCommandBuffer( commandBuffer, beginInfo ) != Result.Success )
		{
			throw new Exception( $"Failed to begin a command buffer" );
		}

		var imageInfo = new ImageCreateInfo
		{
			SType = StructureType.ImageCreateInfo,
			ImageType = ImageType.Type2D,
			Format = Format.R8G8B8A8Unorm
		};
		imageInfo.Extent.Width = (uint)width;
		imageInfo.Extent.Height = (uint)height;
		imageInfo.Extent.Depth = 1;
		imageInfo.MipLevels = 1;
		imageInfo.ArrayLayers = 1;
		imageInfo.Samples = SampleCountFlags.Count1Bit;
		imageInfo.Tiling = ImageTiling.Optimal;
		imageInfo.Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit;
		imageInfo.SharingMode = SharingMode.Exclusive;
		imageInfo.InitialLayout = ImageLayout.Undefined;
		if ( Apis.Vk.CreateImage( logicalDevice, imageInfo, default, out fontImage ) != Result.Success )
		{
			throw new Exception( $"Failed to create font image" );
		}
		Apis.Vk.GetImageMemoryRequirements( logicalDevice, fontImage, out var fontReq );
		var fontAllocInfo = new MemoryAllocateInfo
		{
			SType = StructureType.MemoryAllocateInfo,
			AllocationSize = fontReq.Size,
			MemoryTypeIndex = GetMemoryTypeIndex( MemoryPropertyFlags.DeviceLocalBit, fontReq.MemoryTypeBits )
		};
		if ( Apis.Vk.AllocateMemory( logicalDevice, &fontAllocInfo, default, out fontMemory ) != Result.Success )
		{
			throw new Exception( $"Failed to allocate device memory" );
		}
		if ( Apis.Vk.BindImageMemory( logicalDevice, fontImage, fontMemory, 0 ) != Result.Success )
		{
			throw new Exception( $"Failed to bind device memory" );
		}

		var imageViewInfo = new ImageViewCreateInfo
		{
			SType = StructureType.ImageViewCreateInfo,
			Image = fontImage,
			ViewType = ImageViewType.Type2D,
			Format = Format.R8G8B8A8Unorm
		};
		imageViewInfo.SubresourceRange.AspectMask = ImageAspectFlags.ColorBit;
		imageViewInfo.SubresourceRange.LevelCount = 1;
		imageViewInfo.SubresourceRange.LayerCount = 1;
		if ( Apis.Vk.CreateImageView( logicalDevice, &imageViewInfo, default, out fontView ) != Result.Success )
		{
			throw new Exception( $"Failed to create an image view" );
		}

		var descImageInfo = new DescriptorImageInfo
		{
			Sampler = fontSampler,
			ImageView = fontView,
			ImageLayout = ImageLayout.ShaderReadOnlyOptimal
		};
		var writeDescriptors = new WriteDescriptorSet
		{
			SType = StructureType.WriteDescriptorSet,
			DstSet = descriptorSet,
			DescriptorCount = 1,
			DescriptorType = DescriptorType.CombinedImageSampler,
			PImageInfo = (DescriptorImageInfo*)Unsafe.AsPointer( ref descImageInfo )
		};
		Apis.Vk.UpdateDescriptorSets( logicalDevice, 1, writeDescriptors, 0, default );

		// Create the Upload Buffer:
		var bufferInfo = new BufferCreateInfo
		{
			SType = StructureType.BufferCreateInfo,
			Size = upload_size,
			Usage = BufferUsageFlags.TransferSrcBit,
			SharingMode = SharingMode.Exclusive
		};
		if ( Apis.Vk.CreateBuffer( logicalDevice, bufferInfo, default, out var uploadBuffer ) != Result.Success )
		{
			throw new Exception( $"Failed to create a device buffer" );
		}

		Apis.Vk.GetBufferMemoryRequirements( logicalDevice, uploadBuffer, out var uploadReq );
		bufferMemoryAlignment = (bufferMemoryAlignment > uploadReq.Alignment) ? bufferMemoryAlignment : uploadReq.Alignment;

		var uploadAllocInfo = new MemoryAllocateInfo
		{
			SType = StructureType.MemoryAllocateInfo,
			AllocationSize = uploadReq.Size,
			MemoryTypeIndex = GetMemoryTypeIndex( MemoryPropertyFlags.HostVisibleBit, uploadReq.MemoryTypeBits )
		};
		if ( Apis.Vk.AllocateMemory( logicalDevice, uploadAllocInfo, default, out var uploadBufferMemory ) != Result.Success )
		{
			throw new Exception( $"Failed to allocate device memory" );
		}
		if ( Apis.Vk.BindBufferMemory( logicalDevice, uploadBuffer, uploadBufferMemory, 0 ) != Result.Success )
		{
			throw new Exception( $"Failed to bind device memory" );
		}

		void* map = null;
		if ( Apis.Vk.MapMemory( logicalDevice, uploadBufferMemory, 0, upload_size, 0, (void**)(&map) ) != Result.Success )
		{
			throw new Exception( $"Failed to map device memory" );
		}
		Unsafe.CopyBlock( map, pixels.ToPointer(), (uint)upload_size );

		var range = new MappedMemoryRange
		{
			SType = StructureType.MappedMemoryRange,
			Memory = uploadBufferMemory,
			Size = upload_size
		};
		if ( Apis.Vk.FlushMappedMemoryRanges( logicalDevice, 1, range ) != Result.Success )
		{
			throw new Exception( $"Failed to flush memory to device" );
		}
		Apis.Vk.UnmapMemory( logicalDevice, uploadBufferMemory );

		const uint VK_QUEUE_FAMILY_IGNORED = ~0U;

		var copyBarrier = new ImageMemoryBarrier
		{
			SType = StructureType.ImageMemoryBarrier,
			DstAccessMask = AccessFlags.TransferWriteBit,
			OldLayout = ImageLayout.Undefined,
			NewLayout = ImageLayout.TransferDstOptimal,
			SrcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
			DstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
			Image = fontImage
		};
		copyBarrier.SubresourceRange.AspectMask = ImageAspectFlags.ColorBit;
		copyBarrier.SubresourceRange.LevelCount = 1;
		copyBarrier.SubresourceRange.LayerCount = 1;
		Apis.Vk.CmdPipelineBarrier( commandBuffer, PipelineStageFlags.HostBit, PipelineStageFlags.TransferBit, 0, 0, default, 0, default, 1, copyBarrier );

		var region = new BufferImageCopy();
		region.ImageSubresource.AspectMask = ImageAspectFlags.ColorBit;
		region.ImageSubresource.LayerCount = 1;
		region.ImageExtent.Width = (uint)width;
		region.ImageExtent.Height = (uint)height;
		region.ImageExtent.Depth = 1;
		Apis.Vk.CmdCopyBufferToImage( commandBuffer, uploadBuffer, fontImage, ImageLayout.TransferDstOptimal, 1, &region );

		var use_barrier = new ImageMemoryBarrier
		{
			SType = StructureType.ImageMemoryBarrier,
			SrcAccessMask = AccessFlags.TransferWriteBit,
			DstAccessMask = AccessFlags.ShaderReadBit,
			OldLayout = ImageLayout.TransferDstOptimal,
			NewLayout = ImageLayout.ShaderReadOnlyOptimal,
			SrcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
			DstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
			Image = fontImage
		};
		use_barrier.SubresourceRange.AspectMask = ImageAspectFlags.ColorBit;
		use_barrier.SubresourceRange.LevelCount = 1;
		use_barrier.SubresourceRange.LayerCount = 1;
		Apis.Vk.CmdPipelineBarrier( commandBuffer, PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit, 0, 0, default, 0, default, 1, use_barrier );

		// Store our identifier
		io.Fonts.SetTexID( (IntPtr)fontImage.Handle );

		if ( Apis.Vk.EndCommandBuffer( commandBuffer ) != Result.Success )
		{
			throw new Exception( $"Failed to begin a command buffer" );
		}

		Apis.Vk.GetDeviceQueue( logicalDevice, graphicsFamilyIndex, 0, out var graphicsQueue );

		var submitInfo = new SubmitInfo
		{
			SType = StructureType.SubmitInfo,
			CommandBufferCount = 1,
			PCommandBuffers = (CommandBuffer*)Unsafe.AsPointer( ref commandBuffer )
		};
		if ( Apis.Vk.QueueSubmit( graphicsQueue, 1, submitInfo, default ) != Result.Success )
		{
			throw new Exception( $"Failed to begin a command buffer" );
		}

		if ( Apis.Vk.QueueWaitIdle( graphicsQueue ) != Result.Success )
		{
			throw new Exception( $"Failed to begin a command buffer" );
		}

		Apis.Vk.DestroyBuffer( logicalDevice, uploadBuffer, default );
		Apis.Vk.FreeMemory( logicalDevice, uploadBufferMemory, default );
		Apis.Vk.DestroyCommandPool( logicalDevice, commandPool, default );
	}

	private uint GetMemoryTypeIndex( MemoryPropertyFlags properties, uint type_bits )
	{
		Apis.Vk.GetPhysicalDeviceMemoryProperties( engine.PhysicalDevice, out var prop );
		for ( int i = 0; i < prop.MemoryTypeCount; i++ )
		{
			if ( (prop.MemoryTypes[i].PropertyFlags & properties) == properties && (type_bits & (1u << i)) != 0 )
			{
				return (uint)i;
			}
		}
		return 0xFFFFFFFF; // Unable to find memoryType
	}

	private void BeginFrame()
	{
		ImGuiNET.ImGui.NewFrame();
		frameBegun = true;
		keyboard = input.Keyboards[0];
		keyboard.KeyChar += OnKeyChar;
	}

	private void OnKeyChar( IKeyboard keyboard, char key )
	{
		pressedChars.Add( key );
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
		var logicalDevice = engine.LogicalDevice;

		int framebufferWidth = (int)(drawDataPtr.DisplaySize.X * drawDataPtr.FramebufferScale.X);
		int framebufferHeight = (int)(drawDataPtr.DisplaySize.Y * drawDataPtr.FramebufferScale.Y);
		if ( framebufferWidth <= 0 || framebufferHeight <= 0 )
		{
			return;
		}

		var renderPassInfo = new RenderPassBeginInfo
		{
			SType = StructureType.RenderPassBeginInfo,
			RenderPass = renderPass,
			Framebuffer = framebuffer
		};
		renderPassInfo.RenderArea.Offset = default;
		renderPassInfo.RenderArea.Extent = swapchainExtent;
		renderPassInfo.ClearValueCount = 0;
		renderPassInfo.PClearValues = default;

		Apis.Vk.CmdBeginRenderPass( commandBuffer, &renderPassInfo, SubpassContents.Inline );

		var drawData = *drawDataPtr.NativePtr;

		// Avoid rendering when minimized, scale coordinates for retina displays (screen coordinates != framebuffer coordinates)
		int fb_width = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
		int fb_height = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);
		if ( fb_width <= 0 || fb_height <= 0 )
		{
			return;
		}

		// Allocate array to store enough vertex/index buffers
		if ( mainWindowRenderBuffers.FrameRenderBuffers is null )
		{
			mainWindowRenderBuffers.Index = 0;
			mainWindowRenderBuffers.Count = (uint)engine.SwapchainImageCount;
			frameRenderBuffers = GlobalMemory.Allocate( sizeof( FrameRenderBuffer ) * (int)mainWindowRenderBuffers.Count );
			mainWindowRenderBuffers.FrameRenderBuffers = frameRenderBuffers.AsPtr<FrameRenderBuffer>();
			for ( int i = 0; i < (int)mainWindowRenderBuffers.Count; i++ )
			{
				mainWindowRenderBuffers.FrameRenderBuffers[i].IndexBuffer.Handle = 0;
				mainWindowRenderBuffers.FrameRenderBuffers[i].IndexBufferSize = 0;
				mainWindowRenderBuffers.FrameRenderBuffers[i].IndexBufferMemory.Handle = 0;
				mainWindowRenderBuffers.FrameRenderBuffers[i].VertexBuffer.Handle = 0;
				mainWindowRenderBuffers.FrameRenderBuffers[i].VertexBufferSize = 0;
				mainWindowRenderBuffers.FrameRenderBuffers[i].VertexBufferMemory.Handle = 0;
			}
		}
		mainWindowRenderBuffers.Index = (mainWindowRenderBuffers.Index + 1) % mainWindowRenderBuffers.Count;

		ref FrameRenderBuffer frameRenderBuffer = ref mainWindowRenderBuffers.FrameRenderBuffers[mainWindowRenderBuffers.Index];

		if ( drawData.TotalVtxCount > 0 )
		{
			// Create or resize the vertex/index buffers
			ulong vertex_size = (ulong)drawData.TotalVtxCount * (ulong)sizeof( ImDrawVert );
			ulong index_size = (ulong)drawData.TotalIdxCount * (ulong)sizeof( ushort );
			if ( frameRenderBuffer.VertexBuffer.Handle == default || frameRenderBuffer.VertexBufferSize < vertex_size )
			{
				CreateOrResizeBuffer( ref frameRenderBuffer.VertexBuffer, ref frameRenderBuffer.VertexBufferMemory, ref frameRenderBuffer.VertexBufferSize, vertex_size, BufferUsageFlags.VertexBufferBit );
			}
			if ( frameRenderBuffer.IndexBuffer.Handle == default || frameRenderBuffer.IndexBufferSize < index_size )
			{
				CreateOrResizeBuffer( ref frameRenderBuffer.IndexBuffer, ref frameRenderBuffer.IndexBufferMemory, ref frameRenderBuffer.IndexBufferSize, index_size, BufferUsageFlags.IndexBufferBit );
			}

			// Upload vertex/index data into a single contiguous GPU buffer
			ImDrawVert* vtx_dst = null;
			ushort* idx_dst = null;
			if ( Apis.Vk.MapMemory( logicalDevice, frameRenderBuffer.VertexBufferMemory, 0, frameRenderBuffer.VertexBufferSize, 0, (void**)(&vtx_dst) ) != Result.Success )
			{
				throw new Exception( $"Unable to map device memory" );
			}
			if ( Apis.Vk.MapMemory( logicalDevice, frameRenderBuffer.IndexBufferMemory, 0, frameRenderBuffer.IndexBufferSize, 0, (void**)(&idx_dst) ) != Result.Success )
			{
				throw new Exception( $"Unable to map device memory" );
			}
			for ( int n = 0; n < drawData.CmdListsCount; n++ )
			{
				ImDrawList* cmd_list = drawDataPtr.CmdLists[n];
				Unsafe.CopyBlock( vtx_dst, cmd_list->VtxBuffer.Data.ToPointer(), (uint)cmd_list->VtxBuffer.Size * (uint)sizeof( ImDrawVert ) );
				Unsafe.CopyBlock( idx_dst, cmd_list->IdxBuffer.Data.ToPointer(), (uint)cmd_list->IdxBuffer.Size * (uint)sizeof( ushort ) );
				vtx_dst += cmd_list->VtxBuffer.Size;
				idx_dst += cmd_list->IdxBuffer.Size;
			}

			Span<MappedMemoryRange> range = stackalloc MappedMemoryRange[2];
			range[0].SType = StructureType.MappedMemoryRange;
			range[0].Memory = frameRenderBuffer.VertexBufferMemory;
			range[0].Size = Vk.WholeSize;
			range[1].SType = StructureType.MappedMemoryRange;
			range[1].Memory = frameRenderBuffer.IndexBufferMemory;
			range[1].Size = Vk.WholeSize;
			if ( Apis.Vk.FlushMappedMemoryRanges( logicalDevice, 2, range ) != Result.Success )
			{
				throw new Exception( $"Unable to flush memory to device" );
			}
			Apis.Vk.UnmapMemory( logicalDevice, frameRenderBuffer.VertexBufferMemory );
			Apis.Vk.UnmapMemory( logicalDevice, frameRenderBuffer.IndexBufferMemory );
		}

		// Setup desired Vulkan state
		Apis.Vk.CmdBindPipeline( commandBuffer, PipelineBindPoint.Graphics, pipeline );
		Apis.Vk.CmdBindDescriptorSets( commandBuffer, PipelineBindPoint.Graphics, pipelineLayout, 0, 1, descriptorSet, 0, null );

		// Bind Vertex And Index Buffer:
		if ( drawData.TotalVtxCount > 0 )
		{
			ReadOnlySpan<Buffer> vertex_buffers = stackalloc Buffer[] { frameRenderBuffer.VertexBuffer };
			ulong vertex_offset = 0;
			Apis.Vk.CmdBindVertexBuffers( commandBuffer, 0, 1, vertex_buffers, (ulong*)Unsafe.AsPointer( ref vertex_offset ) );
			Apis.Vk.CmdBindIndexBuffer( commandBuffer, frameRenderBuffer.IndexBuffer, 0, sizeof( ushort ) == 2 ? IndexType.Uint16 : IndexType.Uint32 );
		}

		// Setup viewport:
		Viewport viewport;
		viewport.X = 0;
		viewport.Y = 0;
		viewport.Width = (float)fb_width;
		viewport.Height = (float)fb_height;
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
			ImDrawList* cmd_list = drawDataPtr.CmdLists[n];
			for ( int cmd_i = 0; cmd_i < cmd_list->CmdBuffer.Size; cmd_i++ )
			{
				ref ImDrawCmd pcmd = ref cmd_list->CmdBuffer.Ref<ImDrawCmd>( cmd_i );

				// Project scissor/clipping rectangles into framebuffer space
				Vector4 clipRect;
				clipRect.X = (pcmd.ClipRect.X - clipOff.X) * clipScale.X;
				clipRect.Y = (pcmd.ClipRect.Y - clipOff.Y) * clipScale.Y;
				clipRect.Z = (pcmd.ClipRect.Z - clipOff.X) * clipScale.X;
				clipRect.W = (pcmd.ClipRect.W - clipOff.Y) * clipScale.Y;

				if ( clipRect.X < fb_width && clipRect.Y < fb_height && clipRect.Z >= 0.0f && clipRect.W >= 0.0f )
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
			indexOffset += cmd_list->IdxBuffer.Size;
			vertexOffset += cmd_list->VtxBuffer.Size;
		}

		Apis.Vk.CmdEndRenderPass( commandBuffer );
	}

	private unsafe void CreateOrResizeBuffer( ref Buffer buffer, ref DeviceMemory buffer_memory, ref ulong bufferSize, ulong newSize, BufferUsageFlags usage )
	{
		var logicalDevice = engine.LogicalDevice;

		if ( buffer.Handle != default )
		{
			Apis.Vk.DestroyBuffer( logicalDevice, buffer, default );
		}
		if ( buffer_memory.Handle != default )
		{
			Apis.Vk.FreeMemory( logicalDevice, buffer_memory, default );
		}

		ulong sizeAlignedVertexBuffer = ((newSize - 1) / bufferMemoryAlignment + 1) * bufferMemoryAlignment;
		var bufferInfo = new BufferCreateInfo
		{
			SType = StructureType.BufferCreateInfo,
			Size = sizeAlignedVertexBuffer,
			Usage = usage,
			SharingMode = SharingMode.Exclusive
		};
		if ( Apis.Vk.CreateBuffer( logicalDevice, bufferInfo, default, out buffer ) != Result.Success )
		{
			throw new Exception( $"Unable to create a device buffer" );
		}

		Apis.Vk.GetBufferMemoryRequirements( logicalDevice, buffer, out var req );
		bufferMemoryAlignment = (bufferMemoryAlignment > req.Alignment) ? bufferMemoryAlignment : req.Alignment;
		var allocInfo = new MemoryAllocateInfo
		{
			SType = StructureType.MemoryAllocateInfo,
			AllocationSize = req.Size,
			MemoryTypeIndex = GetMemoryTypeIndex( MemoryPropertyFlags.HostVisibleBit, req.MemoryTypeBits )
		};
		if ( Apis.Vk.AllocateMemory( logicalDevice, &allocInfo, default, out buffer_memory ) != Result.Success )
		{
			throw new Exception( $"Unable to allocate device memory" );
		}

		if ( Apis.Vk.BindBufferMemory( logicalDevice, buffer, buffer_memory, 0 ) != Result.Success )
		{
			throw new Exception( $"Unable to bind device memory" );
		}
		bufferSize = req.Size;
	}

	/// <summary>
	/// Frees all graphics resources used by the renderer.
	/// </summary>
	public unsafe void Dispose()
	{
		var logicalDevice = engine.LogicalDevice;

		keyboard.KeyChar -= OnKeyChar;

		for ( uint n = 0; n < mainWindowRenderBuffers.Count; n++ )
		{
			Apis.Vk.DestroyBuffer( logicalDevice, mainWindowRenderBuffers.FrameRenderBuffers[n].VertexBuffer, default );
			Apis.Vk.FreeMemory( logicalDevice, mainWindowRenderBuffers.FrameRenderBuffers[n].VertexBufferMemory, default );
			Apis.Vk.DestroyBuffer( logicalDevice, mainWindowRenderBuffers.FrameRenderBuffers[n].IndexBuffer, default );
			Apis.Vk.FreeMemory( logicalDevice, mainWindowRenderBuffers.FrameRenderBuffers[n].IndexBufferMemory, default );
		}

		Apis.Vk.DestroyShaderModule( logicalDevice, shaderModuleVert, default );
		Apis.Vk.DestroyShaderModule( logicalDevice, shaderModuleFrag, default );
		Apis.Vk.DestroyImageView( logicalDevice, fontView, default );
		Apis.Vk.DestroyImage( logicalDevice, fontImage, default );
		Apis.Vk.FreeMemory( logicalDevice, fontMemory, default );
		Apis.Vk.DestroySampler( logicalDevice, fontSampler, default );
		Apis.Vk.DestroyDescriptorSetLayout( logicalDevice, descriptorSetLayout, default );
		Apis.Vk.DestroyPipelineLayout( logicalDevice, pipelineLayout, default );
		Apis.Vk.DestroyPipeline( logicalDevice, pipeline, default );
		Apis.Vk.DestroyDescriptorPool( logicalDevice, descriptorPool, default );
		Apis.Vk.DestroyRenderPass( logicalDevice, renderPass, default );

		ImGuiNET.ImGui.DestroyContext();
		GC.SuppressFinalize( this );
	}

	private struct FrameRenderBuffer
	{
		public DeviceMemory VertexBufferMemory;
		public DeviceMemory IndexBufferMemory;
		public ulong VertexBufferSize;
		public ulong IndexBufferSize;
		public Buffer VertexBuffer;
		public Buffer IndexBuffer;
	};

	private unsafe struct WindowRenderBuffers
	{
		public uint Index;
		public uint Count;
		public FrameRenderBuffer* FrameRenderBuffers;
	};
}

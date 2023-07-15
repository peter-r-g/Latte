using Latte.Windowing.Extensions;
using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanRenderPass : VulkanWrapper
{
	internal required RenderPass RenderPass { get; init; }

	[SetsRequiredMembers]
	internal VulkanRenderPass( in RenderPass renderPass, LogicalGpu owner ) : base( owner )
	{
		RenderPass = renderPass;
	}

	public unsafe override void Dispose()
	{
		if ( Disposed )
			return;

		Apis.Vk.DestroyRenderPass( LogicalGpu!, RenderPass, null );

		GC.SuppressFinalize( this );
		Disposed = true;
	}

	private static Format FindSupportedFormat( Gpu gpu, IEnumerable<Format> candidates, ImageTiling tiling, FormatFeatureFlags features )
	{
		foreach ( var format in candidates )
		{
			var properties = gpu.GetFormatProperties( format );

			if ( tiling == ImageTiling.Linear && (properties.LinearTilingFeatures & features) == features )
				return format;
			else if ( tiling == ImageTiling.Optimal && (properties.OptimalTilingFeatures & features) == features )
				return format;
		}

		throw new ApplicationException( "Failed to find a suitable format" );
	}

	private static Format FindDepthFormat( Gpu gpu )
	{
		var formats = new Format[]
		{
			Format.D32Sfloat,
			Format.D32SfloatS8Uint,
			Format.D24UnormS8Uint
		};

		return FindSupportedFormat( gpu, formats, ImageTiling.Optimal, FormatFeatureFlags.DepthStencilAttachmentBit );
	}

	public static implicit operator RenderPass( VulkanRenderPass vulkanRenderPass )
	{
		if ( vulkanRenderPass.Disposed )
			throw new ObjectDisposedException( nameof( VulkanRenderPass ) );

		return vulkanRenderPass.RenderPass;
	}

	internal static unsafe VulkanRenderPass New( LogicalGpu logicalGpu, Format swapchainImageFormat, SampleCountFlags msaaSamples )
	{
		var useMsaa = msaaSamples != SampleCountFlags.Count1Bit;
		var colorAttachment = new AttachmentDescription
		{
			Format = swapchainImageFormat,
			Samples = msaaSamples,
			LoadOp = AttachmentLoadOp.Clear,
			StoreOp = AttachmentStoreOp.Store,
			InitialLayout = ImageLayout.Undefined,
			FinalLayout = useMsaa
				? ImageLayout.ColorAttachmentOptimal
				: ImageLayout.PresentSrcKhr
		};

		var colorAttachmentRef = new AttachmentReference
		{
			Attachment = 0,
			Layout = ImageLayout.AttachmentOptimal
		};

		var depthAttachment = new AttachmentDescription
		{
			Format = FindDepthFormat( logicalGpu.Gpu! ),
			Samples = msaaSamples,
			LoadOp = AttachmentLoadOp.Clear,
			StoreOp = AttachmentStoreOp.DontCare,
			StencilLoadOp = AttachmentLoadOp.DontCare,
			StencilStoreOp = AttachmentStoreOp.DontCare,
			InitialLayout = ImageLayout.Undefined,
			FinalLayout = ImageLayout.DepthStencilAttachmentOptimal
		};

		var depthAttachmentRef = new AttachmentReference
		{
			Attachment = 1,
			Layout = ImageLayout.DepthStencilAttachmentOptimal
		};

		var colorAttachmentResolve = new AttachmentDescription
		{
			Format = swapchainImageFormat,
			Samples = SampleCountFlags.Count1Bit,
			LoadOp = AttachmentLoadOp.DontCare,
			StoreOp = AttachmentStoreOp.DontCare,
			StencilLoadOp = AttachmentLoadOp.DontCare,
			StencilStoreOp = AttachmentStoreOp.DontCare,
			InitialLayout = ImageLayout.Undefined,
			FinalLayout = ImageLayout.PresentSrcKhr
		};

		var colorAttachmentResolveRef = new AttachmentReference
		{
			Attachment = 2,
			Layout = ImageLayout.ColorAttachmentOptimal
		};

		var subpassDescription = new SubpassDescription
		{
			PipelineBindPoint = PipelineBindPoint.Graphics,
			ColorAttachmentCount = 1,
			PColorAttachments = &colorAttachmentRef,
			PDepthStencilAttachment = &depthAttachmentRef,
			PResolveAttachments = useMsaa
				? &colorAttachmentResolveRef
				: null
		};

		var subpassDependency = new SubpassDependency
		{
			SrcSubpass = Vk.SubpassExternal,
			DstSubpass = 0,
			SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
			SrcAccessMask = 0,
			DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
			DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit
		};

		var attachments = stackalloc AttachmentDescription[useMsaa ? 3 : 2];
		attachments[0] = colorAttachment;
		attachments[1] = depthAttachment;
		if ( useMsaa )
			attachments[2] = colorAttachmentResolve;

		var renderPassInfo = new RenderPassCreateInfo
		{
			SType = StructureType.RenderPassCreateInfo,
			AttachmentCount = (uint)(useMsaa ? 3 : 2),
			PAttachments = attachments,
			SubpassCount = 1,
			PSubpasses = &subpassDescription,
			DependencyCount = 1,
			PDependencies = &subpassDependency
		};

		Apis.Vk.CreateRenderPass( logicalGpu, renderPassInfo, null, out var renderPass ).Verify();

		return new VulkanRenderPass( renderPass, logicalGpu );
	}
}

using Silk.NET.Vulkan;
using System;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanImage : IDisposable
{
	internal LogicalGpu Owner { get; }

	internal Image Image { get; set; }
	internal DeviceMemory Memory { get; set; }
	internal ImageView View { get; set; }

	internal VulkanImage( in Image image, in DeviceMemory memory, in ImageView view, LogicalGpu owner )
	{
		Image = image;
		Memory = memory;
		View = view;
		Owner = owner;
	}

	public unsafe void Dispose()
	{
		Apis.Vk.DestroyImageView( Owner, View, null );
		Apis.Vk.DestroyImage( Owner, Image, null );
		Apis.Vk.FreeMemory( Owner, Memory, null );
	}

	internal unsafe void TransitionImageLayout( in CommandBuffer commandBuffer, Format format,
		ImageLayout oldLayout, ImageLayout newLayout, uint mipLevels )
	{
		var barrier = new ImageMemoryBarrier()
		{
			SType = StructureType.ImageMemoryBarrier,
			OldLayout = oldLayout,
			NewLayout = newLayout,
			SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
			DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
			Image = Image,
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

		Apis.Vk.CmdPipelineBarrier( commandBuffer, sourceStage, destinationStage, 0,
			0, null,
			0, null,
			1, barrier );
	}

	public static implicit operator Image( VulkanImage vulkanImage ) => vulkanImage.Image;
}

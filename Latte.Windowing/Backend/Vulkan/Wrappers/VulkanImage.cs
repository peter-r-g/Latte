using Silk.NET.Vulkan;
using System;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanImage : IDisposable
{
	internal LogicalGpu Owner { get; }

	internal Image Image { get; set; }
	internal DeviceMemory Memory { get; set; }
	internal ImageView View { get; set; }

	private bool disposed;

	internal VulkanImage( in Image image, in DeviceMemory memory, in ImageView view, LogicalGpu owner )
	{
		Image = image;
		Memory = memory;
		View = view;
		Owner = owner;
	}

	~VulkanImage()
	{
		Dispose();
	}

	public unsafe void Dispose()
	{
		if ( disposed )
			return;

		disposed = true;
		Apis.Vk.DestroyImageView( Owner, View, null );
		Apis.Vk.DestroyImage( Owner, Image, null );
		Apis.Vk.FreeMemory( Owner, Memory, null );

		GC.SuppressFinalize( this );
	}

	internal void CopyBufferToImage( in CommandBuffer commandBuffer, VulkanBuffer buffer, uint width, uint height )
	{
		var region = new BufferImageCopy
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

		Apis.Vk.CmdCopyBufferToImage( commandBuffer, buffer, Image, ImageLayout.TransferDstOptimal, 1, region );
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

	internal unsafe void GenerateMipMaps( in CommandBuffer commandBuffer, Format format, uint width, uint height, uint mipLevels )
	{
		var formatProperties = Owner.Gpu.GetFormatProperties( format );
		if ( !formatProperties.OptimalTilingFeatures.HasFlag( FormatFeatureFlags.SampledImageFilterLinearBit ) )
			throw new ApplicationException( "Texture image format does not support linear blitting" );

		var barrier = new ImageMemoryBarrier
		{
			SType = StructureType.ImageMemoryBarrier,
			Image = Image,
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

			Apis.Vk.CmdPipelineBarrier( commandBuffer,
				PipelineStageFlags.TransferBit, PipelineStageFlags.TransferBit, 0,
				0, null,
				0, null,
				1, barrier );

			var blit = new ImageBlit
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

			Apis.Vk.CmdBlitImage( commandBuffer,
				Image, ImageLayout.TransferSrcOptimal,
				Image, ImageLayout.TransferDstOptimal,
				1, blit, Filter.Linear );

			barrier.OldLayout = ImageLayout.TransferSrcOptimal;
			barrier.NewLayout = ImageLayout.ShaderReadOnlyOptimal;
			barrier.SrcAccessMask = AccessFlags.TransferReadBit;
			barrier.DstAccessMask = AccessFlags.ShaderReadBit;

			Apis.Vk.CmdPipelineBarrier( commandBuffer,
				PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit, 0,
				0, null,
				0, null,
				1, barrier );

			if ( mipWidth > 1 ) mipWidth /= 2;
			if ( mipHeight > 1 ) mipHeight /= 2;
		}

		barrier.SubresourceRange.BaseMipLevel = mipLevels - 1;
		barrier.OldLayout = ImageLayout.TransferDstOptimal;
		barrier.NewLayout = ImageLayout.ShaderReadOnlyOptimal;
		barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
		barrier.DstAccessMask = AccessFlags.ShaderReadBit;

		Apis.Vk.CmdPipelineBarrier( commandBuffer,
			PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit, 0,
			0, null,
			0, null,
			1, barrier );
	}

	private static bool HasStencilComponent( Format format )
	{
		return format == Format.D32Sfloat || format == Format.D24UnormS8Uint;
	}

	public static implicit operator Image( VulkanImage vulkanImage ) => vulkanImage.Image;
}

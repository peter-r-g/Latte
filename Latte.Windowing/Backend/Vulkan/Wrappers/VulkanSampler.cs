using Latte.Windowing.Extensions;
using Silk.NET.Vulkan;
using System;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanSampler : VulkanWrapper
{
	internal Sampler Sampler { get; }

	internal VulkanSampler( in Sampler sampler, LogicalGpu owner ) : base( owner )
	{
		Sampler = sampler;
	}

	public override unsafe void Dispose()
	{
		if ( Disposed )
			return;

		Apis.Vk.DestroySampler( LogicalGpu!, Sampler, null );

		GC.SuppressFinalize( this );
		Disposed = true;
	}

	public static implicit operator Sampler( VulkanSampler vulkanSampler )
	{
		if ( vulkanSampler.Disposed )
			throw new ObjectDisposedException( nameof( VulkanSampler ) );

		return vulkanSampler.Sampler;
	}

	internal static unsafe VulkanSampler New( LogicalGpu logicalGpu, bool enableMsaa, uint mipLevels )
	{
		var samplerInfo = new SamplerCreateInfo()
		{
			SType = StructureType.SamplerCreateInfo,
			MagFilter = Filter.Linear,
			MinFilter = Filter.Linear,
			AddressModeU = SamplerAddressMode.Repeat,
			AddressModeV = SamplerAddressMode.Repeat,
			AddressModeW = SamplerAddressMode.Repeat,
			AnisotropyEnable = enableMsaa ? Vk.True : Vk.False,
			MaxAnisotropy = logicalGpu.Gpu!.Properties.Limits.MaxSamplerAnisotropy,
			BorderColor = BorderColor.IntOpaqueBlack,
			UnnormalizedCoordinates = Vk.False,
			CompareEnable = Vk.False,
			CompareOp = CompareOp.Always,
			MipmapMode = SamplerMipmapMode.Linear,
			MipLodBias = 0,
			MinLod = 0,
			MaxLod = mipLevels
		};

		Apis.Vk.CreateSampler( logicalGpu, samplerInfo, null, out var sampler ).Verify();

		return new VulkanSampler( sampler, logicalGpu );
	}
}

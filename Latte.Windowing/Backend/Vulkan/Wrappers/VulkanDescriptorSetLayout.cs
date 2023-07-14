using Silk.NET.Vulkan;
using System;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanDescriptorSetLayout : VulkanWrapper
{
	internal DescriptorSetLayout DescriptorSetLayout { get; }

	internal VulkanDescriptorSetLayout( in DescriptorSetLayout descriptorSetLayout, LogicalGpu owner ) : base( owner )
	{
		DescriptorSetLayout = descriptorSetLayout;
	}

	public override unsafe void Dispose()
	{
		if ( Disposed )
			return;

		Apis.Vk.DestroyDescriptorSetLayout( LogicalGpu!, DescriptorSetLayout, null );

		GC.SuppressFinalize( this );
		Disposed = true;
	}

	public static implicit operator DescriptorSetLayout( VulkanDescriptorSetLayout vulkanDescriptorSetLayout )
	{
		if ( vulkanDescriptorSetLayout.Disposed )
			throw new ObjectDisposedException( nameof( VulkanDescriptorSetLayout ) );

		return vulkanDescriptorSetLayout.DescriptorSetLayout;
	}
}

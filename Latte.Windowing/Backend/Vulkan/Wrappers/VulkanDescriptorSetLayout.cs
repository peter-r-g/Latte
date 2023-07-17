using Latte.Windowing.Extensions;
using Silk.NET.Vulkan;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanDescriptorSetLayout : VulkanWrapper
{
	internal required DescriptorSetLayout DescriptorSetLayout { get; init; }

	[SetsRequiredMembers]
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

	internal static unsafe VulkanDescriptorSetLayout New( LogicalGpu logicalGpu, in ReadOnlySpan<DescriptorSetLayoutBinding> bindings )
	{
		fixed ( DescriptorSetLayoutBinding* bindingsPtr = bindings )
		{
			var layoutInfo = new DescriptorSetLayoutCreateInfo()
			{
				SType = StructureType.DescriptorSetLayoutCreateInfo,
				BindingCount = (uint)bindings.Length,
				PBindings = bindingsPtr
			};

			Apis.Vk.CreateDescriptorSetLayout( logicalGpu, layoutInfo, null, out var descriptorSetLayout ).Verify();

			return new VulkanDescriptorSetLayout( descriptorSetLayout, logicalGpu );
		}
	}
}

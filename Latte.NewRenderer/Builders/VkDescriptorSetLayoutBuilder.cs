using Latte.NewRenderer.Exceptions;
using Latte.NewRenderer.Extensions;
using Silk.NET.Vulkan;
using System.Collections.Generic;

namespace Latte.NewRenderer.Builders;

internal sealed class VkDescriptorSetLayoutBuilder
{
	private readonly Device logicalDevice;
	private readonly List<DescriptorSetLayoutBinding> bindings = [];

	internal VkDescriptorSetLayoutBuilder( Device logicalDevice )
	{
		VkInvalidHandleException.ThrowIfInvalid( logicalDevice );

		this.logicalDevice = logicalDevice;
	}

	internal VkDescriptorSetLayoutBuilder AddBinding( uint binding, DescriptorType type, ShaderStageFlags shaderStageFlags )
	{
		bindings.Add( VkInfo.DescriptorSetLayoutBinding( type, shaderStageFlags, binding ) );
		return this;
	}

	internal VkDescriptorSetLayoutBuilder Clear()
	{
		bindings.Clear();
		return this;
	}

	internal unsafe DescriptorSetLayout Build()
	{
		// FIXME: Don't create a new array for this.
		var layoutInfo = VkInfo.DescriptorSetLayout( bindings.ToArray() );

		Apis.Vk.CreateDescriptorSetLayout( logicalDevice, layoutInfo, null, out var layout ).Verify();
		return layout;
	}
}

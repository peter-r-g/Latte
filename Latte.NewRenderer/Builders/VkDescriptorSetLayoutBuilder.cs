using Latte.NewRenderer.Exceptions;
using Latte.NewRenderer.Extensions;
using Silk.NET.Vulkan;
using System;

namespace Latte.NewRenderer.Builders;

internal sealed class VkDescriptorSetLayoutBuilder
{
	private readonly Device logicalDevice;
	private readonly DescriptorSetLayoutBinding[] bindings;

	private int currentBindings;

	internal VkDescriptorSetLayoutBuilder( Device logicalDevice, int maxBindings )
	{
		VkInvalidHandleException.ThrowIfInvalid( logicalDevice );

		this.logicalDevice = logicalDevice;
		bindings = new DescriptorSetLayoutBinding[maxBindings];
	}

	internal VkDescriptorSetLayoutBuilder AddBinding( uint binding, DescriptorType type, ShaderStageFlags shaderStageFlags )
	{
		if ( currentBindings >= bindings.Length )
			throw new InvalidOperationException( "The maximum amount of descriptor set layout bindings has been exceeded" );

		bindings[currentBindings++] = VkInfo.DescriptorSetLayoutBinding( type, shaderStageFlags, binding );
		return this;
	}

	internal VkDescriptorSetLayoutBuilder Clear()
	{
		currentBindings = 0;
		return this;
	}

	internal unsafe DescriptorSetLayout Build()
	{
		var bindingsSpan = bindings.AsSpan()[..currentBindings];
		var layoutInfo = VkInfo.DescriptorSetLayout( bindingsSpan );

		Apis.Vk.CreateDescriptorSetLayout( logicalDevice, layoutInfo, null, out var layout ).Verify();
		return layout;
	}
}

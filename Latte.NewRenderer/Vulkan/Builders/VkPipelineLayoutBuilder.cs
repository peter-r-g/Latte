using Latte.NewRenderer.Vulkan.Exceptions;
using Latte.NewRenderer.Vulkan.Extensions;
using Silk.NET.Vulkan;
using System;

namespace Latte.NewRenderer.Vulkan.Builders;

internal sealed class VkPipelineLayoutBuilder
{
	private readonly Device logicalDevice;
	private readonly PushConstantRange[] pushConstantRanges;
	private readonly DescriptorSetLayout[] descriptorSetLayouts;

	private int currentPushConstantRanges;
	private int currentDescriptorSetLayouts;

	internal VkPipelineLayoutBuilder( Device logicalDevice, int maxPushConstantRanges, int maxDescriptorSetLayouts )
	{
		VkInvalidHandleException.ThrowIfInvalid( logicalDevice );

		this.logicalDevice = logicalDevice;
		pushConstantRanges = new PushConstantRange[maxPushConstantRanges];
		descriptorSetLayouts = new DescriptorSetLayout[maxDescriptorSetLayouts];
	}

	internal VkPipelineLayoutBuilder AddPushConstantRange( PushConstantRange pushConstantRange )
	{
		if ( currentPushConstantRanges >= pushConstantRanges.Length )
			throw new InvalidOperationException( "The maximum amount of push constant ranges has been exceeded" );

		pushConstantRanges[currentPushConstantRanges++] = pushConstantRange;
		return this;
	}

	internal VkPipelineLayoutBuilder AddDescriptorSetLayout( DescriptorSetLayout descriptorSetLayout )
	{
		if ( currentDescriptorSetLayouts >= descriptorSetLayouts.Length )
			throw new InvalidOperationException( "The maximum amount of descriptor set layouts has been exceeded" );

		descriptorSetLayouts[currentDescriptorSetLayouts++] = descriptorSetLayout;
		return this;
	}

	internal VkPipelineLayoutBuilder ClearPushConstantRanges()
	{
		currentPushConstantRanges = 0;
		return this;
	}

	internal VkPipelineLayoutBuilder ClearDescriptorSetLayouts()
	{
		currentDescriptorSetLayouts = 0;
		return this;
	}

	internal VkPipelineLayoutBuilder Clear()
	{
		ClearPushConstantRanges();
		ClearDescriptorSetLayouts();
		return this;
	}

	internal unsafe PipelineLayout Build()
	{
		var pushConstantRangesSpan = pushConstantRanges.AsSpan()[..currentPushConstantRanges];
		var descriptorSetLayoutsSpan = descriptorSetLayouts.AsSpan()[..currentDescriptorSetLayouts];
		var layoutInfo = VkInfo.PipelineLayout( pushConstantRangesSpan, descriptorSetLayoutsSpan );

		Apis.Vk.CreatePipelineLayout( logicalDevice, layoutInfo, null, out var pipelineLayout ).AssertSuccess();
		return pipelineLayout;
	}
}

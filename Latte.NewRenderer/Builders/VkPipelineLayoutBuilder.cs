using Latte.NewRenderer.Extensions;
using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;

namespace Latte.NewRenderer.Builders;

internal sealed class VkPipelineLayoutBuilder
{
	private readonly Device logicalDevice;
	private readonly List<PushConstantRange> pushConstantRanges = [];
	private readonly List<DescriptorSetLayout> descriptorSetLayouts = [];

	internal VkPipelineLayoutBuilder( Device logicalDevice )
	{
		this.logicalDevice = logicalDevice;
	}

	internal VkPipelineLayoutBuilder AddPushConstantRange( PushConstantRange pushConstantRange )
	{
		pushConstantRanges.Add( pushConstantRange );
		return this;
	}

	internal VkPipelineLayoutBuilder AddDescriptorSetLayout( DescriptorSetLayout descriptorSetLayout )
	{
		descriptorSetLayouts.Add( descriptorSetLayout );
		return this;
	}

	internal VkPipelineLayoutBuilder ClearPushConstantRanges()
	{
		pushConstantRanges.Clear();
		return this;
	}

	internal VkPipelineLayoutBuilder ClearDescriptorSetLayouts()
	{
		descriptorSetLayouts.Clear();
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
		// FIXME: Don't create new arrays for these.
		var pushConstantRanges = this.pushConstantRanges.ToArray().AsSpan();
		var descriptorSetLayouts = this.descriptorSetLayouts.ToArray().AsSpan();
		var layoutInfo = VkInfo.PipelineLayout( pushConstantRanges, descriptorSetLayouts );

		Apis.Vk.CreatePipelineLayout( logicalDevice, layoutInfo, null, out var pipelineLayout ).Verify();
		return pipelineLayout;
	}
}

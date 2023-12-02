﻿using Latte.NewRenderer.Extensions;
using Silk.NET.Vulkan;
using System;

namespace Latte.NewRenderer.Builders;

internal sealed class VkPipelineBuilder
{
	private readonly Device logicalDevice;
	private readonly RenderPass renderPass;

	private PipelineLayout pipelineLayout;
	private Viewport viewport;
	private Rect2D scissor;
	private PipelineShaderStageCreateInfo[] shaderStages = [];
	private PipelineVertexInputStateCreateInfo vertexInputInfo;
	private PipelineInputAssemblyStateCreateInfo inputAssemblyInfo;
	private PipelineRasterizationStateCreateInfo rasterizerInfo;
	private PipelineColorBlendAttachmentState colorBlendAttachment;
	private PipelineMultisampleStateCreateInfo multisamplingInfo;

	internal VkPipelineBuilder( Device logicalDevice, RenderPass renderPass )
	{
		this.logicalDevice = logicalDevice;
		this.renderPass = renderPass;
	}

	internal VkPipelineBuilder WithPipelineLayout( PipelineLayout pipelineLayout )
	{
		this.pipelineLayout = pipelineLayout;
		return this;
	}

	internal VkPipelineBuilder WithViewport( Viewport viewport )
	{
		this.viewport = viewport;
		return this;
	}

	internal VkPipelineBuilder WithScissor( Rect2D scissor )
	{
		this.scissor = scissor;
		return this;
	}

	internal VkPipelineBuilder AddShaderStage( PipelineShaderStageCreateInfo shaderStageCreateInfo )
	{
		var newShaderStages = new PipelineShaderStageCreateInfo[shaderStages.Length + 1];
		Array.Copy( shaderStages, newShaderStages, shaderStages.Length );
		newShaderStages[^1] = shaderStageCreateInfo;
		shaderStages = newShaderStages;

		return this;
	}

	internal VkPipelineBuilder ClearShaderStages()
	{
		shaderStages = [];
		return this;
	}

	internal VkPipelineBuilder WithVertexInputState( PipelineVertexInputStateCreateInfo vertexInputInfo )
	{
		this.vertexInputInfo = vertexInputInfo;
		return this;
	}

	internal VkPipelineBuilder WithInputAssemblyState( PipelineInputAssemblyStateCreateInfo inputAssemblyInfo )
	{
		this.inputAssemblyInfo = inputAssemblyInfo;
		return this;
	}

	internal VkPipelineBuilder WithRasterizerState( PipelineRasterizationStateCreateInfo rasterizerInfo )
	{
		this.rasterizerInfo = rasterizerInfo;
		return this;
	}

	internal VkPipelineBuilder WithColorBlendAttachmentState( PipelineColorBlendAttachmentState colorBlendAttachment )
	{
		this.colorBlendAttachment = colorBlendAttachment;
		return this;
	}

	internal VkPipelineBuilder WithMultisamplingState( PipelineMultisampleStateCreateInfo multisamplingInfo )
	{
		this.multisamplingInfo = multisamplingInfo;
		return this;
	}

	internal unsafe Pipeline Build()
	{
		var viewport = this.viewport;
		var scissor = this.scissor;
		var colorBlendAttachment = this.colorBlendAttachment;
		var vertexInputInfo = this.vertexInputInfo;
		var inputAssemblyInfo = this.inputAssemblyInfo;
		var rasterizerInfo = this.rasterizerInfo;
		var multisamplingInfo = this.multisamplingInfo;

		var viewportState = new PipelineViewportStateCreateInfo
		{
			SType = StructureType.PipelineViewportStateCreateInfo,
			PNext = null,
			ViewportCount = 1,
			PViewports = &viewport,
			ScissorCount = 1,
			PScissors = &scissor
		};

		var colorBlendingCreateInfo = new PipelineColorBlendStateCreateInfo
		{
			SType = StructureType.PipelineColorBlendStateCreateInfo,
			PNext = null,
			LogicOpEnable = Vk.False,
			LogicOp = LogicOp.Copy,
			AttachmentCount = 1,
			PAttachments = &colorBlendAttachment
		};

		fixed( PipelineShaderStageCreateInfo* shaderStagesPtr = shaderStages )
		{
			var pipelineCreateInfo = new GraphicsPipelineCreateInfo
			{
				SType = StructureType.GraphicsPipelineCreateInfo,
				PNext = null,
				StageCount = (uint)shaderStages.Length,
				PStages = shaderStagesPtr,
				PVertexInputState = &vertexInputInfo,
				PInputAssemblyState = &inputAssemblyInfo,
				PViewportState = &viewportState,
				PRasterizationState = &rasterizerInfo,
				PMultisampleState = &multisamplingInfo,
				PColorBlendState = &colorBlendingCreateInfo,
				Layout = pipelineLayout,
				RenderPass = renderPass,
				Subpass = 0,
				BasePipelineHandle = default
			};

			Apis.Vk.CreateGraphicsPipelines( logicalDevice, default, 1, pipelineCreateInfo, null, out var pipeline ).Verify();
			return pipeline;
		}
	}
}

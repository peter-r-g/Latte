﻿using Silk.NET.Vulkan;

namespace Latte.NewRenderer;

internal sealed class FrameData
{
	internal Semaphore PresentSemaphore;
	internal Semaphore RenderSemaphore;
	internal Fence RenderFence;

	internal CommandPool CommandPool;
	internal CommandBuffer CommandBuffer;
}

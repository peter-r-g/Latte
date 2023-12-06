using Silk.NET.Vulkan;

namespace Latte.NewRenderer;

internal struct UploadContext
{
	internal Fence UploadFence;
	internal CommandPool CommandPool;
	internal CommandBuffer CommandBuffer;
}

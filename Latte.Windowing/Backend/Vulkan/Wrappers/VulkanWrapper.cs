using System;

namespace Latte.Windowing.Backend.Vulkan;

internal abstract class VulkanWrapper : IDisposable
{
	internal VulkanInstance? Instance { get; }
	internal Gpu? Gpu { get; }
	internal LogicalGpu? LogicalGpu { get; }

	internal bool Disposed { get; set; }

	protected VulkanWrapper( LogicalGpu logicalGpu )
	{
		LogicalGpu = logicalGpu;
		Gpu = logicalGpu.Gpu;
		Instance = logicalGpu.Gpu!.Instance;
	}

	protected VulkanWrapper( Gpu gpu )
	{
		Gpu = gpu;
		Instance = gpu.Instance;
	}

	protected VulkanWrapper( VulkanInstance instance )
	{
		Instance = instance;
	}

	protected VulkanWrapper()
	{
	}

	~VulkanWrapper()
	{
		Dispose();
	}

	public abstract void Dispose();
}

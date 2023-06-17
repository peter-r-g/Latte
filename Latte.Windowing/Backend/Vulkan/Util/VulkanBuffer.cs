using Silk.NET.Vulkan;
using System;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanBuffer : IDisposable
{
	internal LogicalGpu Owner { get; }

	internal Buffer Buffer { get; }
	internal DeviceMemory Memory { get; }

	private bool disposed;

	internal VulkanBuffer( in Buffer buffer, in DeviceMemory memory, LogicalGpu owner )
	{
		Buffer = buffer;
		Memory = memory;
		Owner = owner;
	}

	~VulkanBuffer()
	{
		Dispose();
	}

	public unsafe void Dispose()
	{
		if ( disposed )
			return;

		disposed = true;
		Apis.Vk.DestroyBuffer( Owner, Buffer, null );
		Apis.Vk.FreeMemory( Owner, Memory, null );

		GC.SuppressFinalize( this );
	}

	public static implicit operator Buffer( VulkanBuffer vulkanBuffer ) => vulkanBuffer.Buffer;
}

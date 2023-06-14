using Silk.NET.Vulkan;
using System;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class GPUBuffer<T> where T : unmanaged
{
	internal Buffer Buffer { get; }
	internal ulong Offset { get; }
	internal BufferUsageFlags UsageFlags { get; }

	internal unsafe GPUBuffer( VulkanBackend backend, in ReadOnlySpan<T> data, BufferUsageFlags usage )
	{
		var bufferSize = (ulong)sizeof( T ) * (ulong)data.Length;
		var buffer = backend.GetGPUBuffer( bufferSize, usage, out var offset );
		Buffer = buffer;
		Offset = offset;
		UsageFlags = usage;

		backend.UploadToBuffer( buffer, data );
	}
}

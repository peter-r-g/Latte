using Silk.NET.Vulkan;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Latte.Windowing.Backend.Vulkan;

internal struct TemporaryCommandBuffer : IDisposable
{
	internal required LogicalGpu Owner { get; init; }
	internal required CommandBuffer CommandBuffer { get; init; }

	internal bool Disposed { get; set; }

	[SetsRequiredMembers]
	internal TemporaryCommandBuffer( LogicalGpu owner, in CommandBuffer commandBuffer )
	{
		Owner = owner;
		CommandBuffer = commandBuffer;
	}

	public void Dispose()
	{
		if ( Disposed )
			return;

		Owner.EndOneTimeCommands( ref this );
		Disposed = true;
	}

	public static implicit operator CommandBuffer( in TemporaryCommandBuffer temporaryCommandBuffer )
	{
		if ( temporaryCommandBuffer.Disposed )
			throw new ObjectDisposedException( nameof( TemporaryCommandBuffer ) );

		return temporaryCommandBuffer.CommandBuffer;
	}
}

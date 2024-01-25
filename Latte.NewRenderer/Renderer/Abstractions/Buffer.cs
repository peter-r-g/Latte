using System;

namespace Latte.Windowing.Renderer.Abstractions;

internal abstract class Buffer : IDisposable
{
	internal abstract BufferLayout Layout { get; init; }

	internal unsafe abstract void SetData( void* dataPtr, ulong size );

	protected virtual void Dispose( bool disposing )
	{
	}

	public void Dispose()
	{
		Dispose( disposing: true );
		GC.SuppressFinalize( this );
	}
}

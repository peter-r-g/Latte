using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Latte.NewRenderer.Renderer.Extensions;

internal static class BufferExtensions
{
	internal static unsafe void SetData<T>( this Buffer buffer, T data ) where T : unmanaged
	{
		var size = Unsafe.SizeOf<T>();
		var ptr = Marshal.AllocHGlobal( size );
		Unsafe.Copy( (void*)ptr, in data );

		buffer.SetData( (void*)ptr, (ulong)size );

		Marshal.FreeHGlobal( ptr );
	}

	internal static unsafe void SetData<T>( this Buffer buffer, ReadOnlySpan<T> data ) where T : unmanaged
	{
		var size = Unsafe.SizeOf<T>();
		var ptr = Marshal.AllocHGlobal( size );
		for ( var i = 0; i < data.Length; i++ )
			Unsafe.Copy( (void*)(ptr + (size * i)), in data[i] );

		buffer.SetData( (void*)ptr, (ulong)size );

		Marshal.FreeHGlobal( ptr );
	}
}

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Latte.NewRenderer.Renderer.Abstractions;

namespace Latte.NewRenderer.Renderer.Abstractions.Extensions;

internal static class BufferExtensions
{
	private const int MaxStackAllocBytes = 1024;

	internal static unsafe void SetData<T>( this Buffer buffer, T data ) where T : unmanaged
	{
		var size = Unsafe.SizeOf<T>();

		if ( size <= MaxStackAllocBytes )
		{
			void* ptr = stackalloc byte[size];
			Unsafe.Copy( ptr, in data );

			buffer.SetData( ptr, (ulong)size );
		}
		else
		{
			var ptr = Marshal.AllocHGlobal( size );
			Unsafe.Copy( (void*)ptr, in data );

			buffer.SetData( (void*)ptr, (ulong)size );

			Marshal.FreeHGlobal( ptr );
		}
	}

	internal static unsafe void SetData<T>( this Buffer buffer, ReadOnlySpan<T> data ) where T : unmanaged
	{
		var size = Unsafe.SizeOf<T>();
		
		if ( size <= MaxStackAllocBytes )
		{
			byte* ptr = stackalloc byte[size];
			for ( var i = 0; i < data.Length; i++ )
				Unsafe.Copy( (ptr + size * i), in data[i] );

			buffer.SetData( ptr, (ulong)size );
		}
		else
		{
			var ptr = Marshal.AllocHGlobal( size );
			for ( var i = 0; i < data.Length; i++ )
				Unsafe.Copy( (void*)(ptr + size * i), in data[i] );

			buffer.SetData( (void*)ptr, (ulong)size );

			Marshal.FreeHGlobal( ptr );
		}
	}
}

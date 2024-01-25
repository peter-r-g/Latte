using Silk.NET.Vulkan;
using System;
using System.Runtime.InteropServices;

namespace Latte.NewRenderer.Renderer.Vulkan.Temp;

internal unsafe sealed class Shader : IDisposable
{
	private readonly Device logicalDevice;
	internal readonly ReadOnlyMemory<byte> Code;
	internal readonly string EntryPoint;
	internal readonly nint EntryPointPtr;

	internal ShaderModule Module;

	private bool disposed;

	internal Shader( Device logicalDevice, ReadOnlyMemory<byte> code, string entryPoint )
	{
		this.logicalDevice = logicalDevice;
		Code = code;
		EntryPoint = entryPoint;
		EntryPointPtr = Marshal.StringToHGlobalAnsi( entryPoint );
	}

	~Shader()
	{
		Dispose( disposing: false );
	}

	private void Dispose( bool disposing )
	{
		if ( disposed )
			return;

		if ( disposing )
		{
		}

		Apis.Vk.DestroyShaderModule( logicalDevice, Module, null );
		Marshal.FreeHGlobal( EntryPointPtr );
		disposed = true;
	}

	public void Dispose()
	{
		Dispose( disposing: true );
		GC.SuppressFinalize( this );
	}
}

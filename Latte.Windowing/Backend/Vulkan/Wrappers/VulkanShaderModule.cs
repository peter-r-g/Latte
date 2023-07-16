using Latte.Windowing.Extensions;
using Silk.NET.Vulkan;
using System;
namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanShaderModule : VulkanWrapper
{
	internal ShaderModule ShaderModule { get; }

	internal VulkanShaderModule( in ShaderModule shaderModule, LogicalGpu owner ) : base( owner )
	{
		ShaderModule = shaderModule;
	}

	public override unsafe void Dispose()
	{
		if ( Disposed )
			return;

		Apis.Vk.DestroyShaderModule( LogicalGpu!, ShaderModule, null );

		GC.SuppressFinalize( this );
		Disposed = true;
	}

	public static implicit operator ShaderModule( VulkanShaderModule vulkanShaderModule )
	{
		if ( vulkanShaderModule.Disposed )
			throw new ObjectDisposedException( nameof( VulkanShaderModule ) );

		return vulkanShaderModule.ShaderModule;
	}

	internal static unsafe VulkanShaderModule New( LogicalGpu logicalGpu, in ReadOnlySpan<byte> shaderCode )
	{
		var createInfo = new ShaderModuleCreateInfo
		{
			SType = StructureType.ShaderModuleCreateInfo,
			CodeSize = (nuint)shaderCode.Length
		};

		fixed ( byte* shaderCodePtr = shaderCode )
		{
			createInfo.PCode = (uint*)shaderCodePtr;

			Apis.Vk.CreateShaderModule( logicalGpu, createInfo, null, out var shaderModule ).Verify();

			return new VulkanShaderModule( shaderModule, logicalGpu );
		}
	}
}

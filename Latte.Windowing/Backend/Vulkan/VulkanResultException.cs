using Silk.NET.Vulkan;
using System;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanResultException : Exception
{
	internal VulkanResultException( Result result ) : base( $"Expected {Result.Success}, got {result}" )
	{
	}
}

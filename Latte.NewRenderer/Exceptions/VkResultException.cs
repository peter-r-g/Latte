using Silk.NET.Vulkan;

namespace Latte.NewRenderer.Exceptions;

internal sealed class VkResultException : VkException
{
	internal VkResultException( Result result ) : base( $"Expected {Result.Success}, got {result}" )
	{
	}
}

using Latte.Windowing.Renderer.Vulkan.Exceptions;
using Silk.NET.Vulkan;

namespace Latte.Windowing.Renderer.Vulkan.Extensions;

internal static class ResultExtensions
{
	internal static void AssertSuccess( this Result result )
	{
#if DEBUG
		if ( result == Result.Success )
			return;

		throw new VkResultException( result );
#endif
	}
}

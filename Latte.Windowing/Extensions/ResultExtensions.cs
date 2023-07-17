using Latte.Windowing.Backend.Vulkan;
using Silk.NET.Vulkan;

namespace Latte.Windowing.Extensions;

internal static class ResultExtensions
{
	internal static void Verify( this Result result )
	{
#if DEBUG
		if ( result == Result.Success )
			return;

		throw new VulkanResultException( result );
#endif
	}
}

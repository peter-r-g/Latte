using Latte.NewRenderer.Exceptions;
using Silk.NET.Vulkan;

namespace Latte.NewRenderer.Extensions;

internal static class ResultExtensions
{
	internal static void Verify( this Result result )
	{
#if DEBUG
		if ( result == Result.Success )
			return;

		throw new VkResultException( result );
#endif
	}
}

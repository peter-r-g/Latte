using System;

namespace Latte.NewRenderer.Exceptions;

internal sealed class VkInvalidInstanceException : VkException
{
	internal VkInvalidInstanceException( Type type ) : base( $"An instance of {type} is invalid" )
	{
	}
}

using System;

namespace Latte.NewRenderer.Exceptions;

internal sealed class VkInvalidHandleException : VkException
{
	internal VkInvalidHandleException( Type type ) : base( $"An instance of {type} is invalid" )
	{
	}
}

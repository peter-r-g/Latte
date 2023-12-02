using System;

namespace Latte.NewRenderer.Exceptions;

internal class VkException : Exception
{
	public VkException()
	{
	}

	public VkException( string? message ) : base( message )
	{
	}

	public VkException( string? message, Exception? innerException ) : base( message, innerException )
	{
	}
}

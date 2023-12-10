using Latte.NewRenderer.Input;
using Silk.NET.GLFW;
using System;

namespace Latte.NewRenderer.Extensions;

/// <summary>
/// Contains extension methods for <see cref="CursorMode"/>.
/// </summary>
internal static class CursorModeExtensions
{
	/// <summary>
	/// Converts the cursor mode to a GLFW related cursor mode.
	/// </summary>
	/// <param name="cursorMode">The cursor mode to convert.</param>
	/// <returns>The converted cursor mode.</returns>
	/// <exception cref="ArgumentException">Thrown when an unknown cursor mode was passed.</exception>
	internal static CursorModeValue ToGlfw( this CursorMode cursorMode )
	{
		return cursorMode switch
		{
			CursorMode.Visible => CursorModeValue.CursorNormal,
			CursorMode.Hidden => CursorModeValue.CursorHidden,
			CursorMode.Trapped => CursorModeValue.CursorDisabled,
			_ => throw new ArgumentException( $"Received unknown cursor mode \"{cursorMode}\"", nameof(cursorMode) )
		};
	}
}

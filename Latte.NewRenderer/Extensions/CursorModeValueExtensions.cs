using Latte.Windowing.Input;
using Silk.NET.GLFW;
using System;

namespace Latte.Windowing.Extensions;

/// <summary>
/// Contains extension methods for <see cref="CursorModeValue"/>.
/// </summary>
internal static class CursorModeValueExtensions
{
	/// <summary>
	/// Converts a cursor mode value to the engine equivalent.
	/// </summary>
	/// <param name="cursorModeValue">The cursor mode value to convert.</param>
	/// <returns>The converted cursor mode value.</returns>
	/// <exception cref="ArgumentException">Thrown when an unknown cursor mode value was passed.</exception>
	internal static CursorMode ToLatte( this CursorModeValue cursorModeValue )
	{
		return cursorModeValue switch
		{
			CursorModeValue.CursorNormal => CursorMode.Visible,
			CursorModeValue.CursorHidden => CursorMode.Hidden,
			CursorModeValue.CursorDisabled => CursorMode.Trapped,
			_ => throw new ArgumentException( $"Received unknown cursor mode value \"{cursorModeValue}\"", nameof( cursorModeValue ) )
		};
	}
}

using Latte.Windowing.Input;
using Silk.NET.Input;
using System;

namespace Latte.Windowing.Extensions;

/// <summary>
/// Contains extension methods for <see cref="InputButton"/>.
/// </summary>
public static class InputButtonExtensions
{
	/// <summary>
	/// Returns whether or not a button is a keyboard input.
	/// </summary>
	/// <param name="button">The button to check.</param>
	/// <returns>whether or not the button is a keyboard input.</returns>
	public static bool IsKeyboardInput( this InputButton button )
	{
		return button >= InputButton.KeyboardW && button <= InputButton.KeyboardLeftShift;
	}

	/// <summary>
	/// Returns whether or not a button is a mouse button.
	/// </summary>
	/// <param name="button">The button to check.</param>
	/// <returns>Whether or not the button is a mouse button.</returns>
	public static bool IsMouseInput( this InputButton button )
	{
		return button >= InputButton.LMB && button <= InputButton.RMB;
	}

	/// <summary>
	/// Converts a button to a GLFW keyboard key.
	/// </summary>
	/// <param name="button">The button to convert.</param>
	/// <returns>The converted button.</returns>
	/// <exception cref="ArgumentException">Thrown when the button is not recognized as a keyboard key.</exception>
	internal static Key ToKeyboardKey( this InputButton button )
	{
		return button switch
		{
			InputButton.KeyboardW => Key.W,
			InputButton.KeyboardA => Key.A,
			InputButton.KeyboardS => Key.S,
			InputButton.KeyboardD => Key.D,
			InputButton.KeyboardQ => Key.Q,
			InputButton.KeyboardE => Key.E,
			InputButton.KeyboardTab => Key.Tab,
			InputButton.KeyboardEscape => Key.Escape,
			InputButton.KeyboardLeftShift => Key.ShiftLeft,
			_ => throw new ArgumentException( $"Received non keyboard key \"{button}\"", nameof( button ) )
		};
	}

	/// <summary>
	/// Converts a button to a GLFW mouse button.
	/// </summary>
	/// <param name="button">The button to convert.</param>
	/// <returns>The converted button.</returns>
	/// <exception cref="ArgumentException">Thrown when the button is not recognized as a mouse button.</exception>
	internal static MouseButton ToMouseButton( this InputButton button )
	{
		return button switch
		{
			InputButton.LMB => MouseButton.Left,
			InputButton.RMB => MouseButton.Right,
			InputButton.MMB => MouseButton.Middle,
			_ => throw new ArgumentException( $"Received non mouse button \"{button}\"", nameof( button ) )
		};
	}
}

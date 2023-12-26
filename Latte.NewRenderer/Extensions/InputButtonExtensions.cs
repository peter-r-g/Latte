using Latte.NewRenderer.Input;
using Silk.NET.Input;
using System;

namespace Latte.NewRenderer.Extensions;

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
		return button > InputButton.KEYBOARD && button < InputButton.COUNT;
	}

	/// <summary>
	/// Returns whether or not a button is a mouse button.
	/// </summary>
	/// <param name="button">The button to check.</param>
	/// <returns>Whether or not the button is a mouse button.</returns>
	public static bool IsMouseInput( this InputButton button )
	{
		return button > InputButton.MOUSE && button < InputButton.KEYBOARD;
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
			InputButton.KeyboardF1 => Key.F1,
			InputButton.KeyboardF2 => Key.F2,
			InputButton.KeyboardF3 => Key.F3,
			InputButton.KeyboardF4 => Key.F4,
			InputButton.KeyboardF5 => Key.F5,
			InputButton.KeyboardF6 => Key.F6,
			InputButton.KeyboardF7 => Key.F7,
			InputButton.KeyboardF8 => Key.F8,
			InputButton.KeyboardF9 => Key.F9,
			InputButton.KeyboardF10 => Key.F10,
			InputButton.KeyboardF11 => Key.F11,
			InputButton.KeyboardF12 => Key.F12,
			InputButton.KeyboardF13 => Key.F13,
			InputButton.KeyboardF14 => Key.F14,
			InputButton.KeyboardF15 => Key.F15,
			InputButton.KeyboardF16 => Key.F16,
			InputButton.KeyboardF17 => Key.F17,
			InputButton.KeyboardF18 => Key.F18,
			InputButton.KeyboardF19 => Key.F19,
			InputButton.KeyboardF20 => Key.F20,
			InputButton.KeyboardF21 => Key.F21,
			InputButton.KeyboardF22 => Key.F22,
			InputButton.KeyboardF23 => Key.F23,
			InputButton.KeyboardF24 => Key.F24,
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

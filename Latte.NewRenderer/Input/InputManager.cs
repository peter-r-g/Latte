using Latte.NewRenderer.Extensions;
using Silk.NET.Input;
using Silk.NET.Windowing;
using System;
using System.Collections.Generic;
using System.Numerics;
using MouseButton = Silk.NET.Input.MouseButton;

namespace Latte.NewRenderer.Input;

/// <summary>
/// A container that wraps a <see cref="IWindow"/>s <see cref="IInputContext"/>.
/// </summary>
public sealed class InputManager
{
	/// <summary>
	/// The window that this input manager is operating on.
	/// </summary>
	private IWindow Window { get; set; }
	/// <summary>
	/// The input context of the window.
	/// </summary>
	private IInputContext InputContext { get; set; } = null!;

	/// <summary>
	/// Contains a set of keys that have been pressed this frame on all keyboards.
	/// </summary>
	private readonly Dictionary<int, HashSet<Key>> pressedKeyboards = new();
	/// <summary>
	/// Contains a set of buttons that have been pressed this frame on all mice.
	/// </summary>
	private readonly Dictionary<int, HashSet<MouseButton>> pressedMice = new();
	/// <summary>
	/// Contains the mouse deltas of all mice.
	/// </summary>
	private readonly Dictionary<int, Vector2> mouseDeltas = new();

	internal InputManager( IWindow window )
	{
		Window = window;
	}

	/// <summary>
	/// Initializes the input manager.
	/// </summary>
	internal void Initialize()
	{
		InputContext = Window.CreateInput();

		foreach ( var mouse in InputContext.Mice )
			InputConnectionChanged( mouse, true );

		foreach ( var keyboard in InputContext.Keyboards )
			InputConnectionChanged( keyboard, true );

		InputContext.ConnectionChanged += InputConnectionChanged;
	}

	/// <summary>
	/// Resets all single-frame data.
	/// </summary>
	/// <remarks>This should be called after the user-code has executed.</remarks>
	internal void Update()
	{
		foreach ( var (_, pressedKeys) in pressedKeyboards )
			pressedKeys.Clear();

		foreach ( var (_, pressedButtons) in pressedMice )
			pressedButtons.Clear();

		foreach ( var (index, _) in mouseDeltas )
			mouseDeltas[index] = Vector2.Zero;
	}

	/// <summary>
	/// Cleans up all data related to this manager.
	/// </summary>
	internal void Cleanup()
	{
		InputContext.ConnectionChanged -= InputConnectionChanged;

		foreach ( var mouse in InputContext.Mice )
			InputConnectionChanged( mouse, false );

		foreach ( var keyboard in InputContext.Keyboards )
			InputConnectionChanged( keyboard, false );

		InputContext.Dispose();
	}

	/// <summary>
	/// Returns whether or not a button has been pressed this frame.
	/// </summary>
	/// <param name="button">The button to check.</param>
	/// <param name="inputIndex">The index of the input device to check.</param>
	/// <returns>Whether or not the button has been pressed this frame.</returns>
	/// <exception cref="ArgumentException">Thrown when the input button is not recognized.</exception>
	public bool Pressed( InputButton button, int inputIndex = 0 )
	{
		if ( button.IsKeyboardInput() )
			return pressedKeyboards[inputIndex].Contains( button.ToKeyboardKey() );
		else if ( button.IsMouseInput() )
			return pressedMice[inputIndex].Contains( button.ToMouseButton() );
		else
			throw new ArgumentException( $"Received unknown input button \"{button}\"", nameof( button ) );
	}

	/// <summary>
	/// Returns whether or not a button is being pressed.
	/// </summary>
	/// <param name="button">The button to check.</param>
	/// <param name="inputIndex">The index of the input device to check.</param>
	/// <returns>Whether or not the button is being pressed.</returns>
	/// <exception cref="ArgumentException">Thrown when the input button is not recognized.</exception>
	public bool Down( InputButton button, int inputIndex = 0 )
	{
		if ( button.IsKeyboardInput() )
			return InputContext.Keyboards[inputIndex].IsKeyPressed( button.ToKeyboardKey() );
		else if ( button.IsMouseInput() )
			return InputContext.Mice[inputIndex].IsButtonPressed( button.ToMouseButton() );
		else
			throw new ArgumentException( $"Received unknown input button \"{button}\"", nameof( button ) );
	}

	/// <summary>
	/// Returns whether or not a button is not being pressed.
	/// </summary>
	/// <param name="button">The button to check.</param>
	/// <param name="inputIndex">The index of the input device to check.</param>
	/// <returns>Whether or not the button is not being pressed.</returns>
	/// <exception cref="ArgumentException">Thrown when the input button is not recognized.</exception>
	public bool Up( InputButton button, int inputIndex = 0 ) => !Down( button, inputIndex );

	/// <summary>
	/// Returns the delta of the mouse.
	/// </summary>
	/// <param name="mouseIndex">The index of the mouse to check.</param>
	/// <returns>The delta of the mouse.</returns>
	public Vector2 GetMouseDelta( int mouseIndex = 0 )
	{
		return mouseDeltas[mouseIndex];
	}

	/*/// <summary>
	/// Sets the mode the cursor will operate in.
	/// </summary>
	/// <param name="cursorMode">The mode to set the cursor to.</param>
	/// <exception cref="NotImplementedException">Thrown when trying to set the cursor mode on a window that is not supported.</exception>
	public unsafe void SetCursorMode( CursorMode cursorMode )
	{
		if ( !GlfwWindowing.IsViewGlfw( Window ) )
			throw new NotImplementedException( "Only GLFW windows are supported currently" );

		Apis.Glfw.SetInputMode( (WindowHandle*)Window.Handle, CursorStateAttribute.Cursor, cursorMode.ToGlfw() );
	}

	/// <summary>
	/// Gets the mode the cursor is operating in.
	/// </summary>
	/// <returns>The mode the cursor is operating in.</returns>
	/// <exception cref="NotImplementedException">Thrown when trying to get the cursor mode on a window that is not supported.</exception>
	public unsafe CursorMode GetCursorMode()
	{
		if ( !GlfwWindowing.IsViewGlfw( Window ) )
			throw new NotImplementedException( "Only GLFW windows are supported currently" );
		
		var value = (CursorModeValue)Apis.Glfw.GetInputMode( (WindowHandle*)Window.Handle, CursorStateAttribute.Cursor );
		return value.ToLatte();
	}*/

	/// <summary>
	/// Invoked when a input device has changed its connection.
	/// </summary>
	/// <param name="device">The device has changed.</param>
	/// <param name="connected">Whether or not the device is connected.</param>
	private void InputConnectionChanged( IInputDevice device, bool connected )
	{
		if ( device is IKeyboard keyboard )
		{
			if ( connected )
			{
				pressedKeyboards.Add( keyboard.Index, new HashSet<Key>() );
				keyboard.KeyDown += KeyboardKeyPressed;
			}
			else
			{
				pressedKeyboards.Remove( keyboard.Index );
				keyboard.KeyDown -= KeyboardKeyPressed;
			}
		}
		else if ( device is IMouse mouse )
		{
			if ( connected )
			{
				pressedMice.Add( mouse.Index, new HashSet<MouseButton>() );
				mouseDeltas.Add( device.Index, Vector2.Zero );
				mouse.MouseMove += OnMouseMove;
				mouse.MouseDown += MouseButtonPressed;
			}
			else
			{
				pressedMice.Remove( mouse.Index );
				mouseDeltas.Remove( device.Index );
				mouse.MouseMove -= OnMouseMove;
				mouse.MouseDown -= MouseButtonPressed;
			}
		}
	}

	/// <summary>
	/// Invoked when a keyboards key has been pressed.
	/// </summary>
	/// <param name="keyboard">The keyboard whose key has been pressed.</param>
	/// <param name="key">The key that was pressed.</param>
	private void KeyboardKeyPressed( IKeyboard keyboard, Key key, int _ )
	{
		pressedKeyboards[keyboard.Index].Add( key );
	}

	/// <summary>
	/// Invoked when a mouse has moved.
	/// </summary>
	/// <param name="mouse">The mouse that has moved.</param>
	/// <param name="delta">The delta of the mouse.</param>
	private void OnMouseMove( IMouse mouse, Vector2 delta )
	{
		mouseDeltas[mouse.Index] = delta;
	}

	/// <summary>
	/// Invoked when a mouse button has been pressed.
	/// </summary>
	/// <param name="mouse">The mouse that has had its button pressed.</param>
	/// <param name="button">The button that was pressed.</param>
	private void MouseButtonPressed( IMouse mouse, MouseButton button )
	{
		pressedMice[mouse.Index].Add( button );
	}
}

namespace Latte.Windowing.Input;

/// <summary>
/// Defines a mode for a cursor to operate in.
/// </summary>
public enum CursorMode
{
	/// <summary>
	/// The cursor is visible.
	/// </summary>
	Visible,
	/// <summary>
	/// The cursor is hidden but can still leave the windows focus.
	/// </summary>
	Hidden,
	/// <summary>
	/// The cursor is hidden and trapped inside the window.
	/// </summary>
	Trapped
}

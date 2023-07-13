using Latte.Assets;
using Latte.Windowing.Options;
using System.Numerics;

namespace Latte.Windowing;

/// <summary>
/// Defines a publically accessible API to a rendering API.
/// </summary>
public interface IRenderingBackend
{
	/// <summary>
	/// The modifiable options of rendering.
	/// </summary>
	IRenderingOptions Options { get; }

	delegate void OptionsAppliedHandler( IRenderingBackend backend );
	/// <summary>
	/// An event that is invoked once the rendering API has applied all changed options.
	/// </summary>
	event OptionsAppliedHandler? OptionsApplied;

	/// <summary>
	/// Draws a model at <see cref="Vector3.Zero"/> in world co-ordinates.
	/// </summary>
	/// <param name="model">The model to draw.</param>
	void DrawModel( Model model );
	/// <summary>
	/// Draws a model at a given world position.
	/// </summary>
	/// <param name="model">The model to draw.</param>
	/// <param name="position">The world position to draw the model at.</param>
	void DrawModel( Model model, in Vector3 position );
}

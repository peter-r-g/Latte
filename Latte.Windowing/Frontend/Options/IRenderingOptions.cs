namespace Latte.Windowing.Options;

/// <summary>
/// Defines an object that can receive changes to a rendering backends options.
/// </summary>
public interface IRenderingOptions
{
	/// <summary>
	/// Whether or not to render everything in wireframe.
	/// </summary>
	bool WireframeEnabled { get; set; }
	/// <summary>
	/// The level of Multi-sampling Anti-aliasing to use.
	/// </summary>
	MsaaOption Msaa { get; set; }

	/// <summary>
	/// Returns whether or not options have changed.
	/// </summary>
	/// <param name="optionNames">An optional array of option names to check.</param>
	/// <returns>Whether or not options have changed.</returns>
	bool HasOptionsChanged( params string[] optionNames );
	/// <summary>
	/// Lets the backend renderer know that it should apply any changed options.
	/// </summary>
	void ApplyOptions();
}

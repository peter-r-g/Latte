using System;

namespace Latte.Windowing;

/// <summary>
/// Defines a type of rendering API that is supported.
/// </summary>
/// <remarks>
/// This is implemented with <see cref="FlagsAttribute"/> so rendering APIs can track initialization on assets.
/// </remarks>
[Flags]
public enum RenderingBackend
{
	/// <summary>
	/// The Vulkan API.
	/// </summary>
	Vulkan = 1
}

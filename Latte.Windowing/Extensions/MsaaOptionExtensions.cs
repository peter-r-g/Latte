using Latte.Windowing.Options;
using Silk.NET.Vulkan;
using System;

namespace Latte.Windowing.Extensions;

/// <summary>
/// Contains extension methods for <see cref="MsaaOption"/>.
/// </summary>
internal static class MsaaOptionExtensions
{
	/// <summary>
	/// Converts a MSAA option to a Vulkan option.
	/// </summary>
	/// <param name="msaaOption">The option to convert.</param>
	/// <returns>The converted MSAA option.</returns>
	/// <exception cref="ArgumentException">Thrown when the MSAA option is invalid.</exception>
	internal static SampleCountFlags ToVulkan( this MsaaOption msaaOption )
	{
		return msaaOption switch
		{
			MsaaOption.One => SampleCountFlags.Count1Bit,
			MsaaOption.Two => SampleCountFlags.Count2Bit,
			MsaaOption.Four => SampleCountFlags.Count4Bit,
			MsaaOption.Eight => SampleCountFlags.Count8Bit,
			MsaaOption.Sixteen => SampleCountFlags.Count16Bit,
			MsaaOption.ThirtyTwo => SampleCountFlags.Count32Bit,
			MsaaOption.SixtyFour => SampleCountFlags.Count64Bit,
			_ => throw new ArgumentException( $"Received invalid {nameof( MsaaOption )}", nameof( msaaOption ) )
		};
	}
}

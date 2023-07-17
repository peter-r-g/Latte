using Latte.Logging;

namespace Latte.Windowing;

internal static class Loggers
{
	internal static Logger Vulkan { get; } = new( "Vulkan", LogLevel.Verbose );
}

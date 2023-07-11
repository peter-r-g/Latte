using Latte.Logging;

namespace Latte.Hotload;

internal static class Loggers
{
	internal static Logger Hotloader { get; } = new( "Hotloader", LogLevel.Verbose );
	internal static Logger Compiler { get; } = new( "Compiler", LogLevel.Verbose );
	internal static Logger NuGet { get; } = new( "NuGet", LogLevel.None );
}

using Latte.Hotload;
using Latte.Logging;

namespace Latte.Windowing;

public sealed class WindowingEntryPoint : IEntryPoint
{
	public Logger? Log { get; set; }

	public void Main()
	{
		Log = new Logger( "Windowing", LogLevel.Verbose );
		Log.Information( "Hello from windowing!" );
	}

	public void PostHotload()
	{
	}

	public void PreHotload()
	{
	}
}

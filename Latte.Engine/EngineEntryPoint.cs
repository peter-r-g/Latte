using Latte.Attributes;
using Latte.Hotload;
using Latte.Logging;

namespace Latte.Engine;

public sealed class EngineEntryPoint : IEntryPoint
{
	internal static Logger Log { get; } = new( "Engine", LogLevel.Verbose );

	[SkipUpgrading] private static bool NeverHotloaded { get; set; }
	private static bool HasMainExecuted { get; set; } = false;
	private int TimesHotloaded { get; set; } = 0;

	public void Main()
	{
		Log.Information( "Hello from engine!" );
		HasMainExecuted = true;
		NeverHotloaded = true;

		Program.AddAssembly( new AssemblyInfo
		{
			Name = "Latte.Windowing",
			Path = "Latte.Windowing.dll",
			ProjectPath = "../Latte.Windowing"
		} );

	}

	public void PreHotload()
	{
		Log.Information( $"Has the engine main been executed? {(HasMainExecuted ? "Yes" : "No")}" );
		Log.Information( $"This will be hotload #{TimesHotloaded + 1}" );
	}

	public void PostHotload()
	{
		TimesHotloaded++;
		Log.Information( $"Has the engine assembly ever hotloaded? {(NeverHotloaded ? "No" : "Yes")}" );
	}
}

using Latte.Hotload;
using System;

namespace Latte.Engine;

public sealed class EngineEntryPoint : IEntryPoint
{
	private static bool HasMainExecuted { get; set; } = false;
	private int TimesHotloaded { get; set; } = 0;

	public void Main()
	{
		Console.WriteLine( "Hello from engine!" );
		HasMainExecuted = true;
	}

	public void PreHotload()
	{
		Console.WriteLine( $"Has the engine main been executed? {(HasMainExecuted ? "Yes" : "No")}" );
		Console.WriteLine( $"This will be hotload #{TimesHotloaded + 1}" );
	}

	public void PostHotload()
	{
		TimesHotloaded++;
	}
}

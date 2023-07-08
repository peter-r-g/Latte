using Latte.Hotload;
using System;

namespace Latte.Engine;

public sealed class EngineEntryPoint : IEntryPoint
{
	public void Main()
	{
		Console.WriteLine( "Hello from engine!" );
	}

	public void PreHotload()
	{
	}

	public void PostHotload()
	{
	}
}

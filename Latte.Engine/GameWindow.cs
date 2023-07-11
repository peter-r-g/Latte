using Latte.Hotload;
using Latte.Windowing;
using System;
using System.Linq;

namespace Latte;

internal sealed class GameWindow : Window
{
	protected override bool EnableVulkanValidationLayers => true;

	private IGame? ActiveGame { get; set; }

	protected override void Load()
	{
		Program.AddAssemblyAsync( new AssemblyInfo()
		{
			Name = "Sandbox",
			ProjectPath = "../Sandbox"
		} ).Wait();

		var sandboxAssembly = HotloadableAssembly.All["Sandbox"];
		var gameType = sandboxAssembly.Assembly!.ExportedTypes.FirstOrDefault( type =>
		{
			foreach ( var @interface in type.GetInterfaces() )
			{
				if ( @interface == typeof( IGame ) )
					return true;
			}

			return false;
		} );

		if ( gameType is null )
			return;
		
		ActiveGame = (IGame)Activator.CreateInstance( gameType )!;
		ActiveGame.Input = Input;
		ActiveGame.Renderer = Renderer;
		ActiveGame.Load();
	}

	protected override void Update( double dt )
	{
		ActiveGame?.Update( dt );
	}

	protected override void Render( double dt )
	{
		ActiveGame?.Draw( dt );
	}
}

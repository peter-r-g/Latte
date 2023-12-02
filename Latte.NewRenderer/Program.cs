using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.SDL;
using Silk.NET.Windowing.Sdl;
using Silk.NET.Windowing;
using Window = Silk.NET.Windowing.Window;

namespace Latte.NewRenderer;

internal static class Program
{
	private static void Main()
	{
		SdlWindowing.Use();
		SdlProvider.SetMainReady = true;
		var options = WindowOptions.DefaultVulkan with
		{
			Size = new Vector2D<int>( 1700, 900 )
		};
		var window = Window.Create( options );
		window.Initialize();

		using var engine = new VkEngine();
		engine.Initialize( window );

		var inputContext = window.CreateInput();
		var keyboard = inputContext.Keyboards[0];

		while ( !window.IsClosing )
		{
			window.DoEvents();
			engine.Draw();

			if ( keyboard.IsKeyPressed( Key.Number1 ) )
				engine.currentPipeline = engine.redTrianglePipeline;
			else if ( keyboard.IsKeyPressed( Key.Number2 ) )
				engine.currentPipeline = engine.coloredTrianglePipeline;
			else if ( keyboard.IsKeyPressed( Key.Number3 ) )
				engine.currentPipeline = engine.meshPipeline;

			if ( keyboard.IsKeyPressed( Key.Escape ) )
				window.Close();
		}

		engine.WaitForIdle();
	}
}

using Latte.NewRenderer.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;
using System;
using System.Numerics;
using Window = Silk.NET.Windowing.Window;

namespace Latte.NewRenderer;

internal static class Program
{
	private static IWindow window = null!;
	private static VkEngine engine = null!;
	private static InputManager input = null!;
	private static Vector2 lastMousePosition;

	private static unsafe void Main()
	{
		GlfwWindowing.Use();
		var options = WindowOptions.DefaultVulkan with
		{
			Size = new Vector2D<int>( 1700, 900 )
		};
		window = Window.Create( options );
		window.Initialize();

		engine = new VkEngine();
		engine.Initialize( window );
		window.Update += Update;
		window.Render += Render;

		input = new InputManager( window );
		input.Initialize();

		input.SetCursorMode( CursorMode.Trapped );

		window.Run( () =>
		{
			window.DoEvents();
			if ( !window.IsClosing )
				window.DoUpdate();

			if ( !window.IsClosing && window.IsVisible )
				window.DoRender();

			input.Update();
		} );

		engine.WaitForIdle();
		engine.Dispose();
		input.Cleanup();
		window.Dispose();
	}

	private static unsafe void Update( double dt )
	{
		UpdateCamera( dt );

		if ( input.Pressed( InputButton.KeyboardEscape ) )
			window.Close();

		if ( input.Pressed( InputButton.KeyboardE ) )
			engine.WireframeEnabled = !engine.WireframeEnabled;
	}

	private static void Render( double dt )
	{
		engine.Draw();
	}

	private static void UpdateCamera( double dt )
	{
		var moveSpeed = (input.Down( InputButton.KeyboardLeftShift ) ? 10 : 2.5f) * (float)dt;

		//Move forwards
		if ( input.Down( InputButton.KeyboardW ) )
			Camera.Main.Position += moveSpeed * Camera.Main.Front;
		//Move backwards
		if ( input.Down( InputButton.KeyboardS ) )
			Camera.Main.Position -= moveSpeed * Camera.Main.Front;
		//Move left
		if ( input.Down( InputButton.KeyboardA ) )
			Camera.Main.Position -= Vector3.Normalize( Vector3.Cross( Camera.Main.Front, Camera.Main.Up ) ) * moveSpeed;
		//Move right
		if ( input.Down( InputButton.KeyboardD ) )
			Camera.Main.Position += Vector3.Normalize( Vector3.Cross( Camera.Main.Front, Camera.Main.Up ) ) * moveSpeed;

		var position = input.GetMouseDelta();
		if ( position == Vector2.Zero )
			return;

		/*if ( Input.GetCursorMode() == CursorMode.Visible || lastMousePosition == default )
		{
			lastMousePosition = position;
			return;
		}*/

		var lookSensitivity = 0.1f;
		var xOffset = (position.X - lastMousePosition.X) * lookSensitivity;
		var yOffset = (position.Y - lastMousePosition.Y) * lookSensitivity;
		lastMousePosition = position;

		Camera.Main.Yaw += xOffset;
		Camera.Main.Pitch -= yOffset;

		//We don't want to be able to look behind us by going over our head or under our feet so make sure it stays within these bounds
		//Camera.Main.Pitch = Math.Clamp( Camera.Main.Pitch, -89.0f, 89.0f );

		Camera.Main.Direction = new Vector3(
			MathF.Cos( Scalar.DegreesToRadians( Camera.Main.Yaw ) ) * MathF.Cos( Scalar.DegreesToRadians( Camera.Main.Pitch ) ),
			MathF.Sin( Scalar.DegreesToRadians( Camera.Main.Pitch ) ),
			MathF.Sin( Scalar.DegreesToRadians( Camera.Main.Yaw ) ) * MathF.Cos( Scalar.DegreesToRadians( Camera.Main.Pitch ) )
		);
	}
}

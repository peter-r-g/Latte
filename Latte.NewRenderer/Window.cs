using Latte.NewRenderer.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;
using System;
using System.Numerics;

namespace Latte.NewRenderer;

public class Window
{
	public bool IsRunning { get; private set; }

	private IWindow window = null!;
	private VkEngine engine = null!;
	private InputManager input = null!;

	private Vector2 lastMousePosition;
	private bool showImguiDemo;

	internal unsafe void Main()
	{
		IsRunning = true;
		GlfwWindowing.Use();
		var options = WindowOptions.DefaultVulkan with
		{
			Size = new Vector2D<int>( 1700, 900 )
		};
		window = Silk.NET.Windowing.Window.Create( options );
		window.Initialize();

		input = new InputManager( window );
		input.Initialize();

		engine = new VkEngine( window, input.InputContext );
		window.Update += Update;
		window.Render += Render;

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
		IsRunning = false;
	}

	internal void Close() => window.Close();

	private void Update( double dt )
	{
		engine.ImGuiController.Update( (float)dt );

		window.Title = $"Latte.NewRenderer ({(int)(1 / dt)} FPS)";

		UpdateCamera( dt );

		if ( input.Pressed( InputButton.KeyboardEscape ) )
			window.Close();

		if ( input.Pressed( InputButton.KeyboardTab ) )
			input.SetCursorMode( input.GetCursorMode() == CursorMode.Visible ? CursorMode.Trapped : CursorMode.Visible );

		if ( input.Pressed( InputButton.KeyboardF1 ) )
			showImguiDemo = !showImguiDemo;

		engine.ImGuiShowRendererStatistics();
		if ( showImguiDemo )
			ImGuiNET.ImGui.ShowDemoWindow();
	}

	private void Render( double dt )
	{
		engine.Draw();
	}

	private void UpdateCamera( double dt )
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

		if ( input.GetCursorMode() == CursorMode.Visible || lastMousePosition == default )
		{
			lastMousePosition = position;
			return;
		}

		var lookSensitivity = 0.1f;
		var xOffset = (position.X - lastMousePosition.X) * lookSensitivity;
		var yOffset = (position.Y - lastMousePosition.Y) * lookSensitivity;
		lastMousePosition = position;

		Camera.Main.Yaw += xOffset;
		Camera.Main.Pitch -= yOffset;

		//We don't want to be able to look behind us by going over our head or under our feet so make sure it stays within these bounds
		Camera.Main.Pitch = Math.Clamp( Camera.Main.Pitch, -89.0f, 89.0f );

		Camera.Main.Direction = new Vector3(
			MathF.Cos( Scalar.DegreesToRadians( Camera.Main.Yaw ) ) * MathF.Cos( Scalar.DegreesToRadians( Camera.Main.Pitch ) ),
			MathF.Sin( Scalar.DegreesToRadians( Camera.Main.Pitch ) ),
			MathF.Sin( Scalar.DegreesToRadians( Camera.Main.Yaw ) ) * MathF.Cos( Scalar.DegreesToRadians( Camera.Main.Pitch ) )
		);
	}
}

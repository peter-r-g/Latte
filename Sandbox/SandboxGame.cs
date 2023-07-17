using Latte;
using Latte.Assets;
using Latte.Windowing;
using Latte.Windowing.Input;
using Latte.Windowing.Options;
using System;
using System.Numerics;
using Zio;

namespace Sandbox;

public sealed class SandboxGame : IGame
{
	private static readonly UPath VikingRoomModelPath = "/Models/viking_room.obj";
	private static readonly UPath VikingRoomTexturePath = "/Textures/viking_room.png";

	public InputManager Input { get; set; } = null!;
	public IRenderingBackend Renderer { get; set; } = null!;

	private Texture VikingRoomTexture { get; set; } = null!;
	private Model VikingRoomModel { get; set; } = null!;
	private Vector2 LastMousePosition { get; set; }

	public void Load()
	{
		VikingRoomTexture = Texture.FromPath( VikingRoomTexturePath );
		VikingRoomModel = Model.FromPath( VikingRoomModelPath );
	}

	public void Update( double dt )
	{
		UpdateKeyboard( dt );
		UpdateMouse( dt );
	}

	private void UpdateKeyboard( double dt )
	{
		var moveSpeed = (Input.Down( InputButton.KeyboardLeftShift ) ? 10 : 2.5f) * (float)dt;

		//Move forwards
		if ( Input.Down( InputButton.KeyboardW ) )
			Camera.Position += moveSpeed * Camera.Front;
		//Move backwards
		if ( Input.Down( InputButton.KeyboardS ) )
			Camera.Position -= moveSpeed * Camera.Front;
		//Move left
		if ( Input.Down( InputButton.KeyboardA ) )
			Camera.Position -= Vector3.Normalize( Vector3.Cross( Camera.Front, Camera.Up ) ) * moveSpeed;
		//Move right
		if ( Input.Down( InputButton.KeyboardD ) )
			Camera.Position += Vector3.Normalize( Vector3.Cross( Camera.Front, Camera.Up ) ) * moveSpeed;

		// Toggle wireframe
		if ( Input.Pressed( InputButton.KeyboardQ ) )
		{
			Renderer.Options.WireframeEnabled = !Renderer.Options.WireframeEnabled;
			Renderer.Options.ApplyOptions();
		}

		// Change MSAA options
		if ( Input.Pressed( InputButton.KeyboardE ) )
		{
			switch ( Renderer.Options.Msaa )
			{
				case MsaaOption.One:
					Renderer.Options.Msaa = MsaaOption.Two;
					break;
				case MsaaOption.Two:
					Renderer.Options.Msaa = MsaaOption.Four;
					break;
				case MsaaOption.Four:
					Renderer.Options.Msaa = MsaaOption.Eight;
					break;
				case MsaaOption.Eight:
					Renderer.Options.Msaa = MsaaOption.One;
					break;
			}

			Renderer.Options.ApplyOptions();
		}

		// Change cursor behavior
		if ( Input.Pressed( InputButton.KeyboardTab ) )
		{
			if ( Input.GetCursorMode() == CursorMode.Visible )
				Input.SetCursorMode( CursorMode.Trapped );
			else
				Input.SetCursorMode( CursorMode.Visible );
		}

		// Close the application
		//if ( Input.Pressed( InputButton.KeyboardEscape ) )
		//	Close();
	}

	private void UpdateMouse( double dt )
	{
		var position = Input.GetMouseDelta();
		if ( position == Vector2.Zero )
			return;

		if ( Input.GetCursorMode() == CursorMode.Visible || LastMousePosition == default )
		{
			LastMousePosition = position;
			return;
		}

		var lookSensitivity = 0.1f;
		var xOffset = (position.X - LastMousePosition.X) * lookSensitivity;
		var yOffset = (position.Y - LastMousePosition.Y) * lookSensitivity;
		LastMousePosition = position;

		Camera.Yaw += xOffset;
		Camera.Pitch -= yOffset;

		//We don't want to be able to look behind us by going over our head or under our feet so make sure it stays within these bounds
		Camera.Pitch = Math.Clamp( Camera.Pitch, -89.0f, 89.0f );

		Camera.Direction = new Vector3(
			MathF.Cos( DegreesToRadians( Camera.Yaw ) ) * MathF.Cos( DegreesToRadians( Camera.Pitch ) ),
			MathF.Sin( DegreesToRadians( Camera.Pitch ) ),
			MathF.Sin( DegreesToRadians( Camera.Yaw ) ) * MathF.Cos( DegreesToRadians( Camera.Pitch ) )
		);
	}

	public void Draw( double dt )
	{
		Renderer.SetTexture( VikingRoomTexture );
		
		for ( var x = 0; x < 30; x++ )
		{
			for ( var y = 0; y < 30; y++ )
			{
				Renderer.DrawModel( VikingRoomModel, new Vector3( x * 2, 0, y * 2 ) );
			}
		}
	}

	private static float DegreesToRadians( float degrees )
	{
		return degrees * (MathF.PI / 180);
	}
}

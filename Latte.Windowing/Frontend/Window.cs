using Latte.Windowing.Backend;
using Latte.Windowing.Backend.Vulkan;
using Latte.Windowing.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using System;

namespace Latte.Windowing;

/// <summary>
/// A graphical window that utilizes a <see cref="Windowing.RenderingBackend"/>.
/// </summary>
public class Window : IDisposable
{
	/// <summary>
	/// The initial title to give the window.
	/// </summary>
	protected virtual string InitialTitle => "Latte";
	/// <summary>
	/// The initial width of the window in pixels.
	/// </summary>
	protected virtual int InitialWidth => 800;
	/// <summary>
	/// The initial height of the window in pixels.
	/// </summary>
	protected virtual int InitialHeight => 600;
	/// <summary>
	/// The rendering backend API to use for the window.
	/// </summary>
	protected virtual RenderingBackend RenderingBackend => RenderingBackend.Vulkan;
	/// <summary>
	/// Whether or not to enable Vulkan validation layers.
	/// </summary>
	/// <remarks>
	/// This only applies when your <see cref="RenderingBackend"/> is <see cref="RenderingBackend.Vulkan"/>.
	/// </remarks>
	protected virtual bool EnableVulkanValidationLayers => false;

	/// <summary>
	/// A container for all input being applied to this window.
	/// </summary>
	protected InputManager Input { get; }
	/// <summary>
	/// The rendering API.
	/// </summary>
	protected IRenderingBackend Renderer => InternalRenderer;
	/// <summary>
	/// The internal rendering backend.
	/// </summary>
	private IInternalRenderingBackend InternalRenderer { get; }
	/// <summary>
	/// The graphical window.
	/// </summary>
	private IWindow UnderlyingWindow { get; }

	public Window()
	{
		var options = WindowOptions.DefaultVulkan with
		{
			Size = new Vector2D<int>( InitialWidth, InitialHeight ),
			Title = InitialTitle
		};

		UnderlyingWindow = Silk.NET.Windowing.Window.Create( options );
		Input = new InputManager( UnderlyingWindow );
		InternalRenderer = RenderingBackend switch
		{
			RenderingBackend.Vulkan => new VulkanBackend( UnderlyingWindow, EnableVulkanValidationLayers ),
			_ => throw new ArgumentException( $"Unknown rendering backend {RenderingBackend}", nameof( RenderingBackend ) )
		};

		UnderlyingWindow.Update += Update;
		UnderlyingWindow.Render += RenderInternal;
		UnderlyingWindow.Closing += OnClosing;
		UnderlyingWindow.Move += OnMove;
		UnderlyingWindow.Resize += OnResize;
		UnderlyingWindow.StateChanged += OnStateChanged;
		UnderlyingWindow.FocusChanged += OnFocusChanged;
		UnderlyingWindow.FileDrop += OnFileDrop;

		UnderlyingWindow.Initialize();
		Input.Initialize();
		InternalRenderer.Inititalize();
	}

	~Window()
	{
		Dispose();
	}

	/// <summary>
	/// Runs the application window.
	/// </summary>
	public void Run()
	{
		Load();
		UnderlyingWindow.Run( () =>
		{
			UnderlyingWindow.DoEvents();
			if ( !UnderlyingWindow.IsClosing )
				UnderlyingWindow.DoUpdate();

			if ( !UnderlyingWindow.IsClosing )
				UnderlyingWindow.DoRender();

			Input.Update();
		} );
		Dispose();
	}

	/// <summary>
	/// Closes the application window.
	/// </summary>
	public void Close()
	{
		UnderlyingWindow.Close();
	}

	/// <summary>
	/// Invoked once at the start of the window to load data.
	/// </summary>
	protected virtual void Load()
	{
	}

	/// <summary>
	/// Invoked once at the end of the window to unload data.
	/// </summary>
	protected virtual void Unload()
	{
	}

	/// <summary>
	/// Invoked once a frame to update logic of the application.
	/// </summary>
	/// <param name="dt">The time in miliseconds since the last frame.</param>
	protected virtual void Update( double dt )
	{
	}

	/// <summary>
	/// Invoked once a frame to render graphical items to the application window.
	/// </summary>
	/// <param name="dt">The time in miliseconds since the last frame.</param>
	protected virtual void Render( double dt )
	{
	}

	/// <summary>
	/// Invoked when the underlying window is being closed.
	/// </summary>
	protected virtual void OnClosing()
	{
	}

	/// <summary>
	/// Invoked when the window has been moved.
	/// </summary>
	/// <param name="newPosition">The new position of the window from the top-left of the monitor.</param>
	protected virtual void OnMove( Vector2D<int> newPosition )
	{
	}

	/// <summary>
	/// Invoked when the window has been resized.
	/// </summary>
	/// <param name="newSize">The new size of the window.</param>
	protected virtual void OnResize( Vector2D<int> newSize )
	{
	}

	/// <summary>
	/// Invoked when the windows state has been changed.
	/// </summary>
	/// <param name="newState">The new state of the window.</param>
	protected virtual void OnStateChanged( WindowState newState )
	{
	}

	/// <summary>
	/// Invoked when the focus has been changed on the window.
	/// </summary>
	/// <param name="focused"></param>
	protected virtual void OnFocusChanged( bool focused )
	{
	}

	/// <summary>
	/// Invoked when files have been dropped onto the window.
	/// </summary>
	/// <param name="files"></param>
	protected virtual void OnFileDrop( string[] files )
	{
	}

	/// <summary>
	/// An internal wrapper for the render method to run backend rendering.
	/// </summary>
	/// <param name="dt"></param>
	private void RenderInternal( double dt )
	{
		InternalRenderer.BeginFrame();
		Render( dt );
		InternalRenderer.EndFrame();
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		if ( !UnderlyingWindow.IsClosing )
			throw new InvalidOperationException( "The window should only be disposed when the window has been requested to close" );

		UnderlyingWindow.Load -= Load;
		UnderlyingWindow.Update -= Update;
		UnderlyingWindow.Render -= RenderInternal;
		UnderlyingWindow.Closing -= OnClosing;
		UnderlyingWindow.Move -= OnMove;
		UnderlyingWindow.Resize -= OnResize;
		UnderlyingWindow.StateChanged -= OnStateChanged;
		UnderlyingWindow.FocusChanged -= OnFocusChanged;
		UnderlyingWindow.FileDrop -= OnFileDrop;

		InternalRenderer.WaitForIdle();
		Unload();

		InternalRenderer.Cleanup();
		Input.Cleanup();
		UnderlyingWindow.Reset();
		UnderlyingWindow.Dispose();

		GC.SuppressFinalize( this );
	}
}

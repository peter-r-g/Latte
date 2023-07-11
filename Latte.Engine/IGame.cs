using Latte.Windowing;
using Latte.Windowing.Input;

namespace Latte;

public interface IGame
{
	InputManager Input { get; set; }
	IRenderingBackend Renderer { get; set; }

	void Load();

	void Update( double dt );
	void Draw( double dt );
}

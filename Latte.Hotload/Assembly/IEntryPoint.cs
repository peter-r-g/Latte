namespace Latte.Hotload;

public interface IEntryPoint
{
	void Main();

	void PreHotload();
	void PostHotload();
}

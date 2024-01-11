using System;

namespace Latte.NewRenderer.Vulkan.ImGui;

public readonly struct ImGuiFontConfig
{
	public ImGuiFontConfig( string fontPath, int fontSize )
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( fontSize );
		FontPath = fontPath ?? throw new ArgumentNullException( nameof( fontPath ) );
		FontSize = fontSize;
	}

	public string FontPath { get; }
	public int FontSize { get; }
}

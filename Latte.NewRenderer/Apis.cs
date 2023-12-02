using Silk.NET.SDL;
using Silk.NET.Vulkan;

namespace Latte.NewRenderer;

internal static class Apis
{
	internal static readonly Sdl Sdl = Sdl.GetApi();
	internal static readonly Vk Vk = Vk.GetApi();
}

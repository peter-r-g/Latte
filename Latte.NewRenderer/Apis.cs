using Silk.NET.GLFW;
using Silk.NET.Vulkan;

namespace Latte.Windowing;

internal static class Apis
{
	internal static readonly Glfw Glfw = Glfw.GetApi();
	internal static readonly Vk Vk = Vk.GetApi();
}

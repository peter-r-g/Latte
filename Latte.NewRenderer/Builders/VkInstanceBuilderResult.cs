using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;

namespace Latte.NewRenderer.Builders;

internal readonly struct VkInstanceBuilderResult
{
	internal readonly Instance Instance;
	internal readonly ExtDebugUtils? DebugUtilsExtension;
	internal readonly DebugUtilsMessengerEXT DebugMessenger;

	internal VkInstanceBuilderResult( Instance instance, ExtDebugUtils? debugUtilsExtension, DebugUtilsMessengerEXT debugMessenger )
	{
		Instance = instance;
		DebugUtilsExtension = debugUtilsExtension;
		DebugMessenger = debugMessenger;
	}
}

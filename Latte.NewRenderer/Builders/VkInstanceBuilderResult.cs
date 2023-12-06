using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using System.Diagnostics.CodeAnalysis;

namespace Latte.NewRenderer.Builders;

[method: SetsRequiredMembers]
internal struct VkInstanceBuilderResult( Instance instance, ExtDebugUtils? debugUtilsExtension, DebugUtilsMessengerEXT debugMessenger )
{
	internal required Instance Instance = instance;
	internal required ExtDebugUtils? DebugUtilsExtension = debugUtilsExtension;
	internal required DebugUtilsMessengerEXT DebugMessenger = debugMessenger;
}

using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using System.Diagnostics.CodeAnalysis;

namespace Latte.NewRenderer.Builders;

[method: SetsRequiredMembers]
internal readonly struct VkInstanceBuilderResult(
	Instance instance,
	ExtDebugUtils? debugUtilsExtension,
	DebugUtilsMessengerEXT debugMessenger )
{
	internal required Instance Instance { get; init; } = instance;
	internal required ExtDebugUtils? DebugUtilsExtension { get; init; } = debugUtilsExtension;
	internal required DebugUtilsMessengerEXT DebugMessenger { get; init; } = debugMessenger;
}

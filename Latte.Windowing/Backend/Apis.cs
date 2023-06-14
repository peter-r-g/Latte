using Silk.NET.Assimp;
using Silk.NET.GLFW;
using Silk.NET.Vulkan;
using System;

namespace Latte.Windowing.Backend;

internal static class Apis
{
	internal static Vk Vk
	{
		get
		{
			vk ??= Vk.GetApi();
			return vk;
		}
	}
	[ThreadStatic]
	private static Vk? vk;

	internal static Assimp Assimp
	{
		get
		{
			assimp ??= Assimp.GetApi();
			return assimp;
		}
	}
	[ThreadStatic]
	private static Assimp? assimp;

	internal static Glfw Glfw
	{
		get
		{
			glfw ??= Glfw.GetApi();
			return glfw;
		}
	}
	[ThreadStatic]
	private static Glfw? glfw;
}

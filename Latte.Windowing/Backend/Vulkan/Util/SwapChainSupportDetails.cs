﻿using Silk.NET.Vulkan;

namespace Latte.Windowing.Backend.Vulkan;

internal struct SwapchainSupportDetails
{
	internal SurfaceCapabilitiesKHR Capabilities { get; set; }
	internal SurfaceFormatKHR[] Formats { get; set; }
	internal PresentModeKHR[] PresentModes { get; set; }
}

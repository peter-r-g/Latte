using Latte.Windowing.Extensions;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class Gpu : VulkanWrapper
{
	internal PhysicalDevice PhysicalDevice { get; }

	internal IReadOnlyCollection<LogicalGpu> LogicalGpus => logicalGpus;
	private readonly ConcurrentBag<LogicalGpu> logicalGpus = new();

	internal PhysicalDeviceFeatures Features { get; }
	internal PhysicalDeviceProperties Properties { get; }
	internal PhysicalDeviceMemoryProperties MemoryProperties { get; }
	internal SwapchainSupportDetails SwapchainSupportDetails => GetSwapchainSupport();

	internal Gpu( in PhysicalDevice physicalDevice, VulkanInstance instance ) : base( instance )
	{
		PhysicalDevice = physicalDevice;

		Features = Apis.Vk.GetPhysicalDeviceFeatures( physicalDevice );
		Properties = Apis.Vk.GetPhysicalDeviceProperties( physicalDevice );
		MemoryProperties = Apis.Vk.GetPhysicalDeviceMemoryProperties( physicalDevice );
	}

	public override void Dispose()
	{
		if ( Disposed )
			return;

		foreach ( var logicalGpu in LogicalGpus )
			logicalGpu.Dispose();

		GC.SuppressFinalize( this );
		Disposed = true;
	}

	internal unsafe LogicalGpu CreateLogicalGpu( in QueueFamilyIndices familyIndices, in PhysicalDeviceFeatures features,
		string[] extensions, bool enableValidationLayers = false, string[]? validationLayers = null )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( Gpu ) );

		if ( enableValidationLayers && (validationLayers is null || validationLayers.Length == 0) )
			throw new ArgumentException( "No validation layers were passed", nameof( validationLayers ) );

		if ( !familyIndices.IsComplete() )
			throw new ArgumentException( "Attempted to create a logical device from indices that are not complete", nameof( familyIndices ) );

		var queuePriority = 1f;
		var uniqueIndices = familyIndices.GetUniqueFamilies();

		var queueCreateInfos = stackalloc DeviceQueueCreateInfo[uniqueIndices.Count];
		for ( uint i = 0; i < uniqueIndices.Count; i++ )
		{
			queueCreateInfos[i] = new DeviceQueueCreateInfo
			{
				SType = StructureType.DeviceQueueCreateInfo,
				QueueFamilyIndex = i,
				QueueCount = 1,
				PQueuePriorities = &queuePriority
			};
		}

		fixed( PhysicalDeviceFeatures* featuresPtr = &features )
		{
			var deviceCreateInfo = new DeviceCreateInfo
			{
				SType = StructureType.DeviceCreateInfo,
				QueueCreateInfoCount = (uint)uniqueIndices.Count,
				PQueueCreateInfos = queueCreateInfos,
				PEnabledFeatures = featuresPtr,
				EnabledExtensionCount = (uint)extensions.Length,
				PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr( extensions )
			};

			if ( enableValidationLayers )
			{
				deviceCreateInfo.EnabledLayerCount = (uint)validationLayers!.Length;
				deviceCreateInfo.PpEnabledLayerNames = (byte**)SilkMarshal.StringArrayToPtr( validationLayers );
			}
			else
				deviceCreateInfo.EnabledLayerCount = 0;

			Apis.Vk.CreateDevice( PhysicalDevice, deviceCreateInfo, null, out var logicalDevice ).Verify();

			var logicalGpu = new LogicalGpu( logicalDevice, this, familyIndices );
			logicalGpus.Add( logicalGpu );
			
			if ( enableValidationLayers )
				SilkMarshal.Free( (nint)deviceCreateInfo.PpEnabledLayerNames );

			return logicalGpu;
		}
	}

	internal unsafe bool SupportsExtensions( params string[] extensions )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( Gpu ) );

		uint extensionCount;
		Apis.Vk.EnumerateDeviceExtensionProperties( PhysicalDevice, string.Empty, &extensionCount, null ).Verify();

		var availableExtensions = stackalloc ExtensionProperties[(int)extensionCount];
		Apis.Vk.EnumerateDeviceExtensionProperties( PhysicalDevice, string.Empty, &extensionCount, availableExtensions ).Verify();

		var matches = 0;
		for ( var i = 0; i < extensionCount; i++ )
		{
			var extension = availableExtensions[ i ];
			if ( extensions.Contains( Marshal.PtrToStringAnsi( (nint)extension.ExtensionName ) ) )
				matches++;
		}

		return matches >= extensions.Length;
	}

	internal FormatProperties GetFormatProperties( Format format )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( Gpu ) );

		return Apis.Vk.GetPhysicalDeviceFormatProperties( PhysicalDevice, format );
	}

	internal unsafe QueueFamilyIndices GetQueueFamilyIndices( bool requireUnique = false )
	{
		if ( Disposed )
			throw new ObjectDisposedException( nameof( Gpu ) );

		var indices = new QueueFamilyIndices();

		uint queueFamilyCount;
		Apis.Vk.GetPhysicalDeviceQueueFamilyProperties( PhysicalDevice, &queueFamilyCount, null );

		var queueFamilies = stackalloc QueueFamilyProperties[(int)queueFamilyCount];
		Apis.Vk.GetPhysicalDeviceQueueFamilyProperties( PhysicalDevice, &queueFamilyCount, queueFamilies );

		for ( uint i = 0; i < queueFamilyCount; i++ )
		{
			if ( queueFamilies[i].QueueFlags.HasFlag( QueueFlags.GraphicsBit ) )
				indices.GraphicsFamily = i;

			Instance!.SurfaceExtension.GetPhysicalDeviceSurfaceSupport( PhysicalDevice, i, Instance.Surface, out var presentSupported );
			if ( presentSupported && ((requireUnique && indices.GraphicsFamily != i) || !requireUnique) )
				indices.PresentFamily = i;

			if ( indices.IsComplete() )
				break;
		}

		return indices;
	}

	private unsafe SwapchainSupportDetails GetSwapchainSupport()
	{
		var details = new SwapchainSupportDetails();

		var surfaceExtension = Instance!.SurfaceExtension;
		var surface = Instance.Surface;
		surfaceExtension.GetPhysicalDeviceSurfaceCapabilities( PhysicalDevice, surface, out var capabilities ).Verify();
		details.Capabilities = capabilities;

		uint formatCount;
		surfaceExtension.GetPhysicalDeviceSurfaceFormats( PhysicalDevice, surface, &formatCount, null ).Verify();

		var formats = new SurfaceFormatKHR[formatCount];
		fixed ( SurfaceFormatKHR* formatsPtr = formats )
			surfaceExtension.GetPhysicalDeviceSurfaceFormats( PhysicalDevice, surface, &formatCount, formatsPtr ).Verify();
		details.Formats = formats;

		uint presentModeCount;
		surfaceExtension.GetPhysicalDeviceSurfacePresentModes( PhysicalDevice, surface, &presentModeCount, null ).Verify();

		var presentModes = new PresentModeKHR[presentModeCount];
		fixed ( PresentModeKHR* presentModesPtr = presentModes )
			surfaceExtension.GetPhysicalDeviceSurfacePresentModes( PhysicalDevice, surface, &presentModeCount, presentModesPtr ).Verify();
		details.PresentModes = presentModes;

		return details;
	}

	public static implicit operator PhysicalDevice( Gpu gpu )
	{
		if ( gpu.Disposed )
			throw new ObjectDisposedException( nameof( Gpu ) );

		return gpu.PhysicalDevice;
	}
}

using Latte.NewRenderer.Exceptions;
using Latte.NewRenderer.Extensions;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using System;
using System.Runtime.InteropServices;

namespace Latte.NewRenderer.Builders;

internal unsafe sealed class VkLogicalDeviceBuilder : IDisposable
{
	private readonly PhysicalDevice physicalDevice;

	private SurfaceKHR surface;
	private KhrSurface? surfaceExtension;
	private PhysicalDeviceFeatures featuresRequired;
	private VkQueueFamilyIndices queueFamilyIndices;
	private string[] extensions = [];
	private nint pNextPtr;
	private bool disposed;

	internal VkLogicalDeviceBuilder( PhysicalDevice physicalDevice )
	{
		VkInvalidHandleException.ThrowIfInvalid( physicalDevice );

		this.physicalDevice = physicalDevice;
	}

	~VkLogicalDeviceBuilder()
	{
		Dispose( disposing: false );
	}

	internal VkLogicalDeviceBuilder WithSurface( SurfaceKHR surface, KhrSurface? surfaceExtension )
	{
		VkInvalidHandleException.ThrowIfInvalid( surface );

		this.surface = surface;
		this.surfaceExtension = surfaceExtension;
		return this;
	}

	internal VkLogicalDeviceBuilder WithFeatures( PhysicalDeviceFeatures features )
	{
		featuresRequired = features;
		return this;
	}

	internal VkLogicalDeviceBuilder WithQueueFamilyIndices( VkQueueFamilyIndices queueFamilyIndices )
	{
		this.queueFamilyIndices = queueFamilyIndices;
		return this;
	}

	internal VkLogicalDeviceBuilder WithExtensions( params string[] extensions )
	{
		this.extensions = extensions;
		return this;
	}

	internal VkLogicalDeviceBuilder WithPNext<T>( T pNext ) where T : unmanaged
	{
		pNextPtr = Marshal.AllocHGlobal( sizeof( T ) );
		Marshal.StructureToPtr( pNext, pNextPtr, false );
		return this;
	}

	internal VkLogicalDeviceBuilderResult Build()
	{
		var queuePriority = 1f;
		var uniqueIndices = queueFamilyIndices.UniqueQueues.Span;

		var queueCreateInfos = stackalloc DeviceQueueCreateInfo[uniqueIndices.Length];
		for ( var i = 0; i < uniqueIndices.Length; i++ )
		{
			queueCreateInfos[i] = new DeviceQueueCreateInfo
			{
				SType = StructureType.DeviceQueueCreateInfo,
				QueueFamilyIndex = uniqueIndices[i],
				QueueCount = 1,
				PQueuePriorities = &queuePriority
			};
		}

		fixed ( PhysicalDeviceFeatures* featuresRequiredPtr = &featuresRequired )
		{
			var createInfo = new DeviceCreateInfo
			{
				SType = StructureType.DeviceCreateInfo,
				QueueCreateInfoCount = (uint)uniqueIndices.Length,
				PQueueCreateInfos = queueCreateInfos,
				PEnabledFeatures = featuresRequiredPtr,
				EnabledExtensionCount = (uint)extensions.Length,
				PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr( extensions ),
				PNext = (void*)pNextPtr
			};

			Apis.Vk.CreateDevice( physicalDevice, createInfo, null, out var logicalDevice ).Verify();
			SilkMarshal.Free( (nint)createInfo.PpEnabledExtensionNames );

			var graphicsQueue = Apis.Vk.GetDeviceQueue( logicalDevice, queueFamilyIndices.GraphicsQueue, 0 );
			var presentQueue = Apis.Vk.GetDeviceQueue( logicalDevice, queueFamilyIndices.PresentQueue, 0 );
			var transferQueue = Apis.Vk.GetDeviceQueue( logicalDevice, queueFamilyIndices.TransferQueue, 0 );

			return new VkLogicalDeviceBuilderResult( logicalDevice, graphicsQueue, queueFamilyIndices.GraphicsQueue,
				presentQueue, queueFamilyIndices.PresentQueue, transferQueue, queueFamilyIndices.TransferQueue );
		}
	}

	private void Dispose( bool disposing )
	{
		if ( disposed )
			return;

		if ( disposing )
		{
		}

		Marshal.FreeHGlobal( pNextPtr );
		disposed = true;
	}

	public void Dispose()
	{
		Dispose( disposing: true );
		GC.SuppressFinalize( this );
	}
}

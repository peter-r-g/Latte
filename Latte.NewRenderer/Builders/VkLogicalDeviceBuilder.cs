using Latte.NewRenderer.Extensions;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Latte.NewRenderer.Builders;

// TODO: Add builder methods.
internal sealed class VkLogicalDeviceBuilder
{
	private PhysicalDevice physicalDevice;
	private SurfaceKHR surface;
	private KhrSurface? surfaceExtension;
	private PhysicalDeviceFeatures featuresRequired;
	private string[] extensions = [];
	private bool uniqueGraphicsQueueRequired;
	private bool uniquePresentQueueRequired;

	private VkLogicalDeviceBuilder( PhysicalDevice physicalDevice, SurfaceKHR surface,
		KhrSurface? surfaceExtension, PhysicalDeviceFeatures featuresRequired,
		bool uniqueGraphicsQueueRequired, bool uniquePresentQueueRequired )
	{
		this.physicalDevice = physicalDevice;
		this.surface = surface;
		this.surfaceExtension = surfaceExtension;
		this.featuresRequired = featuresRequired;
		this.uniqueGraphicsQueueRequired = uniqueGraphicsQueueRequired;
		this.uniquePresentQueueRequired = uniquePresentQueueRequired;
	}

	internal VkLogicalDeviceBuilder WithExtensions( params string[] extensions )
	{
		this.extensions = extensions;
		return this;
	}

	internal unsafe VkLogicalDeviceBuilderResult Build()
	{
		var queuePriority = 1f;
		var indices = VkQueueFamilyIndices.Get( physicalDevice, surface, surfaceExtension, uniqueGraphicsQueueRequired, uniquePresentQueueRequired );
		var uniqueIndices = indices.ToUnique();

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

		fixed( PhysicalDeviceFeatures* featuresRequiredPtr = &featuresRequired )
		{
			var createInfo = new DeviceCreateInfo
			{
				SType = StructureType.DeviceCreateInfo,
				QueueCreateInfoCount = (uint)uniqueIndices.Length,
				PQueueCreateInfos = queueCreateInfos,
				PEnabledFeatures = featuresRequiredPtr,
				EnabledExtensionCount = (uint)extensions.Length,
				PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr( extensions )
			};

			Apis.Vk.CreateDevice( physicalDevice, createInfo, null, out var logicalDevice ).Verify();
			SilkMarshal.Free( (nint)createInfo.PpEnabledExtensionNames );

			var graphicsQueue = Apis.Vk.GetDeviceQueue( logicalDevice, indices.GraphicsQueue, 0 );
			var presentQueue = Apis.Vk.GetDeviceQueue( logicalDevice, indices.PresentQueue, 0 );

			return new VkLogicalDeviceBuilderResult( logicalDevice, graphicsQueue, indices.GraphicsQueue, presentQueue, indices.PresentQueue );
		}
	}

	internal static VkLogicalDeviceBuilder FromPhysicalSelector( PhysicalDevice physicalDevice, VkPhysicalDeviceSelector selector )
	{
		return new VkLogicalDeviceBuilder( physicalDevice, selector.Surface,
			selector.SurfaceExtension, selector.FeaturesRequired,
			selector.UniqueGraphicsQueueRequired, selector.UniquePresentQueueRequired );
	}
}

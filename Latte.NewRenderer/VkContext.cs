using Latte.NewRenderer.Allocations;
using Latte.NewRenderer.Builders;
using Latte.NewRenderer.Exceptions;
using Latte.NewRenderer.Extensions;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Latte.NewRenderer;

internal static unsafe class VkContext
{
	[MemberNotNullWhen( true, nameof( AllocationManager ), nameof( DisposalManager ), nameof( Extensions ) )]
	internal static bool IsInitialized { get; private set; }

	internal static Instance Instance { get; private set; }
	internal static PhysicalDevice PhysicalDevice { get; private set; }
	internal static PhysicalDeviceInfo PhysicalDeviceInfo { get; private set; }
	internal static Device LogicalDevice { get; private set; }
	internal static VkQueueFamilyIndices QueueFamilyIndices { get; private set; }

	internal static Queue GraphicsQueue { get; private set; }
	internal static Queue PresentQueue { get; private set; }
	internal static Queue TransferQueue { get; private set; }

	internal static DebugUtilsMessengerEXT DebugMessenger { get; private set; }

	internal static AllocationManager? AllocationManager { get; private set; }
	internal static DisposalManager? DisposalManager { get; private set; }
	internal static ExtensionContainer? Extensions { get; private set; }

	private static readonly string[] DefaultInstanceExtensions = [
		ExtDebugUtils.ExtensionName,
		KhrSurface.ExtensionName
	];

	private static readonly string[] DefaultDeviceExtensions = [
		KhrSwapchain.ExtensionName
	];

	// FIXME: Is there a way to initialize global state without a view?
	internal static unsafe SurfaceKHR Initialize( IView view ) => Initialize( view, DefaultInstanceExtensions, DefaultDeviceExtensions );

	internal static unsafe SurfaceKHR Initialize( IView view, string[] instanceExtensions, string[] deviceExtensions )
	{
		if ( IsInitialized )
			return view.VkSurface!.Create<AllocationCallbacks>( Instance.ToHandle(), null ).ToSurface();

		var instanceBuilderResult = new VkInstanceBuilder()
			.WithName( "Latte" )
			.WithView( view )
			.WithExtensions( instanceExtensions )
			.RequireVulkanVersion( 1, 1, 0 )
			.UseDefaultDebugMessenger()
			.Build();

		Instance = instanceBuilderResult.Instance;
		DebugMessenger = instanceBuilderResult.DebugMessenger;
		var debugUtilsExtension = instanceBuilderResult.DebugUtilsExtension;

		VkInvalidHandleException.ThrowIfInvalid( Instance );

		if ( !Apis.Vk.TryGetInstanceExtension<KhrSurface>( Instance, out var surfaceExtension ) )
			throw new VkException( $"Failed to get the {KhrSurface.ExtensionName} extension" );

		var surface = view.VkSurface!.Create<AllocationCallbacks>( Instance.ToHandle(), null ).ToSurface();

		var physicalDeviceSelectorResult = new VkPhysicalDeviceSelector( Instance )
			.RequireDiscreteDevice( true )
			.RequireVersion( 1, 1, 0 )
			.WithSurface( surface, surfaceExtension )
			.RequireUniqueGraphicsQueue( true )
			.RequireUniquePresentQueue( true )
			.RequireUniqueTransferQueue( true )
			.Select();

		PhysicalDevice = physicalDeviceSelectorResult.PhysicalDevice;
		PhysicalDeviceInfo = new PhysicalDeviceInfo( PhysicalDevice );
		QueueFamilyIndices = physicalDeviceSelectorResult.QueueFamilyIndices;

		VkInvalidHandleException.ThrowIfInvalid( PhysicalDevice );

		var logicalDeviceBuilderResult = new VkLogicalDeviceBuilder( PhysicalDevice )
			.WithSurface( surface, surfaceExtension )
			.WithQueueFamilyIndices( QueueFamilyIndices )
			.WithExtensions( deviceExtensions )
			.WithFeatures( new PhysicalDeviceFeatures
			{
				FillModeNonSolid = Vk.True,
				PipelineStatisticsQuery = Vk.True
			} )
			.WithPNext( new PhysicalDeviceShaderDrawParametersFeatures
			{
				SType = StructureType.PhysicalDeviceShaderDrawParametersFeatures,
				PNext = null,
				ShaderDrawParameters = Vk.True
			} )
			.Build();

		LogicalDevice = logicalDeviceBuilderResult.LogicalDevice;
		GraphicsQueue = logicalDeviceBuilderResult.GraphicsQueue;
		PresentQueue = logicalDeviceBuilderResult.PresentQueue;
		TransferQueue = logicalDeviceBuilderResult.TransferQueue;

		VkInvalidHandleException.ThrowIfInvalid( LogicalDevice );
		VkInvalidHandleException.ThrowIfInvalid( GraphicsQueue );
		VkInvalidHandleException.ThrowIfInvalid( PresentQueue );
		VkInvalidHandleException.ThrowIfInvalid( TransferQueue );

		AllocationManager = new AllocationManager();
		DisposalManager = new DisposalManager();
		Extensions = new ExtensionContainer( instanceExtensions, deviceExtensions );

		DisposalManager.Add( () => Apis.Vk.DestroyInstance( Instance, null ) );
		if ( DebugMessenger.IsValid() )
			DisposalManager.Add( () => debugUtilsExtension?.DestroyDebugUtilsMessenger( Instance, DebugMessenger, null ) );
		DisposalManager.Add( () => Apis.Vk.DestroyDevice( LogicalDevice, null ) );

		IsInitialized = true;
		return surface;
	}

	internal static void Cleanup()
	{
		if ( !IsInitialized )
			return;

		IsInitialized = false;
	}
}

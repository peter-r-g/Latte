﻿using Latte.NewRenderer.Allocations;
using Latte.NewRenderer.Builders;
using Latte.NewRenderer.Exceptions;
using Latte.NewRenderer.Extensions;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using System;
using System.Diagnostics.CodeAnalysis;
using Monitor = System.Threading.Monitor;

namespace Latte.NewRenderer;

internal static unsafe class VkContext
{
	[MemberNotNullWhen( true, nameof( GraphicsQueue ), nameof( PresentQueue ), nameof( TransferQueue ),
		nameof( AllocationManager ), nameof( Extensions ), nameof( disposalManager ) )]
	internal static bool IsInitialized { get; private set; }

	internal static Instance Instance { get; private set; }
	internal static PhysicalDevice PhysicalDevice { get; private set; }
	internal static PhysicalDeviceInfo PhysicalDeviceInfo { get; private set; }
	internal static Device LogicalDevice { get; private set; }
	internal static VkQueueFamilyIndices QueueFamilyIndices { get; private set; }

	internal static VkQueue? GraphicsQueue { get; private set; }
	internal static VkQueue? PresentQueue { get; private set; }
	internal static VkQueue? TransferQueue { get; private set; }

	internal static DebugUtilsMessengerEXT DebugMessenger { get; private set; }

	internal static AllocationManager? AllocationManager { get; private set; }
	internal static ExtensionContainer? Extensions { get; private set; }

	private static DisposalManager? disposalManager;
	private static readonly object initializeLock = new();

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
		Monitor.Enter( initializeLock );
		try
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
			// TODO: Merge queues if same queue.
			GraphicsQueue = new VkQueue( logicalDeviceBuilderResult.GraphicsQueue, QueueFamilyIndices.GraphicsQueue );
			PresentQueue = new VkQueue( logicalDeviceBuilderResult.PresentQueue, QueueFamilyIndices.PresentQueue );
			TransferQueue = new VkQueue( logicalDeviceBuilderResult.TransferQueue, QueueFamilyIndices.TransferQueue );

			VkInvalidHandleException.ThrowIfInvalid( LogicalDevice );
			VkInvalidHandleException.ThrowIfInvalid( GraphicsQueue.Queue );
			VkInvalidHandleException.ThrowIfInvalid( PresentQueue.Queue );
			VkInvalidHandleException.ThrowIfInvalid( TransferQueue.Queue );

			AllocationManager = new AllocationManager();
			disposalManager = new DisposalManager();
			Extensions = new ExtensionContainer( instanceExtensions, deviceExtensions );

			disposalManager.Add( () => Apis.Vk.DestroyInstance( Instance, null ) );
			if ( DebugMessenger.IsValid() )
				disposalManager.Add( () => debugUtilsExtension?.DestroyDebugUtilsMessenger( Instance, DebugMessenger, null ) );
			disposalManager.Add( () => Apis.Vk.DestroyDevice( LogicalDevice, null ) );

			AppDomain.CurrentDomain.ProcessExit += Cleanup;
			IsInitialized = true;
			return surface;
		}
		finally
		{
			Monitor.Exit( initializeLock );
		}
	}

	private static void Cleanup()
	{
		if ( !IsInitialized )
			return;

		AppDomain.CurrentDomain.ProcessExit -= Cleanup;

		GraphicsQueue.Dispose();
		PresentQueue.Dispose();
		TransferQueue.Dispose();
		AllocationManager.Dispose();
		disposalManager.Dispose();
		Extensions.Dispose();

		IsInitialized = false;
	}

	private static void Cleanup( object? sender, EventArgs e ) => Cleanup();
}
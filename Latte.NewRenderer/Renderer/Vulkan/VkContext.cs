﻿using Latte.Windowing.Renderer.Vulkan.Builders;
using Latte.Windowing.Renderer.Vulkan.Exceptions;
using Latte.Windowing.Renderer.Vulkan.Extensions;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;
using VMASharp;
using Monitor = System.Threading.Monitor;

namespace Latte.Windowing.Renderer.Vulkan;

internal static unsafe class VkContext
{
	[MemberNotNullWhen( true, nameof( GraphicsQueue ), nameof( PresentQueue ), nameof( TransferQueue ),
		nameof( AllocationManager ), nameof( Extensions ), nameof( disposalManager ) )]
	internal static bool IsInitialized { get; private set; }

	internal static Instance Instance { get; private set; }
	internal static PhysicalDevice PhysicalDevice { get; private set; }
	internal static VkPhysicalDeviceInfo PhysicalDeviceInfo { get; private set; }
	internal static Device LogicalDevice { get; private set; }
	internal static VkQueueFamilyIndices QueueFamilyIndices { get; private set; }

	internal static VkQueue? GraphicsQueue { get; private set; }
	internal static VkQueue? PresentQueue { get; private set; }
	internal static VkQueue? TransferQueue { get; private set; }

	internal static DebugUtilsMessengerEXT DebugMessenger { get; private set; }

	internal static VulkanMemoryAllocator? AllocationManager { get; private set; }
	internal static VkExtensionContainer? Extensions { get; private set; }

	private static ExtDebugUtils? debugUtilsExtension;
	private static DisposalManager? disposalManager;
	private static readonly object initializeLock = new();

	private static readonly string[] DefaultInstanceExtensions = [
		KhrSurface.ExtensionName
	];

	private static readonly string[] DefaultOptionalInstanceExtensions = [
#if DEBUG
		ExtDebugUtils.ExtensionName
#endif
	];

	private static readonly string[] DefaultDeviceExtensions = [
		KhrSwapchain.ExtensionName
	];

	private static readonly string[] DefaultOptionalDeviceExtensions = [
		"VK_EXT_memory_priority",
		ExtPageableDeviceLocalMemory.ExtensionName
	];

	// FIXME: Is there a way to initialize global state without a view?
	internal static unsafe SurfaceKHR Initialize( IView view )
		=> Initialize( view, DefaultInstanceExtensions, DefaultDeviceExtensions, DefaultOptionalInstanceExtensions, DefaultOptionalDeviceExtensions );

	internal static unsafe SurfaceKHR Initialize( IView view, string[] instanceExtensions, string[] deviceExtensions,
		string[] optionalInstanceExtensions, string[] optionalDeviceExtensions )
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
				.WithOptionalExtensions( optionalInstanceExtensions )
				.RequireVulkanVersion( 1, 1, 0 )
				.UseDefaultDebugMessenger()
				.Build();

			Instance = instanceBuilderResult.Instance;
			DebugMessenger = instanceBuilderResult.DebugMessenger;
			debugUtilsExtension = instanceBuilderResult.DebugUtilsExtension;

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
			PhysicalDeviceInfo = new VkPhysicalDeviceInfo( PhysicalDevice );
			QueueFamilyIndices = physicalDeviceSelectorResult.QueueFamilyIndices;

			VkInvalidHandleException.ThrowIfInvalid( PhysicalDevice );

			var logicalDeviceBuilderResult = new VkLogicalDeviceBuilder( PhysicalDevice )
				.WithSurface( surface, surfaceExtension )
				.WithQueueFamilyIndices( QueueFamilyIndices )
				.WithExtensions( deviceExtensions )
				.WithOptionalExtensions( optionalDeviceExtensions )
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

			var allocatorCreateInfo = new VulkanMemoryAllocatorCreateInfo
			{
				VulkanAPIObject = Apis.Vk,
				VulkanAPIVersion = new Version32( 1, 1, 0 ),
				Instance = Instance,
				PhysicalDevice = PhysicalDevice,
				LogicalDevice = LogicalDevice,
				FrameInUseCount = VkEngine.MaxFramesInFlight,
				Flags = 0
			};

			AllocationManager = new VulkanMemoryAllocator( allocatorCreateInfo );
			disposalManager = new DisposalManager();
			Extensions = new VkExtensionContainer( instanceExtensions, deviceExtensions );

			disposalManager.Add( () => Apis.Vk.DestroyInstance( Instance, null ) );
			if ( DebugMessenger.IsValid() )
				disposalManager.Add( () => debugUtilsExtension?.DestroyDebugUtilsMessenger( Instance, DebugMessenger, null ) );
			// FIXME: Sometimes the program gets stuck destroying the device.
			disposalManager.Add( () => Apis.Vk.DestroyDevice( LogicalDevice, null ) );

			AppDomain.CurrentDomain.ProcessExit += Cleanup;
			IsInitialized = true;

			SetObjectName( PhysicalDevice.Handle, ObjectType.PhysicalDevice, PhysicalDeviceInfo.Name );
			SetObjectName( LogicalDevice.Handle, ObjectType.Device, $"{PhysicalDeviceInfo.Name} (Logical)" );
			SetObjectName( GraphicsQueue.Queue.Handle, ObjectType.Queue, $"Graphics Queue (Family: {GraphicsQueue.QueueFamily})" );
			SetObjectName( PresentQueue.Queue.Handle, ObjectType.Queue, $"Present Queue (Family: {PresentQueue.QueueFamily})" );
			SetObjectName( TransferQueue.Queue.Handle, ObjectType.Queue, $"Transfer Queue (Family: {TransferQueue.QueueFamily})" );
			return surface;
		}
		finally
		{
			Monitor.Exit( initializeLock );
		}
	}

	internal static IEnumerable<VkQueue> GetAllQueues()
	{
		if ( !IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		yield return GraphicsQueue;
		yield return PresentQueue;
		yield return TransferQueue;
	}

	internal static void SetObjectName( nint objectHandle, ObjectType type, string name ) => SetObjectName( (ulong)objectHandle, type, name );
	internal static void SetObjectName( ulong objectHandle, ObjectType type, string name )
	{
#if DEBUG
		if ( !IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		if ( debugUtilsExtension is null )
			return;

		ReadOnlySpan<byte> nameBytes = Encoding.ASCII.GetBytes( name );
		debugUtilsExtension.SetDebugUtilsObjectName( LogicalDevice, VkInfo.DebugObjectName( objectHandle, type, nameBytes ) ).AssertSuccess();
#endif
	}

	internal static void StartDebugLabel( CommandBuffer cmd, string label, Vector4 color )
	{
#if DEBUG
		if ( !IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		if ( debugUtilsExtension is null )
			return;

		ReadOnlySpan<float> colorSpan = stackalloc float[]
		{
			color.X,
			color.Y,
			color.Z,
			color.W
		};

		var labelBytes = Encoding.ASCII.GetBytes( label );
		debugUtilsExtension.CmdBeginDebugUtilsLabel( cmd, VkInfo.DebugLabel( labelBytes, colorSpan ) );
#endif
	}

	internal static void EndDebugLabel( CommandBuffer cmd )
	{
#if DEBUG
		if ( !IsInitialized )
			throw new VkException( $"{nameof( VkContext )} has not been initialized" );

		if ( debugUtilsExtension is null )
			return;

		debugUtilsExtension.CmdEndDebugUtilsLabel( cmd );
#endif
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
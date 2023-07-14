using Silk.NET.Core.Native;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using System.Runtime.InteropServices;
using System;
using Silk.NET.Windowing;
using System.Collections.Generic;
using Latte.Windowing.Extensions;

namespace Latte.Windowing.Backend.Vulkan;

internal unsafe sealed class VulkanInstance : VulkanWrapper
{
	internal IWindow Window { get; }
	internal new Instance Instance { get; }
	internal bool ValidationLayersEnabled { get; }

	internal ExtDebugUtils? DebugUtilsExtension { get; } = null!;
	internal DebugUtilsMessengerEXT DebugMessenger { get; }

	internal KhrSurface SurfaceExtension { get; } = null!;
	internal SurfaceKHR Surface { get; }

	internal VulkanInstance( IWindow window, bool enableValidationLayers, string[]? validationLayers = null )
	{
		ValidationLayersEnabled = enableValidationLayers;
		if ( enableValidationLayers && (validationLayers is null || validationLayers.Length == 0) )
			throw new ArgumentException( "No validation layers were passed", nameof(validationLayers) );

		if ( enableValidationLayers && !CheckValidationLayerSupport( validationLayers! ) )
			throw new ApplicationException( "Failed to find all requested Vulkan validation layers" );

		Window = window;

		var appInfo = new ApplicationInfo()
		{
			SType = StructureType.ApplicationInfo,
			PApplicationName = (byte*)Marshal.StringToHGlobalAnsi( "Latte Game" ),
			ApplicationVersion = new Version32( 1, 0, 0 ),
			PEngineName = (byte*)Marshal.StringToHGlobalAnsi( "Latte" ),
			EngineVersion = new Version32( 1, 0, 0 ),
			ApiVersion = Vk.Version13
		};

		var createInfo = new InstanceCreateInfo()
		{
			SType = StructureType.InstanceCreateInfo,
			PApplicationInfo = &appInfo
		};

		var extensions = GetRequiredExtensions( window, enableValidationLayers );
		createInfo.EnabledExtensionCount = (uint)extensions.Length;
		createInfo.PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr( extensions );

		if ( enableValidationLayers )
		{
			createInfo.EnabledLayerCount = (uint)validationLayers!.Length;
			createInfo.PpEnabledLayerNames = (byte**)SilkMarshal.StringArrayToPtr( validationLayers );

			var debugCreateInfo = new DebugUtilsMessengerCreateInfoEXT();
			PopulateDebugMessengerCreateInfo( ref debugCreateInfo );
			createInfo.PNext = &debugCreateInfo;
		}
		else
			createInfo.EnabledLayerCount = 0;

		Apis.Vk.CreateInstance( createInfo, null, out var instance ).Verify();
		Instance = instance;

		if ( enableValidationLayers && Apis.Vk.TryGetInstanceExtension<ExtDebugUtils>( Instance, out var debugUtilsExtension ) )
		{
			DebugUtilsExtension = debugUtilsExtension;

			var debugCreateInfo = new DebugUtilsMessengerCreateInfoEXT();
			PopulateDebugMessengerCreateInfo( ref debugCreateInfo );

			debugUtilsExtension.CreateDebugUtilsMessenger( instance, &debugCreateInfo, null, out var debugMessenger ).Verify();
			DebugMessenger = debugMessenger;
		}

		if ( !Apis.Vk.TryGetInstanceExtension<KhrSurface>( Instance, out var surfaceExtension ) )
			throw new ApplicationException( "Failed to get KHR_surface extension" );

		SurfaceExtension = surfaceExtension;
		Surface = window.VkSurface!.Create<AllocationCallbacks>( Instance.ToHandle(), null ).ToSurface();

		Marshal.FreeHGlobal( (nint)appInfo.PApplicationName );
		Marshal.FreeHGlobal( (nint)appInfo.PEngineName );
		SilkMarshal.Free( (nint)createInfo.PpEnabledExtensionNames );
		if ( enableValidationLayers )
			SilkMarshal.Free( (nint)createInfo.PpEnabledLayerNames );
	}

	public override void Dispose()
	{
		if ( Disposed )
			return;

		if ( ValidationLayersEnabled && DebugUtilsExtension is not null )
			DebugUtilsExtension.DestroyDebugUtilsMessenger( Instance, DebugMessenger, null );

		SurfaceExtension.DestroySurface( Instance, Surface, null );
		Apis.Vk.DestroyInstance( Instance, null );

		GC.SuppressFinalize( this );
		Disposed = true;
	}

	private bool CheckValidationLayerSupport( IEnumerable<string> validationLayers )
	{
		uint layerCount = 0;
		Apis.Vk.EnumerateInstanceLayerProperties( &layerCount, null ).Verify();

		var availableLayers = new LayerProperties[layerCount];
		fixed ( LayerProperties* availableLayersPtr = availableLayers )
			Apis.Vk.EnumerateInstanceLayerProperties( &layerCount, availableLayersPtr ).Verify();

		foreach ( var layerName in validationLayers )
		{
			var layerFound = false;

			foreach ( var availableLayer in availableLayers )
			{
				var availableLayerName = Marshal.PtrToStringAnsi( (nint)availableLayer.LayerName );
				if ( availableLayerName != layerName )
					continue;

				layerFound = true;
				break;
			}

			if ( layerFound )
				continue;

			if ( Loggers.Vulkan.IsEnabled( Logging.LogLevel.Error ) )
				Loggers.Vulkan.Error( $"Failed to find Vulkan validation layer \"{layerName}\"" );
			return false;
		}

		return true;
	}

	private static string[] GetRequiredExtensions( IWindow window, bool enableValidationLayers )
	{
		if ( window.VkSurface is null )
			throw new ApplicationException( "Window platform does not support Vulkan" );

		var extensions = window.VkSurface.GetRequiredExtensions( out var requiredExtensionCount );
		var extensionCount = requiredExtensionCount;
		if ( enableValidationLayers )
			extensionCount++;

		var requiredExtensions = new string[extensionCount];
		SilkMarshal.CopyPtrToStringArray( (nint)extensions, requiredExtensions );

		if ( enableValidationLayers )
			requiredExtensions[^1] = ExtDebugUtils.ExtensionName;

		return requiredExtensions;
	}

	private static void PopulateDebugMessengerCreateInfo( ref DebugUtilsMessengerCreateInfoEXT debugCreateInfo )
	{
		debugCreateInfo.SType = StructureType.DebugUtilsMessengerCreateInfoExt;
		debugCreateInfo.MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt
			| DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt
			| DebugUtilsMessageSeverityFlagsEXT.WarningBitExt
			| DebugUtilsMessageSeverityFlagsEXT.InfoBitExt;
		debugCreateInfo.MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
			DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt |
			DebugUtilsMessageTypeFlagsEXT.ValidationBitExt;
		debugCreateInfo.PfnUserCallback = (PfnDebugUtilsMessengerCallbackEXT)DebugCallback;
	}

	private static uint DebugCallback( DebugUtilsMessageSeverityFlagsEXT messageSeverity,
		DebugUtilsMessageTypeFlagsEXT messageTypes,
		DebugUtilsMessengerCallbackDataEXT* pCallbackData,
		void* pUserData )
	{
		var message = Marshal.PtrToStringAnsi( (nint)pCallbackData->PMessage );
		if ( message is null )
			return Vk.False;

		switch ( messageSeverity )
		{
			case DebugUtilsMessageSeverityFlagsEXT.None:
			case DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt:
				Loggers.Vulkan.Verbose( message );
				break;
			case DebugUtilsMessageSeverityFlagsEXT.InfoBitExt:
				Loggers.Vulkan.Information( message );
				break;
			case DebugUtilsMessageSeverityFlagsEXT.WarningBitExt:
				Loggers.Vulkan.Warning( message );
				break;
			case DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt:
				Loggers.Vulkan.Error( message );
				break;
		}

		return Vk.False;
	}

	public static implicit operator Instance( VulkanInstance vulkanInstance )
	{
		if ( vulkanInstance.Disposed )
			throw new ObjectDisposedException( nameof( VulkanInstance ) );

		return vulkanInstance.Instance;
	}
}

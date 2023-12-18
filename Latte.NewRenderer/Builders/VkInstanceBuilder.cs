using Latte.NewRenderer.Extensions;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Windowing;
using System;
using System.Runtime.InteropServices;
using Latte.NewRenderer.Exceptions;


#if DEBUG
using System.Diagnostics;
#endif

namespace Latte.NewRenderer.Builders;

internal sealed class VkInstanceBuilder
{
	private string name = "Vulkan Application";
	private Version32 version = new Version( 1, 0, 0 );
	private IView? view;
	private Version32 requiredVersion = new( 1, 0, 0 );
	private bool useDefaultDebugMessenger;

	public VkInstanceBuilder WithName( string name )
	{
		this.name = name;
		return this;
	}

	public VkInstanceBuilder WithVersion( uint major, uint minor, uint patch )
	{
		version = new Version32( major, minor, patch );
		return this;
	}

	public VkInstanceBuilder WithView( IView view )
	{
		this.view = view;
		return this;
	}

	public VkInstanceBuilder RequireVulkanVersion( uint major, uint minor, uint patch )
	{
		requiredVersion = new Version32( major, minor, patch );
		return this;
	}

	public VkInstanceBuilder UseDefaultDebugMessenger()
	{
		useDefaultDebugMessenger = true;
		return this;
	}

	public unsafe VkInstanceBuilderResult Build()
	{
		ArgumentNullException.ThrowIfNull( view, nameof( view ) );

		var appInfo = new ApplicationInfo()
		{
			SType = StructureType.ApplicationInfo,
			PApplicationName = (byte*)Marshal.StringToHGlobalAnsi( name ),
			ApplicationVersion = version,
			PEngineName = (byte*)Marshal.StringToHGlobalAnsi( "Latte" ),
			EngineVersion = new Version32( 1, 0, 0 ),
			ApiVersion = requiredVersion
		};

		var createInfo = new InstanceCreateInfo()
		{
			SType = StructureType.InstanceCreateInfo,
			PApplicationInfo = &appInfo
		};

		var extensions = GetRequiredExtensions( view );
		createInfo.EnabledExtensionCount = (uint)extensions.Length;
		createInfo.PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr( extensions );

		if ( useDefaultDebugMessenger )
		{
			var debugCreateInfo = new DebugUtilsMessengerCreateInfoEXT();
			PopulateDebugMessengerCreateInfo( ref debugCreateInfo );
			createInfo.PNext = &debugCreateInfo;
		}

		Apis.Vk.CreateInstance( createInfo, null, out var instance ).Verify();
		ExtDebugUtils? debugUtilsExtension = null;
		DebugUtilsMessengerEXT debugMessenger = default;

		if ( useDefaultDebugMessenger )
		{
			if ( !Apis.Vk.TryGetInstanceExtension<ExtDebugUtils>( instance, out debugUtilsExtension ) )
				throw new VkException( $"Failed to get the {nameof( ExtDebugUtils )} extension" );

			var debugCreateInfo = new DebugUtilsMessengerCreateInfoEXT();
			PopulateDebugMessengerCreateInfo( ref debugCreateInfo );
			debugUtilsExtension.CreateDebugUtilsMessenger( instance, &debugCreateInfo, null, out debugMessenger ).Verify();
		}

		Marshal.FreeHGlobal( (nint)appInfo.PApplicationName );
		Marshal.FreeHGlobal( (nint)appInfo.PEngineName );
		SilkMarshal.Free( (nint)createInfo.PpEnabledExtensionNames );

		return new VkInstanceBuilderResult( instance, debugUtilsExtension, debugMessenger );
	}

	private static unsafe void PopulateDebugMessengerCreateInfo( ref DebugUtilsMessengerCreateInfoEXT debugCreateInfo )
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

	private static unsafe string[] GetRequiredExtensions( IView view )
	{
		if ( view.VkSurface is null )
			throw new VkException( "Window platform does not support Vulkan" );

		var extensions = view.VkSurface.GetRequiredExtensions( out var requiredExtensionCount );
		var extensionCount = requiredExtensionCount;

		var requiredExtensions = new string[extensionCount];
		SilkMarshal.CopyPtrToStringArray( (nint)extensions, requiredExtensions );

		var finalExtensions = new string[extensionCount + 1];
		for ( var i = 0; i < requiredExtensions.Length; i++ )
			finalExtensions[i] = requiredExtensions[i];
		finalExtensions[^1] = ExtDebugUtils.ExtensionName;

		return finalExtensions;
	}

	private static unsafe uint DebugCallback( DebugUtilsMessageSeverityFlagsEXT messageSeverity,
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
				Console.ForegroundColor = ConsoleColor.Gray;
				Console.WriteLine( message );
				break;
			case DebugUtilsMessageSeverityFlagsEXT.InfoBitExt:
				Console.ForegroundColor = ConsoleColor.DarkGreen;
				Console.WriteLine( message );
				break;
			case DebugUtilsMessageSeverityFlagsEXT.WarningBitExt:
				Console.ForegroundColor = ConsoleColor.DarkYellow;
				Console.WriteLine( message );
				break;
			case DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt:
				Console.ForegroundColor = ConsoleColor.DarkRed;
				Console.WriteLine( message );
#if DEBUG
				Debugger.Break();
#endif
				break;
		}

		return Vk.False;
	}
}

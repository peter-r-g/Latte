using Latte.Windowing.Renderer.Vulkan.Exceptions;
using Latte.Windowing.Renderer.Vulkan.Extensions;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Latte.Windowing.Renderer.Vulkan.Builders;

internal sealed class VkInstanceBuilder
{
	private string name = "Vulkan Application";
	private Version32 version = new Version( 1, 0, 0 );
	private IView? view;
	private HashSet<string> requiredExtensions = [];
	private HashSet<string> optionalExtensions = [];
	private Version32 requiredVersion = new( 1, 0, 0 );
	private bool useDebugMessenger;
	private DebugUtilsMessageSeverityFlagsEXT messageSeverityFlags;
	private DebugUtilsMessageTypeFlagsEXT messageTypeFlags;
	private DebugUtilsMessengerCallbackFunctionEXT? debugMessengerCallback;

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

	public VkInstanceBuilder WithExtensions( params string[] extensions )
	{
		requiredExtensions = extensions.ToHashSet();
		return this;
	}

	public VkInstanceBuilder WithOptionalExtensions( params string[] optionalExtensions )
	{
		this.optionalExtensions = optionalExtensions.ToHashSet();
		return this;
	}

	public VkInstanceBuilder RequireVulkanVersion( uint major, uint minor, uint patch )
	{
		requiredVersion = new Version32( major, minor, patch );
		return this;
	}

	public unsafe VkInstanceBuilder UseDefaultDebugMessenger()
	{
		optionalExtensions.Add( ExtDebugUtils.ExtensionName );
		useDebugMessenger = true;
		messageSeverityFlags = DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt |
			DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt |
			DebugUtilsMessageSeverityFlagsEXT.WarningBitExt |
			DebugUtilsMessageSeverityFlagsEXT.InfoBitExt;
		messageTypeFlags = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
			DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt |
			DebugUtilsMessageTypeFlagsEXT.ValidationBitExt;
		debugMessengerCallback = DefaultDebugMessengerCallback;
		return this;
	}

	public VkInstanceBuilder WithDebugMessenger( DebugUtilsMessageSeverityFlagsEXT messageSeverityFlags,
		DebugUtilsMessageTypeFlagsEXT messageTypeFlags,
		DebugUtilsMessengerCallbackFunctionEXT debugMessengerCallback )
	{
		optionalExtensions.Add( ExtDebugUtils.ExtensionName );
		useDebugMessenger = true;
		this.messageSeverityFlags = messageSeverityFlags;
		this.messageTypeFlags = messageTypeFlags;
		this.debugMessengerCallback = debugMessengerCallback;
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

		var viewExtensions = GetRequiredExtensions( view );
		var requiredExtensions = new HashSet<string>( this.requiredExtensions.Concat( viewExtensions ) ).ToArray();
		if ( !ExtensionsSupported( requiredExtensions, out var unsupportedExtensions ) )
			throw new VkException( $"The following extensions are unsupported by this Vulkan instance: {string.Join( ',', unsupportedExtensions )}" );

		var optionalExtensions = this.optionalExtensions.ToArray();
		ExtensionsSupported( optionalExtensions, out var unsupportedOptionalExtensions );

		var finalExtensions = requiredExtensions.Concat( optionalExtensions )
			.Where( extension => !unsupportedOptionalExtensions.Contains( extension ) )
			.ToHashSet()
			.ToArray();

		createInfo.EnabledExtensionCount = (uint)finalExtensions.Length;
		createInfo.PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr( finalExtensions );

		if ( useDebugMessenger )
		{
			var debugCreateInfo = new DebugUtilsMessengerCreateInfoEXT();
			PopulateDebugMessengerCreateInfo( ref debugCreateInfo );
			createInfo.PNext = &debugCreateInfo;
		}

		Apis.Vk.CreateInstance( createInfo, null, out var instance ).AssertSuccess();
		ExtDebugUtils? debugUtilsExtension = null;
		DebugUtilsMessengerEXT debugMessenger = default;

		if ( useDebugMessenger )
		{
			if ( !Apis.Vk.TryGetInstanceExtension<ExtDebugUtils>( instance, out debugUtilsExtension ) )
				throw new VkException( $"Failed to get the {nameof( ExtDebugUtils )} extension" );

			var debugCreateInfo = new DebugUtilsMessengerCreateInfoEXT();
			PopulateDebugMessengerCreateInfo( ref debugCreateInfo );
			debugUtilsExtension.CreateDebugUtilsMessenger( instance, &debugCreateInfo, null, out debugMessenger ).AssertSuccess();
		}

		Marshal.FreeHGlobal( (nint)appInfo.PApplicationName );
		Marshal.FreeHGlobal( (nint)appInfo.PEngineName );
		SilkMarshal.Free( (nint)createInfo.PpEnabledExtensionNames );

		return new VkInstanceBuilderResult( instance, debugUtilsExtension, debugMessenger );
	}

	private unsafe void PopulateDebugMessengerCreateInfo( ref DebugUtilsMessengerCreateInfoEXT debugCreateInfo )
	{
		debugCreateInfo.SType = StructureType.DebugUtilsMessengerCreateInfoExt;
		debugCreateInfo.MessageSeverity = messageSeverityFlags;
		debugCreateInfo.MessageType = messageTypeFlags;
		debugCreateInfo.PfnUserCallback = (PfnDebugUtilsMessengerCallbackEXT)debugMessengerCallback;
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

	private static bool ExtensionsSupported( IReadOnlyList<string> extensions, out IReadOnlyList<string> unsupportedExtensions )
	{
		var extensionsSupported = true;
		var unsupportedExtensionsBuilder = new List<string>();

		for ( var i = 0; i < extensions.Count; i++ )
		{
			if ( Apis.Vk.IsInstanceExtensionPresent( extensions[i] ) )
				continue;

			extensionsSupported = false;
			unsupportedExtensionsBuilder.Add( extensions[i] );
		}

		unsupportedExtensions = unsupportedExtensionsBuilder;
		return extensionsSupported;
	}

	private static unsafe uint DefaultDebugMessengerCallback( DebugUtilsMessageSeverityFlagsEXT messageSeverity,
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
				System.Diagnostics.Debugger.Break();
#endif
				break;
		}

		return Vk.False;
	}
}

using Latte.NewRenderer.Exceptions;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;

namespace Latte.NewRenderer;

internal sealed class VkExtensionContainer : IDisposable
{
	private readonly Dictionary<string, NativeExtension<Vk>> extensions = [];
	private readonly Dictionary<Type, string> extensionTypeMap = [];

	private readonly HashSet<string> instanceExtensions;
	private readonly HashSet<string> deviceExtensions;

	private readonly object tryGetLock = new();

	private bool disposed;

	internal VkExtensionContainer( string[] instanceExtensions, string[] deviceExtensions )
	{
		this.instanceExtensions = instanceExtensions.ToHashSet();
		this.deviceExtensions = deviceExtensions.ToHashSet();
	}

	~VkExtensionContainer()
	{
		Dispose( disposing: false );
	}

	internal bool IsInstanceExtensionEnabled( string instanceExtension ) => instanceExtensions.Contains( instanceExtension );
	internal bool IsDeviceExtensionEnabled( string deviceExtension ) => deviceExtensions.Contains( deviceExtension );
	internal bool IsExtensionEnabled( string extension ) => IsInstanceExtensionEnabled( extension ) || IsDeviceExtensionEnabled( extension );

	internal bool TryGetExtension<T>( [NotNullWhen( true )] out T? extension ) where T : NativeExtension<Vk>
	{
		Monitor.Enter( tryGetLock );
		try
		{
			ObjectDisposedException.ThrowIf( disposed, this );

			var tType = typeof( T );
			if ( extensionTypeMap.TryGetValue( tType, out var extensionKey ) )
			{
				extension = (T)extensions[extensionKey];
				return true;
			}

			var extensionNameField = tType.GetField( nameof( ExtDebugUtils.ExtensionName ) );
			if ( extensionNameField is null )
				throw new VkException( $"Failed to get {nameof( ExtDebugUtils.ExtensionName )} field from {tType}" );

			var extensionNameObj = extensionNameField.GetValue( null );
			if ( extensionNameObj is not string extensionName )
				throw new VkException( $"Failed to get extension name from {nameof( ExtDebugUtils.ExtensionName )} field on {tType}" );

			var foundExtension = Apis.Vk.TryGetInstanceExtension( VkContext.Instance, out extension );
			if ( foundExtension && extension is not null )
			{
				extensionTypeMap.Add( tType, extensionName );
				extensions.Add( extensionName, extension );
				return true;
			}

			foundExtension = Apis.Vk.TryGetDeviceExtension( VkContext.Instance, VkContext.LogicalDevice, out extension );
			if ( foundExtension && extension is not null )
			{
				extensionTypeMap.Add( tType, extensionName );
				extensions.Add( extensionName, extension );
				return true;
			}

			extension = default;
			return false;
		}
		finally
		{
			Monitor.Exit( tryGetLock );
		}
	}

	private void Dispose( bool disposing )
	{
		if ( disposed )
			return;

		if ( disposing )
		{
		}

		foreach ( var (_, extension) in extensions )
			extension.Dispose();

		disposed = true;
	}

	public void Dispose()
	{
		Dispose( disposing: true );
		GC.SuppressFinalize( this );
	}
}

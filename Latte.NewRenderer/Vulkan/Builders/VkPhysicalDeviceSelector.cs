using Latte.NewRenderer.Vulkan.Exceptions;
using Latte.NewRenderer.Vulkan.Extensions;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using System.Collections.Immutable;
using System.Linq;

namespace Latte.NewRenderer.Vulkan.Builders;

internal sealed class VkPhysicalDeviceSelector
{
	private readonly Instance instance;

	private Version32 versionRequired = new( 1, 0, 0 );
	private SurfaceKHR surface;
	private KhrSurface? surfaceExtension;
	private bool discreteDeviceRequired;
	private string deviceNameRequired = string.Empty;
	private PhysicalDeviceFeatures featuresRequired;
	private bool requireUniqueGraphicsQueue;
	private bool requireUniquePresentQueue;
	private bool requireUniqueTransferQueue;
	private bool selectFirst;

	internal VkPhysicalDeviceSelector( Instance instance )
	{
		VkInvalidHandleException.ThrowIfInvalid( instance );

		this.instance = instance;
	}

	internal VkPhysicalDeviceSelector RequireDeviceName( string name )
	{
		deviceNameRequired = name;
		return this;
	}

	internal VkPhysicalDeviceSelector RequireVersion( uint major, uint minor, uint patch )
	{
		versionRequired = new Version32( major, minor, patch );
		return this;
	}

	internal VkPhysicalDeviceSelector WithSurface( SurfaceKHR surface, KhrSurface surfaceExtension )
	{
		VkInvalidHandleException.ThrowIfInvalid( surface );

		this.surface = surface;
		this.surfaceExtension = surfaceExtension;
		return this;
	}

	internal VkPhysicalDeviceSelector RequireDiscreteDevice( bool requireDiscreteDevice )
	{
		discreteDeviceRequired = requireDiscreteDevice;
		return this;
	}

	internal VkPhysicalDeviceSelector WithFeatures( PhysicalDeviceFeatures requiredFeatures )
	{
		featuresRequired = requiredFeatures;
		return this;
	}

	internal VkPhysicalDeviceSelector RequireUniqueGraphicsQueue( bool requireUniqueGraphicsQueue )
	{
		this.requireUniqueGraphicsQueue = requireUniqueGraphicsQueue;
		return this;
	}

	internal VkPhysicalDeviceSelector RequireUniquePresentQueue( bool requireUniquePresentQueue )
	{
		this.requireUniquePresentQueue = requireUniquePresentQueue;
		return this;
	}

	internal VkPhysicalDeviceSelector RequireUniqueTransferQueue( bool requireUniqueTransferQueue )
	{
		this.requireUniqueTransferQueue = requireUniqueTransferQueue;
		return this;
	}

	internal VkPhysicalDeviceSelector ShouldSelectFirst( bool selectFirst )
	{
		this.selectFirst = selectFirst;
		return this;
	}

	internal unsafe VkPhysicalDeviceSelectorResult Select() => SelectMany().FirstOrDefault();

	internal unsafe ImmutableArray<VkPhysicalDeviceSelectorResult> SelectMany()
	{
		var physicalDeviceCount = 0u;
		Apis.Vk.EnumeratePhysicalDevices( instance, ref physicalDeviceCount, null ).Verify();

		PhysicalDevice* physicalDevices = stackalloc PhysicalDevice[(int)physicalDeviceCount];
		Apis.Vk.EnumeratePhysicalDevices( instance, ref physicalDeviceCount, physicalDevices );

		if ( physicalDeviceCount > 0 && selectFirst )
		{
			var physicalDevice = physicalDevices[0];
			var queueFamilyIndices = VkQueueFamilyIndices.Get( physicalDevice, surface, surfaceExtension,
				requireUniqueGraphicsQueue, requireUniquePresentQueue, requireUniqueTransferQueue );
			return [new VkPhysicalDeviceSelectorResult( physicalDevice, queueFamilyIndices )];
		}

		var suitableDevicesBuilder = ImmutableArray.CreateBuilder<VkPhysicalDeviceSelectorResult>( (int)physicalDeviceCount );

		for ( var i = 0; i < physicalDeviceCount; i++ )
		{
			var physicalDevice = physicalDevices[i];
			if ( IsSuitable( physicalDevice, out var queueFamilyIndices ) )
				suitableDevicesBuilder.Add( new VkPhysicalDeviceSelectorResult( physicalDevice, queueFamilyIndices ) );
		}

		suitableDevicesBuilder.Capacity = suitableDevicesBuilder.Count;
		return suitableDevicesBuilder.MoveToImmutable();
	}

	internal unsafe ImmutableArray<string> SelectNames()
	{
		var suitableDevices = SelectMany();

		var namesBuilder = ImmutableArray.CreateBuilder<string>( suitableDevices.Length );
		for ( var i = 0; i < suitableDevices.Length; i++ )
		{
			var properties = Apis.Vk.GetPhysicalDeviceProperties( suitableDevices[i].PhysicalDevice );
			var name = SilkMarshal.PtrToString( (nint)properties.DeviceName );
			if ( name is null )
				continue;

			namesBuilder.Add( name );
		}

		namesBuilder.Capacity = namesBuilder.Count;
		return namesBuilder.MoveToImmutable();
	}

	private unsafe bool IsSuitable( PhysicalDevice physicalDevice, out VkQueueFamilyIndices queueFamilyIndices )
	{
		queueFamilyIndices = default;
		var properties = Apis.Vk.GetPhysicalDeviceProperties( physicalDevice );

		if ( discreteDeviceRequired && properties.DeviceType != PhysicalDeviceType.DiscreteGpu )
			return false;

		if ( deviceNameRequired != string.Empty )
		{
			var deviceName = SilkMarshal.PtrToString( (nint)properties.DeviceName );
			if ( deviceNameRequired != deviceName )
				return false;
		}

		var apiVersion = (Version32)properties.ApiVersion;
		if ( versionRequired.Major > apiVersion.Major ||
			versionRequired.Minor > apiVersion.Minor ||
			versionRequired.Patch > apiVersion.Patch )
			return false;

		var features = Apis.Vk.GetPhysicalDeviceFeatures( physicalDevice );
		if ( !IsFeatureComplete( featuresRequired, features ) )
			return false;

		queueFamilyIndices = VkQueueFamilyIndices.Get( physicalDevice, surface, surfaceExtension,
			requireUniqueGraphicsQueue, requireUniquePresentQueue, requireUniqueTransferQueue );
		return queueFamilyIndices.GraphicsQueue != uint.MaxValue && queueFamilyIndices.PresentQueue != uint.MaxValue;
	}

	private static bool IsFeatureComplete( PhysicalDeviceFeatures requiredFeatures, PhysicalDeviceFeatures physicalDeviceFeatures )
	{
		var featuresFields = typeof( PhysicalDeviceFeatures ).GetFields();
		for ( var i = 0; i < featuresFields.Length; i++ )
		{
			var requiredValueObj = featuresFields[i].GetValue( requiredFeatures );
			if ( requiredValueObj is not Bool32 requiredValue )
				throw new VkException( $"Failed to get {nameof( PhysicalDeviceFeatures )}.{featuresFields[i].Name} required value" );

			var supportedValueObj = featuresFields[i].GetValue( physicalDeviceFeatures );
			if ( supportedValueObj is not Bool32 supportedValue )
				throw new VkException( $"Failed to get {nameof( PhysicalDeviceFeatures )}.{featuresFields[i].Name} supported value" );

			if ( requiredValue && !supportedValue )
				return false;
		}

		return true;
	}
}

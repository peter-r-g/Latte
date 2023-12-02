using Latte.NewRenderer.Extensions;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Latte.NewRenderer.Builders;

internal sealed class VkPhysicalDeviceSelector
{
	internal Instance Instance { get; }
	internal Version32 VersionRequired { get; private set; } = new( 1, 0, 0 );
	internal SurfaceKHR Surface { get; private set; }
	internal KhrSurface? SurfaceExtension { get; private set; }
	internal bool DiscreteDeviceRequired { get; private set; }
	internal string DeviceNameRequired { get; private set; } = string.Empty;
	internal PhysicalDeviceFeatures FeaturesRequired { get; private set; }
	internal bool UniqueGraphicsQueueRequired { get; private set; }
	internal bool UniquePresentQueueRequired { get; private set; }
	internal bool SelectFirst { get; private set; } = false;

	internal VkPhysicalDeviceSelector( Instance instance )
	{
		Instance = instance;
	}

	internal VkPhysicalDeviceSelector RequireDeviceName( string name )
	{
		DeviceNameRequired = name;
		return this;
	}

	internal VkPhysicalDeviceSelector RequireVersion( uint major, uint minor, uint patch )
	{
		VersionRequired = new Version32( major, minor, patch );
		return this;
	}

	internal VkPhysicalDeviceSelector WithSurface( SurfaceKHR surface, KhrSurface surfaceExtension )
	{
		Surface = surface;
		SurfaceExtension = surfaceExtension;
		return this;
	}

	internal VkPhysicalDeviceSelector RequireDiscreteDevice( bool requireDiscreteDevice )
	{
		DiscreteDeviceRequired = requireDiscreteDevice;
		return this;
	}

	internal VkPhysicalDeviceSelector RequireFeatures( PhysicalDeviceFeatures requiredFeatures )
	{
		FeaturesRequired = requiredFeatures;
		return this;
	}

	internal VkPhysicalDeviceSelector RequireUniqueGraphicsQueue( bool requireUniqueGraphicsQueue )
	{
		UniqueGraphicsQueueRequired = requireUniqueGraphicsQueue;
		return this;
	}

	internal VkPhysicalDeviceSelector RequireUniquePresentQueue( bool requireUniquePresentQueue )
	{
		UniquePresentQueueRequired = requireUniquePresentQueue;
		return this;
	}

	internal VkPhysicalDeviceSelector ShouldSelectFirst( bool selectFirst )
	{
		SelectFirst = selectFirst;
		return this;
	}

	internal unsafe PhysicalDevice Select() => SelectMany().FirstOrDefault();

	internal unsafe ImmutableArray<PhysicalDevice> SelectMany()
	{
		var physicalDeviceCount = 0u;
		Apis.Vk.EnumeratePhysicalDevices( Instance, ref physicalDeviceCount, null ).Verify();

		PhysicalDevice* physicalDevices = stackalloc PhysicalDevice[(int)physicalDeviceCount];
		Apis.Vk.EnumeratePhysicalDevices( Instance, ref physicalDeviceCount, physicalDevices );

		if ( physicalDeviceCount > 0 && SelectFirst )
			return [physicalDevices[0]];

		var suitableDevicesBuilder = ImmutableArray.CreateBuilder<PhysicalDevice>( (int)physicalDeviceCount );

		for ( var i = 0; i < physicalDeviceCount; i++ )
		{
			if ( IsSuitable( physicalDevices[i] ) )
				suitableDevicesBuilder.Add( physicalDevices[i] );
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
			var properties = Apis.Vk.GetPhysicalDeviceProperties( suitableDevices[i] );
			var name = SilkMarshal.PtrToString( (nint)properties.DeviceName );
			if ( name is null )
				continue;

			namesBuilder.Add( name );
		}

		namesBuilder.Capacity = namesBuilder.Count;
		return namesBuilder.MoveToImmutable();
	}

	private unsafe bool IsSuitable( PhysicalDevice physicalDevice )
	{
		if ( UniquePresentQueueRequired && SurfaceExtension is null )
			throw new InvalidOperationException( "A unique present queue is required but no surface (extension) was provided to the selector" );

		var properties = Apis.Vk.GetPhysicalDeviceProperties( physicalDevice );

		if ( DiscreteDeviceRequired && properties.DeviceType != PhysicalDeviceType.DiscreteGpu )
			return false;

		if ( DeviceNameRequired != string.Empty )
		{
			var deviceName = SilkMarshal.PtrToString( (nint)properties.DeviceName );
			if ( DeviceNameRequired != deviceName )
				return false;
		}

		var apiVersion = (Version32)properties.ApiVersion;
		if ( VersionRequired.Major > apiVersion.Major ||
			VersionRequired.Minor > apiVersion.Minor ||
			VersionRequired.Patch > apiVersion.Patch )
			return false;

		var features = Apis.Vk.GetPhysicalDeviceFeatures( physicalDevice );
		if ( !IsFeatureComplete( FeaturesRequired, features ) )
			return false;

		var indices = VkQueueFamilyIndices.Get( physicalDevice, Surface, SurfaceExtension, UniqueGraphicsQueueRequired, UniquePresentQueueRequired );
		return indices.GraphicsQueue != uint.MaxValue && indices.PresentQueue != uint.MaxValue;
	}

	private static bool IsFeatureComplete( PhysicalDeviceFeatures requiredFeatures, PhysicalDeviceFeatures physicalDeviceFeatures )
	{
		var featuresFields = typeof( PhysicalDeviceFeatures ).GetFields();
		for ( var i = 0; i < featuresFields.Length; i++ )
		{
			var requiredValueObj = featuresFields[i].GetValue( requiredFeatures );
			if ( requiredValueObj is not Bool32 requiredValue )
				throw new InvalidCastException();

			var supportedValueObj = featuresFields[i].GetValue( physicalDeviceFeatures );
			if ( supportedValueObj is not Bool32 supportedValue )
				throw new InvalidCastException();

			if ( requiredValue && !supportedValue )
				return false;
		}

		return true;
		/*if ( requiredFeatures.AlphaToOne && !physicalDeviceFeatures.AlphaToOne )
			return false;

		if ( requiredFeatures.DepthBiasClamp && !physicalDeviceFeatures.DepthBiasClamp )
			return false;

		if ( requiredFeatures.DepthBounds && !physicalDeviceFeatures.DepthBounds )
			return false;

		if ( requiredFeatures.DepthClamp && !physicalDeviceFeatures.DepthClamp )
			return false;

		if ( requiredFeatures.DrawIndirectFirstInstance && !physicalDeviceFeatures.DrawIndirectFirstInstance )
			return false;

		if ( requiredFeatures.DualSrcBlend && !physicalDeviceFeatures.DualSrcBlend )
			return false;

		if ( requiredFeatures.FillModeNonSolid && !physicalDeviceFeatures.FillModeNonSolid )
			return false;

		if ( requiredFeatures.FragmentStoresAndAtomics && !physicalDeviceFeatures.FragmentStoresAndAtomics )
			return false;

		if ( requiredFeatures.FullDrawIndexUint32 && !physicalDeviceFeatures.FullDrawIndexUint32 )
			return false;

		if ( requiredFeatures.GeometryShader && !physicalDeviceFeatures.GeometryShader )
			return false;

		if ( requiredFeatures.ImageCubeArray && !physicalDeviceFeatures.ImageCubeArray )
			return false;

		if ( requiredFeatures.IndependentBlend && !physicalDeviceFeatures.IndependentBlend )
			return false;

		if ( requiredFeatures.InheritedQueries && !physicalDeviceFeatures.InheritedQueries )
			return false;

		if ( requiredFeatures.LargePoints && !physicalDeviceFeatures.LargePoints )
			return false;

		if ( requiredFeatures.LogicOp && !physicalDeviceFeatures.LogicOp )
			return false;

		if ( requiredFeatures.MultiDrawIndirect && !physicalDeviceFeatures.MultiDrawIndirect )
			return false;

		if ( requiredFeatures.MultiViewport && !physicalDeviceFeatures.MultiViewport )
			return false;

		if ( requiredFeatures.OcclusionQueryPrecise && !physicalDeviceFeatures.OcclusionQueryPrecise )
			return false;

		if ( requiredFeatures.PipelineStatisticsQuery && !physicalDeviceFeatures.PipelineStatisticsQuery )
			return false;

		if ( requiredFeatures.RobustBufferAccess && !physicalDeviceFeatures.RobustBufferAccess )
			return false;

		if ( requiredFeatures.SamplerAnisotropy && !physicalDeviceFeatures.SamplerAnisotropy )
			return false;

		if ( requiredFeatures.SampleRateShading && !physicalDeviceFeatures.SampleRateShading )
			return false;

		if ( requiredFeatures.ShaderClipDistance && !physicalDeviceFeatures.ShaderClipDistance )
			return false;

		if ( requiredFeatures.ShaderCullDistance && !physicalDeviceFeatures.ShaderCullDistance )
			return false;

		if ( requiredFeatures.ShaderFloat64 && !physicalDeviceFeatures.ShaderFloat64 )
			return false;

		if ( requiredFeatures.ShaderImageGatherExtended && !physicalDeviceFeatures.ShaderImageGatherExtended )
			return false;

		if ( requiredFeatures.ShaderInt16 && !physicalDeviceFeatures.ShaderInt16 )
			return false;

		if ( requiredFeatures.ShaderInt64 && !physicalDeviceFeatures.ShaderInt64 )
			return false;

		if ( requiredFeatures.ShaderResourceMinLod && !physicalDeviceFeatures.ShaderResourceMinLod )
			return false;

		if ( requiredFeatures.ShaderResourceResidency && !physicalDeviceFeatures.ShaderResourceResidency )
			return false;

		if ( requiredFeatures.ShaderSampledImageArrayDynamicIndexing && !physicalDeviceFeatures.ShaderSampledImageArrayDynamicIndexing )
			return false;

		if ( requiredFeatures.ShaderStorageBufferArrayDynamicIndexing && !physicalDeviceFeatures.ShaderStorageBufferArrayDynamicIndexing )
			return false;

		if ( requiredFeatures.ShaderStorageImageArrayDynamicIndexing && !physicalDeviceFeatures.ShaderStorageImageArrayDynamicIndexing )
			return false;

		if ( requiredFeatures.ShaderStorageImageExtendedFormats && !physicalDeviceFeatures.ShaderStorageImageExtendedFormats )
			return false;

		if ( requiredFeatures.ShaderStorageImageMultisample && !physicalDeviceFeatures.ShaderStorageImageMultisample )
			return false;

		if ( requiredFeatures.ShaderStorageImageReadWithoutFormat && !physicalDeviceFeatures.ShaderStorageImageReadWithoutFormat )
			return false;

		if ( requiredFeatures.ShaderStorageImageWriteWithoutFormat && !physicalDeviceFeatures.ShaderStorageImageWriteWithoutFormat )
			return false;

		if ( requiredFeatures.ShaderTessellationAndGeometryPointSize && !physicalDeviceFeatures.ShaderTessellationAndGeometryPointSize )
			return false;

		if ( requiredFeatures.ShaderUniformBufferArrayDynamicIndexing && !physicalDeviceFeatures.ShaderUniformBufferArrayDynamicIndexing )
			return false;

		return true;*/
	}
}

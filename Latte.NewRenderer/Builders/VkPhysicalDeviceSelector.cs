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
	private readonly Instance instance;

	private Version32 versionRequired = new( 1, 0, 0 );
	private SurfaceKHR surface;
	private KhrSurface? surfaceExtension;
	private bool discreteDeviceRequired;
	private string deviceNameRequired = string.Empty;
	private PhysicalDeviceFeatures featuresRequired;
	private bool requireUniqueGraphicsQueue;
	private bool requireUniquePresentQueue;
	private bool selectFirst;

	internal VkPhysicalDeviceSelector( Instance instance )
	{
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
			var queueFamilyIndices = VkQueueFamilyIndices.Get( physicalDevice, surface, surfaceExtension, requireUniqueGraphicsQueue, requireUniquePresentQueue );
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

		queueFamilyIndices = VkQueueFamilyIndices.Get( physicalDevice, surface, surfaceExtension, requireUniqueGraphicsQueue, requireUniquePresentQueue );
		return queueFamilyIndices.GraphicsQueue != uint.MaxValue && queueFamilyIndices.PresentQueue != uint.MaxValue;
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

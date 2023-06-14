using Latte.Windowing.Backend.Vulkan;
using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Latte.Windowing.Extensions;

/// <summary>
/// Contains extension methods for <see cref="Gpu"/>.
/// </summary>
internal static class GpuExtensions
{
	internal static Gpu GetBestGpu( this IEnumerable<Gpu> gpus, out int deviceScore, Func<Gpu, bool>? isSuitableCb = null, Func<Gpu, int, int>? suitabilityCb = null )
	{
		if ( !gpus.Any() )
			throw new ArgumentException( "There are no GPUs to choose from", nameof( gpus ) );

		var bestGpu = gpus.First();
		var bestDeviceScore = GetDeviceSuitability( bestGpu, isSuitableCb, suitabilityCb );

		foreach ( var gpu in gpus.Skip( 1 ) )
		{
			var gpuScore = GetDeviceSuitability( gpu, isSuitableCb, suitabilityCb );
			if ( gpuScore <= bestDeviceScore )
				continue;

			bestGpu = gpu;
			bestDeviceScore = gpuScore;
		}

		deviceScore = bestDeviceScore;
		return bestGpu;
	}

	private static bool IsGpuSuitable( Gpu gpu, Func<Gpu, bool>? isSuitableCb = null )
	{
		if ( isSuitableCb is null )
			return true;
		else
			return isSuitableCb( gpu );
	}

	private static int GetDeviceSuitability( Gpu gpu, Func<Gpu, bool>? isSuitableCb = null, Func<Gpu, int, int>? suitabilityCb = null )
	{
		if ( !IsGpuSuitable( gpu, isSuitableCb ) )
			return -1;

		var score = 0;
		if ( suitabilityCb is not null )
			score = suitabilityCb( gpu, score );

		if ( gpu.Properties.DeviceType == PhysicalDeviceType.DiscreteGpu )
			score += 1000;

		return score;
	}
}

using System;
using System.Collections.Generic;
using VMASharp;

namespace Latte.NewRenderer.Vulkan;

internal sealed class VkStatistics(
	IReadOnlyDictionary<string, TimeSpan> initializationTimings,
	IReadOnlyDictionary<string, TimeSpan> cpuTimings,
	TimeSpan gpuExecuteTime,
	IReadOnlyDictionary<string, VkPipelineStatistics> materialStatistics,
	Stats vmaStats )
{
	internal readonly IReadOnlyDictionary<string, TimeSpan> InitializationTimings = initializationTimings;
	internal readonly IReadOnlyDictionary<string, TimeSpan> CpuTimings = cpuTimings;
	internal readonly TimeSpan GpuExecuteTime = gpuExecuteTime;
	internal readonly IReadOnlyDictionary<string, VkPipelineStatistics> MaterialStatistics = materialStatistics;
	internal readonly Stats VmaStats = vmaStats;
}

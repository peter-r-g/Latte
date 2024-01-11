using System;
using System.Collections.Generic;

namespace Latte.NewRenderer;

internal sealed class VkStatistics( IReadOnlyDictionary<string, TimeSpan> initializationTimings,
	IReadOnlyDictionary<string, TimeSpan> cpuTimings,
	TimeSpan gpuExecuteTime,
	IReadOnlyDictionary<string, VkPipelineStatistics> materialStatistics )
{
	internal readonly IReadOnlyDictionary<string, TimeSpan> InitializationTimings = initializationTimings;
	internal readonly IReadOnlyDictionary<string, TimeSpan> CpuTimings = cpuTimings;
	internal readonly TimeSpan GpuExecuteTime = gpuExecuteTime;
	internal IReadOnlyDictionary<string, VkPipelineStatistics> MaterialStatistics = materialStatistics;
}

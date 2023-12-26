using System;
using System.Collections.Generic;

namespace Latte.NewRenderer;

internal sealed class VkStatistics( IReadOnlyDictionary<string, TimeSpan> cpuTimings, TimeSpan gpuExecuteTime,
	IReadOnlyDictionary<string, PipelineStatistics> materialStatistics )
{
	internal readonly IReadOnlyDictionary<string, TimeSpan> CpuTimings = cpuTimings;
	internal readonly TimeSpan GpuExecuteTime = gpuExecuteTime;
	internal IReadOnlyDictionary<string, PipelineStatistics> MaterialStatistics = materialStatistics;
}

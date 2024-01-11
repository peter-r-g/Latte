using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Latte.NewRenderer.Vulkan;

internal sealed class VkStatistics(
	IReadOnlyDictionary<string, TimeSpan> initializationTimings,
	IReadOnlyDictionary<string, TimeSpan> cpuTimings,
	TimeSpan gpuExecuteTime,
	IReadOnlyDictionary<string, VkPipelineStatistics> materialStatistics,
	int allocationCount,
	ImmutableArray<ulong> memoryTypeAllocationSizes )
{
	internal readonly IReadOnlyDictionary<string, TimeSpan> InitializationTimings = initializationTimings;
	internal readonly IReadOnlyDictionary<string, TimeSpan> CpuTimings = cpuTimings;
	internal readonly TimeSpan GpuExecuteTime = gpuExecuteTime;
	internal readonly IReadOnlyDictionary<string, VkPipelineStatistics> MaterialStatistics = materialStatistics;

	internal readonly int AllocationCount = allocationCount;
	internal readonly ImmutableArray<ulong> MemoryTypeAllocationSizes = memoryTypeAllocationSizes;
}

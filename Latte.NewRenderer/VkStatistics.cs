﻿using System;
using System.Collections.Generic;

namespace Latte.NewRenderer;

internal sealed class VkStatistics( TimeSpan cpuRecordTime, TimeSpan gpuExecuteTime, int drawCalls,
	IReadOnlyDictionary<string, PipelineStatistics> materialStatistics )
{
	internal readonly TimeSpan CpuRecordTime = cpuRecordTime;
	internal readonly TimeSpan GpuExecuteTime = gpuExecuteTime;
	internal readonly int DrawCalls = drawCalls;
	internal IReadOnlyDictionary<string, PipelineStatistics> MaterialStatistics = materialStatistics;
}
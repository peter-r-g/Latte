using System.Diagnostics.CodeAnalysis;

namespace Latte.NewRenderer.Renderer.Vulkan;

[method: SetsRequiredMembers]
internal readonly struct VkPipelineStatistics(
	ulong inputAssemblyVertexCount,
	ulong inputAssemblyPrimitivesCount,
	ulong vertexShaderInvocationCount,
	ulong clippingStagePrimitivesProcessed,
	ulong clippingStagePrimitivesOutput,
	ulong fragmentShaderInvocations )
{
	internal required ulong InputAssemblyVertexCount { get; init; } = inputAssemblyVertexCount;
	internal required ulong InputAssemblyPrimitivesCount { get; init; } = inputAssemblyPrimitivesCount;
	internal required ulong VertexShaderInvocationCount { get; init; } = vertexShaderInvocationCount;
	internal required ulong ClippingStagePrimitivesProcessed { get; init; } = clippingStagePrimitivesProcessed;
	internal required ulong ClippingStagePrimitivesOutput { get; init; } = clippingStagePrimitivesOutput;
	internal required ulong FragmentShaderInvocations { get; init; } = fragmentShaderInvocations;
}

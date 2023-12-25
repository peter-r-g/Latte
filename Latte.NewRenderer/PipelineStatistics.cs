namespace Latte.NewRenderer;

internal struct PipelineStatistics(
	ulong inputAssemblyVertexCount,
	ulong inputAssemblyPrimitivesCount,
	ulong vertexShaderInvocationCount,
	ulong clippingStagePrimitivesProcessed,
	ulong clippingStagePrimitivesOutput,
	ulong fragmentShaderInvocations )
{
	internal ulong InputAssemblyVertexCount = inputAssemblyVertexCount;
	internal ulong InputAssemblyPrimitivesCount = inputAssemblyPrimitivesCount;
	internal ulong VertexShaderInvocationCount = vertexShaderInvocationCount;
	internal ulong ClippingStagePrimitivesProcessed = clippingStagePrimitivesProcessed;
	internal ulong ClippingStagePrimitivesOutput = clippingStagePrimitivesOutput;
	internal ulong FragmentShaderInvocations = fragmentShaderInvocations;
}

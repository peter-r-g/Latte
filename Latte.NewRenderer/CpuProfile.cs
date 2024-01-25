using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Latte.Windowing;

internal sealed class CpuProfile : IDisposable
{
	internal required string Name { get; init; }
	internal TimeSpan Time { get; private set; }

	private readonly long ticks;

	[SetsRequiredMembers]
	private CpuProfile( string name )
	{
		Name = name;
		ticks = Stopwatch.GetTimestamp();
	}

	public void Dispose()
	{
		Time = Stopwatch.GetElapsedTime( ticks );
	}

	internal static CpuProfile New( string name )
	{
		return new CpuProfile( name );
	}
}

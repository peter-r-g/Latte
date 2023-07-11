using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Latte.Logging;

public readonly struct ScopedTimingLogger : IDisposable
{
	private string Name { get; }
	private Logger Logger { get; }
	private LogLevel LogLevel { get; }
	private long StartTicks { get; }

	public ScopedTimingLogger( Logger logger, LogLevel logLevel = LogLevel.Verbose, [CallerMemberName] string? name = null )
	{
		Name = name ?? "Unknown";
		Logger = logger;
		LogLevel = logLevel;
		StartTicks = Stopwatch.GetTimestamp();
	}

	public void Dispose()
	{
		var elapsed = Stopwatch.GetElapsedTime( StartTicks );

		switch ( LogLevel )
		{
			case LogLevel.Error:
				Logger.Error( $"{Name} took {elapsed.TotalMilliseconds}ms" );
				break;
			case LogLevel.Warning:
				Logger.Warning( $"{Name} took {elapsed.TotalMilliseconds}ms" );
				break;
			case LogLevel.Information:
				Logger.Information( $"{Name} took {elapsed.TotalMilliseconds}ms" );
				break;
			case LogLevel.Verbose:
				Logger.Verbose( $"{Name} took {elapsed.TotalMilliseconds}ms" );
				break;
		}
	}
}

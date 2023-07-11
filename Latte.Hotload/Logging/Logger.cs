using System;

namespace Latte.Logging;

public class Logger
{
	public string Name { get; init; }
	public LogLevel Level { get; init; }

	public Logger( string name, LogLevel enabledLevel )
	{
		Name = name;
		Level = enabledLevel;
	}

	public bool IsEnabled( LogLevel level )
	{
		return Level >= level;
	}

	public void Error( string message )
	{
		if ( !IsEnabled( LogLevel.Error ) )
			return;

		Console.ForegroundColor = ConsoleColor.Red;
		Console.Write( '[' );
		Console.Write( Name );
		Console.Write( "] [" );
		Console.Write( "ERR] " );
		Console.WriteLine( message );
	}

	public void Warning( string message )
	{
		if ( !IsEnabled( LogLevel.Warning ) )
			return;

		Console.ForegroundColor = ConsoleColor.DarkYellow;
		Console.Write( '[' );
		Console.Write( Name );
		Console.Write( "] [" );
		Console.Write( "WARN] " );
		Console.WriteLine( message );
	}

	public void Information( string message )
	{
		if ( !IsEnabled( LogLevel.Information ) )
			return;

		Console.ForegroundColor = ConsoleColor.Gray;
		Console.Write( '[' );
		Console.Write( Name );
		Console.Write( "] [" );
		Console.Write( "INFO] " );
		Console.WriteLine( message );
	}

	public void Verbose( string message )
	{
		if ( !IsEnabled( LogLevel.Verbose ) )
			return;

		Console.ForegroundColor = ConsoleColor.Gray;
		Console.Write( '[' );
		Console.Write( Name );
		Console.Write( "] [" );
		Console.Write( "VERB] " );
		Console.WriteLine( message );
	}
}

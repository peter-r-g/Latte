using System;
using System.Threading;

namespace Latte.Windowing;

internal static class Program
{
	private const int WindowsToOpen = 1;

	private static readonly Window[] windows = new Window[WindowsToOpen];
	private static readonly Thread[] windowThreads = new Thread[WindowsToOpen];

	private static unsafe void Main()
	{
		for ( var i = 0; i < WindowsToOpen; i++ )
		{
			windows[i] = new Window();
			windowThreads[i] = new Thread( windows[i].Main );
			windowThreads[i].Start();
		}

		var line = string.Empty;
		do
		{
			line = ReadLine();
			HandleCommand( line );
		} while ( line != "quit" && AreWindowsActive() );

		foreach ( var window in windows )
			window.Close();

		foreach ( var windowThread in windowThreads )
			windowThread.Join();
	}

	private static string ReadLine()
	{
		var line = string.Empty;
		var key = default( ConsoleKeyInfo );
		do
		{
			if ( !Console.KeyAvailable )
				continue;

			key = Console.ReadKey( true );
			if ( char.IsAsciiLetterOrDigit( key.KeyChar ) || key.Key == ConsoleKey.Spacebar )
			{
				line += key.KeyChar;
				Console.Write( key.KeyChar );
			}

			if ( key.Key == ConsoleKey.Backspace )
			{
				line = line[..^1];
				Console.WriteLine();
				Console.Write( line );
			}
		} while ( key.Key != ConsoleKey.Enter && AreWindowsActive() );
		Console.WriteLine();

		return line;
	}

	private static void HandleCommand( string cmd )
	{
	}

	private static bool AreWindowsActive()
	{
		foreach ( var windowThread in windowThreads )
		{
			if ( !windowThread.IsAlive )
				continue;

			return true;
		}

		return false;
	}
}

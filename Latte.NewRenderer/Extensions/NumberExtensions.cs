using System;

namespace Latte.NewRenderer.Vulkan.Extensions;

// Credit to https://stackoverflow.com/a/14488941 for the method.
internal static class NumberExtensions
{
	private static readonly string[] SizeSuffixes = ["bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB"];

	internal static string ToDataSize( this int value, int decimalPlaces = 0 ) => ToDataSize( (decimal)value, decimalPlaces );
	internal static string ToDataSize( this uint value, int decimalPlaces = 0 ) => ToDataSize( (decimal)value, decimalPlaces );
	internal static string ToDataSize( this long value, int decimalPlaces = 0 ) => ToDataSize( (decimal)value, decimalPlaces );
	internal static string ToDataSize( this ulong value, int decimalPlaces = 0 ) => ToDataSize( (decimal)value, decimalPlaces );
	internal static string ToDataSize( this float value, int decimalPlaces = 0 ) => ToDataSize( (decimal)value, decimalPlaces );
	internal static string ToDataSize( this double value, int decimalPlaces = 0 ) => ToDataSize( (decimal)value, decimalPlaces );

	internal static string ToDataSize( this decimal value, int decimalPlaces = 0 )
	{
		if ( value < 0 ) { return "-" + ToDataSize( -value, decimalPlaces ); }

		var i = 0;
		while ( Math.Round( value, decimalPlaces ) >= 1000 )
		{
			value /= 1024;
			i++;
		}

		return string.Format( "{0:n" + decimalPlaces + "} {1}", value, SizeSuffixes[i] );
	}
}

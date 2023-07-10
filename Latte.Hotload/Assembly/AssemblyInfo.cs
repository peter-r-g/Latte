namespace Latte.Hotload;

internal readonly struct AssemblyInfo
{
	internal string Name { get; init; }

	internal string? Path
	{
		get => path;
		init
		{
			if ( value is null )
			{
				path = null;
				return;
			}

			if ( !System.IO.Path.IsPathFullyQualified( value ) )
				path = System.IO.Path.GetFullPath( System.IO.Path.Combine( Program.CurrentDirectory, value ) );
			else
				path = value;
		}
	}
	private readonly string? path;

	internal string? ProjectPath
	{
		get => projectPath;
		init
		{
			if ( value is null )
			{
				projectPath = null;
				return;
			}

			if ( !System.IO.Path.IsPathFullyQualified( value ) )
				projectPath = System.IO.Path.GetFullPath( System.IO.Path.Combine( Program.CurrentDirectory, value ) );
			else
				projectPath = value;
		}
	}
	private readonly string? projectPath;
}

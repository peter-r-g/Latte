namespace Latte.Hotload;

public readonly struct AssemblyInfo
{
	public string Name { get; internal init; }

	public string? Path
	{
		get => path;
		internal init
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

	public string? ProjectPath
	{
		get => projectPath;
		internal init
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

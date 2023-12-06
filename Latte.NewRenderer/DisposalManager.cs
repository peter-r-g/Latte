using System;
using System.Collections.Generic;

namespace Latte.NewRenderer;

internal sealed class DisposalManager : IDisposable
{
	private readonly List<Action> disposals = [];
	private readonly Dictionary<string, List<WeakReference<Action>>> taggedDisposals = [];

	private bool disposed;

	~DisposalManager()
	{
		Dispose( disposing: false );
	}

	internal void Add( Action cb, params string[] tags )
	{
		disposals.Add( cb );
		foreach ( var tag in tags )
		{
			if ( !taggedDisposals.ContainsKey( tag ) )
				taggedDisposals.Add( tag, [] );

			taggedDisposals[tag].Add( new WeakReference<Action>( cb ) );
		}
	}

	internal void Dispose( string tag )
	{
		if ( !taggedDisposals.TryGetValue( tag, out var disposals ) || disposals.Count == 0 )
			throw new ArgumentException( $"There is nothing to be disposed with the tag \"{tag}\"", nameof( tag ) );

		for ( var i = disposals.Count - 1; i >= 0; i-- )
		{
			if ( !disposals[i].TryGetTarget( out var cb ) )
				continue;

			this.disposals.Remove( cb );
			cb();
		}

		disposals.Clear();
	}

	private void Dispose( bool disposing )
	{
		if ( disposed )
			return;

		for ( var i = disposals.Count - 1; i >= 0; i-- )
			disposals[i]();

		disposals.Clear();
		taggedDisposals.Clear();
		disposed = true;
	}

	public void Dispose()
	{
		Dispose( disposing: true );
		GC.SuppressFinalize( this );
	}
}

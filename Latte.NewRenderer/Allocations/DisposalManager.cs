﻿using System;
using System.Collections.Generic;
using System.Threading;

namespace Latte.NewRenderer.Allocations;

internal sealed class DisposalManager : IDisposable
{
	private readonly List<Action> disposals = [];
	private readonly Dictionary<string, List<WeakReference<Action>>> taggedDisposals = [];
	private readonly Dictionary<WeakReference<Action>, string[]> disposalTags = [];

	private readonly object addLock = new();
	private readonly object disposeLock = new();

	private bool disposed;

	~DisposalManager()
	{
		Dispose( disposing: false );
	}

	internal void Add( Action cb, params string[] tags )
	{
		Monitor.Enter( addLock );
		disposals.Add( cb );

		var weakCb = new WeakReference<Action>( cb );
		disposalTags.Add( weakCb, tags );

		foreach ( var tag in tags )
		{
			if ( !taggedDisposals.ContainsKey( tag ) )
				taggedDisposals.Add( tag, [] );

			taggedDisposals[tag].Add( weakCb );
		}

		Monitor.Exit( addLock );
	}

	internal void Dispose( string tag )
	{
		if ( !taggedDisposals.TryGetValue( tag, out var disposals ) || disposals.Count == 0 )
			return;

		Monitor.Enter( disposeLock );
		try
		{
			for ( var i = disposals.Count - 1; i >= 0; i-- )
			{
				var weakCb = disposals[i];

				if ( !weakCb.TryGetTarget( out var cb ) )
				{
					RemoveAssociatedReferences( weakCb );
					continue;
				}

				RemoveAssociatedReferences( weakCb );
				cb();
			}

			disposals.Clear();
		}
		finally
		{
			Monitor.Exit( disposeLock );
		}
	}

	private void RemoveAssociatedReferences( WeakReference<Action> weakCb )
	{
		if ( !disposalTags.TryGetValue( weakCb, out var tags ) )
			return;

		foreach ( var tag in tags )
			taggedDisposals[tag].Remove( weakCb );

		disposalTags.Remove( weakCb );

		if ( weakCb.TryGetTarget( out var cb ) )
			disposals.Remove( cb );
	}

	private void Dispose( bool disposing )
	{
		if ( disposed )
			return;

		Monitor.Enter( disposeLock );
		try
		{
			if ( disposing )
			{
			}

			for ( var i = disposals.Count - 1; i >= 0; i-- )
				disposals[i]();

			disposals.Clear();
			taggedDisposals.Clear();
			disposed = true;
		}
		finally
		{
			Monitor.Exit( disposeLock );
		}
	}

	public void Dispose()
	{
		Dispose( disposing: true );
		GC.SuppressFinalize( this );
	}
}

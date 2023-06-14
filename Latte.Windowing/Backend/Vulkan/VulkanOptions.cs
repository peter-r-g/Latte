using Latte.Windowing.Options;
using System.Collections.Generic;

namespace Latte.Windowing.Backend.Vulkan;

internal sealed class VulkanOptions : IRenderingOptions
{
	public bool WireframeEnabled
	{
		get => wireframeEnabled;
		set
		{
			if ( value == wireframeEnabled )
				return;

			wireframeEnabled = value;
			ChangedOptions.Add( nameof( WireframeEnabled ) );
		}
	}
	private bool wireframeEnabled;

	public MsaaOption Msaa
	{
		get => msaa;
		set
		{
			if ( value == msaa )
				return;

			msaa = value;
			ChangedOptions.Add( nameof( Msaa ) );
		}
	}
	private MsaaOption msaa;

	private VulkanBackend Backend { get; }
	private HashSet<string> ChangedOptions { get; } = new();

	internal VulkanOptions( VulkanBackend backend )
	{
		Backend = backend;
	}

	public void ApplyOptions()
	{
		if ( !HasOptionsChanged() )
			return;

		Backend.UpdateFromOptions();
		ChangedOptions.Clear();
	}

	public bool HasOptionsChanged( params string[] optionNames )
	{
		if ( optionNames.Length == 0 )
			return ChangedOptions.Count > 0;

		foreach ( var optionName in optionNames )
		{
			if ( ChangedOptions.Contains( optionName ) )
				return true;
		}

		return false;
	}
}

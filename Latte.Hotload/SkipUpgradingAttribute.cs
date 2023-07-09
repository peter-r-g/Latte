using System;

namespace Latte.Hotload
{
	[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true )]
	public sealed class SkipUpgradingAttribute : Attribute
	{
	}
}

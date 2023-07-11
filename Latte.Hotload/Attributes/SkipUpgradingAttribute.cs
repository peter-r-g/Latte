using System;

namespace Latte.Attributes;

[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true )]
public sealed class SkipUpgradingAttribute : Attribute
{
}

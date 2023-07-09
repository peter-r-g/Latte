using Latte.Attributes;
using Latte.Logging;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Latte.Hotload.Upgrading;

internal static class Upgrader
{
	/// <summary>
	/// An array of all the upgrader instances to use.
	/// </summary>
	private static ImmutableArray<IMemberUpgrader> Upgraders { get; }

	static Upgrader()
	{
		var upgraderTypes = Assembly.GetExecutingAssembly().GetTypes()
			.Where( t => t.GetInterface( nameof( IMemberUpgrader ) ) is not null );

		var upgraders = ImmutableArray.CreateBuilder<IMemberUpgrader>();
		foreach ( var upgraderType in upgraderTypes )
			upgraders.Add( (IMemberUpgrader)Activator.CreateInstance( upgraderType )! );
		Upgraders = upgraders.OrderByDescending( upgrader => upgrader.Priority ).ToImmutableArray();
	}

	internal static void Upgrade( Assembly oldAssembly, IEntryPoint oldEntryPoint, Assembly newAssembly, IEntryPoint newEntryPoint )
	{
		if ( Loggers.Hotloader.IsEnabled( LogLevel.Verbose ) )
			Loggers.Hotloader.Verbose( $"Starting upgrade for {oldAssembly.GetName().Name}" );

		foreach ( var oldType in oldAssembly.DefinedTypes )
		{
			var newType = newAssembly.GetType( oldType.FullName ?? oldType.Name );
			if ( newType is null )
				continue;

			UpgradeStaticInstance( oldType, newType );
		}

		UpgradeInstance( oldEntryPoint, newEntryPoint );

		if ( Loggers.Hotloader.IsEnabled( LogLevel.Verbose ) )
			Loggers.Hotloader.Verbose( $"Finished upgrade for {oldAssembly.GetName().Name}" );
	}

	/// <summary>
	/// Upgrades a <see cref="Type"/>s static members.
	/// </summary>
	/// <param name="oldType">The old version of the <see cref="Type"/>.</param>
	/// <param name="newType">The new version of the <see cref="Type"/>.</param>
	internal static void UpgradeStaticInstance( Type oldType, Type newType )
	{
		UpgradeMembers( oldType, newType, null, null );
	}

	/// <summary>
	/// Upgrades an instance of an object.
	/// </summary>
	/// <param name="oldInstance">The old instance.</param>
	/// <param name="newInstance">The new instance.</param>
	internal static void UpgradeInstance( object? oldInstance, object? newInstance )
	{
		// Bail
		if ( oldInstance is null || newInstance is null )
			return;

		// Upgrade the members.
		UpgradeMembers( oldInstance.GetType(), newInstance.GetType(), oldInstance, newInstance );
	}

	/// <summary>
	/// Upgrades all members on a type.
	/// </summary>
	/// <param name="oldType">The old version of the <see cref="Type"/>.</param>
	/// <param name="newType">The new version of the <see cref="Type"/>.</param>
	/// <param name="oldInstance">The old instance.</param>
	/// <param name="newInstance">The new instance.</param>
	private static void UpgradeMembers( Type oldType, Type newType, object? oldInstance, object? newInstance )
	{
		if ( Loggers.Hotloader.IsEnabled( LogLevel.Verbose ) )
		{
			if ( oldInstance is null && newInstance is null )
				Loggers.Hotloader.Verbose( $"Upgrading static instance of {oldType} to {newType}" );
			else
				Loggers.Hotloader.Verbose( $"Upgrading instance of {oldType} to {newType}" );
		}

		// If both instance are null then we're upgrading static members.
		var bindingFlags = (oldInstance is null && newInstance is null)
			? BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
			: BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		// Get all members from the old type
		var oldMembers = oldType.GetMembers( bindingFlags )
			.Where( member => member is PropertyInfo || member is FieldInfo );

		// For each member:
		// - If it's a reference type, we will want to upgrade it if it's a class
		//   and not a delegate
		// - Otherwise, copy the value
		foreach ( var oldMember in oldMembers )
		{
			//
			// Old member
			//
			if ( oldMember.GetCustomAttribute<CompilerGeneratedAttribute>() is not null ||
				oldMember.GetCustomAttribute<SkipUpgradingAttribute>() is not null )
				continue;

			var oldUpgradable = UpgradableMember.FromMember( oldMember );
			// Can we upgrade this?
			if ( oldUpgradable is null )
				continue;

			//
			// New member
			//
			var newMember = newType.GetMember( oldMember.Name, bindingFlags ).FirstOrDefault();
			// Does this member exist? (eg. might have been deleted)
			if ( newMember is null || newMember.GetCustomAttribute<SkipUpgradingAttribute>() is not null )
				continue;

			var newUpgradable = UpgradableMember.FromMember( newMember );
			// Can we upgrade this?
			if ( newUpgradable is null )
				continue;

			//
			// Upgrade!
			//
			var wasUpgraded = false;
			foreach ( var upgrader in Upgraders )
			{
				if ( !upgrader.CanUpgrade( oldMember ) )
					continue;

				upgrader.UpgradeMember( oldInstance, oldUpgradable, newInstance, newUpgradable );
				wasUpgraded = true;
				break;
			}

			if ( !wasUpgraded && Loggers.Hotloader.IsEnabled( LogLevel.Warning ) )
				Loggers.Hotloader.Warning( $"Don't know how to upgrade {oldMember.MemberType.ToString().ToLower()} '{oldMember.Name}' in '{oldType.Name}'" );
		}
	}
}

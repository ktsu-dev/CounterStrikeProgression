// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// A weapon the plugin tracks progress for.
/// </summary>
/// <param name="Key">The normalized weapon name.</param>
/// <param name="DisplayName">The name players see.</param>
/// <param name="Class">The weapon family.</param>
/// <param name="IsScoped">Whether the weapon has a scope, which makes no-scope and quickscope possible.</param>
internal sealed record WeaponInfo(WeaponKey Key, string DisplayName, WeaponClass Class, bool IsScoped = false)
{
	/// <summary>
	/// Gets a value indicating whether the weapon fires bullets, as opposed to a knife, grenade, or Zeus.
	/// </summary>
	public bool IsGun => Class is WeaponClass.Pistol or WeaponClass.Smg or WeaponClass.Rifle or WeaponClass.Sniper or WeaponClass.Shotgun or WeaponClass.MachineGun;
}

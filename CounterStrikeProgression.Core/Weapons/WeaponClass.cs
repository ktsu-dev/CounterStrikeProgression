// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// The family a weapon belongs to, which decides the skill shots that apply to it.
/// </summary>
internal enum WeaponClass
{
	Unknown = 0,
	Pistol,
	Smg,
	Rifle,
	Sniper,
	Shotgun,
	MachineGun,
	Knife,
	Grenade,
	Taser,
}

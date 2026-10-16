// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Events;

using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// One damaging bullet or one kill, as the attacker earns it. The plugin fills this from the CS2 event and
/// both players' pawns, plus the round and timing context it tracks. Everything here is plain data, so the
/// skill shot rules can be tested without a game server.
/// </summary>
internal sealed record CombatEvent
{
	/// <summary>Gets the weapon that dealt the damage.</summary>
	public required WeaponInfo Weapon { get; init; }

	/// <summary>Gets a value indicating whether this is a kill. A kill also arrives separately as a hit.</summary>
	public bool IsKill { get; init; }

	/// <summary>Gets a value indicating whether the bullet hit the head.</summary>
	public bool IsHeadshot { get; init; }

	/// <summary>Gets the health damage dealt.</summary>
	public int Damage { get; init; }

	/// <summary>Gets a value indicating whether the victim is a bot.</summary>
	public bool VictimIsBot { get; init; }

	/// <summary>Gets a value indicating whether the victim is on the attacker's side, which never pays.</summary>
	public bool IsTeammate { get; init; }

	/// <summary>Gets the attacker's state.</summary>
	public PlayerState Attacker { get; init; } = new();

	/// <summary>Gets the victim's state.</summary>
	public PlayerState Victim { get; init; } = new();

	/// <summary>Gets how fast the victim was moving across the attacker's view, in units per second.</summary>
	public double VictimSidewaysSpeed { get; init; }

	/// <summary>Gets the distance between the players in game units.</summary>
	public double Distance { get; init; }

	/// <summary>Gets the number of objects the bullet passed through.</summary>
	public int Penetrations { get; init; }

	/// <summary>Gets a value indicating whether the kill went through a smoke.</summary>
	public bool ThroughSmoke { get; init; }

	/// <summary>Gets a value indicating whether the attacker was flashed.</summary>
	public bool AttackerBlind { get; init; }

	/// <summary>Gets a value indicating whether a scoped weapon was fired without the scope.</summary>
	public bool NoScope { get; init; }

	/// <summary>Gets the seconds since the attacker last landed, or infinity when unknown.</summary>
	public double SecondsSinceAttackerLanded { get; init; } = double.PositiveInfinity;

	/// <summary>Gets the seconds since the attacker scoped in, or infinity when they didn't.</summary>
	public double SecondsSinceScoped { get; init; } = double.PositiveInfinity;

	/// <summary>Gets the seconds since the attacker switched to this weapon, or infinity when unknown.</summary>
	public double SecondsSinceWeaponSwitch { get; init; } = double.PositiveInfinity;

	/// <summary>Gets the seconds since the attacker last took damage, or infinity when they haven't.</summary>
	public double SecondsSinceAttackerHurt { get; init; } = double.PositiveInfinity;

	/// <summary>Gets a value indicating whether this was the first shot after a reload.</summary>
	public bool FirstShotAfterReload { get; init; }

	/// <summary>Gets the rounds left in the magazine after the shot, or -1 when unknown.</summary>
	public int ClipAfterShot { get; init; } = -1;

	/// <summary>Gets the shots fired in the current spray, counting this one.</summary>
	public int ShotsInSpray { get; init; }

	/// <summary>Gets the kills earlier in the same spray, not counting this one.</summary>
	public int EarlierKillsInSpray { get; init; }

	/// <summary>Gets how far the attacker turned, in degrees, over the last few ticks before the shot.</summary>
	public double RecentTurnDegrees { get; init; }

	/// <summary>Gets the attacker's kills inside the multi-kill window, counting this one.</summary>
	public int ChainedKills { get; init; } = 1;

	/// <summary>Gets the kills the attacker made with the same bullet, counting this one.</summary>
	public int KillsWithThisBullet { get; init; } = 1;

	/// <summary>Gets a value indicating whether this is the round's first kill.</summary>
	public bool IsOpeningKill { get; init; }

	/// <summary>Gets a value indicating whether the victim had just killed the attacker's teammate.</summary>
	public bool IsTrade { get; init; }

	/// <summary>Gets a value indicating whether the victim had been dominating the attacker.</summary>
	public bool IsRevenge { get; init; }
}

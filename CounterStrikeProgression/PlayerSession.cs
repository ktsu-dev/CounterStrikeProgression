// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression;

using CounterStrikeSharp.API;
using ktsu.CounterStrikeProgression.Core.Events;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Tracking;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// Everything the plugin keeps for one connected human player.
/// </summary>
/// <param name="slot">The player's slot.</param>
/// <param name="steamId">The player's 64-bit Steam ID.</param>
internal sealed class PlayerSession(int slot, ulong steamId)
{
	/// <summary>Gets the player's slot.</summary>
	public int Slot { get; } = slot;

	/// <summary>Gets the player's 64-bit Steam ID.</summary>
	public ulong SteamId { get; } = steamId;

	/// <summary>Whether the player has left, which stops load retries. Read off the game thread, so it is volatile.</summary>
	public volatile bool IsGone;

	/// <summary>Gets or sets the player's progress, which is <see langword="null"/> until it has loaded.</summary>
	public PlayerProgress? Progress { get; set; }

	/// <summary>Gets or sets the rank XP the player has earned this map.</summary>
	public double MapXp { get; set; }

	/// <summary>Gets the player's movement, scope, reload, and aim history.</summary>
	public PlayerCombatTracker Combat { get; } = new();

	/// <summary>Gets the player's shots and hits this life.</summary>
	public LifeTracker Life { get; } = new();

	/// <summary>Gets or sets the weapon of the player's last kill this round, which takes a clutch's ladder progress.</summary>
	public WeaponInfo? LastKillWeapon { get; set; }

	/// <summary>Gets or sets the tick and victim of the last hit counted, so shotgun pellets count once.</summary>
	public (int Tick, int Victim) LastHit { get; set; } = (-1, -1);

	/// <summary>Gets or sets the penetrations CS2 reported for the player's last bullet, with its tick and victim.</summary>
	public (int Tick, int Victim, int Penetrations) LastBullet { get; set; } = (-1, -1, 0);

	/// <summary>
	/// Gets or sets the victim's state at the player's last hit, with its tick and victim. A kill reuses it,
	/// because by the time the death event runs the victim's pawn may already be reset.
	/// </summary>
	public (int Tick, int Victim, PlayerState State, double SidewaysSpeed) LastHitVictim { get; set; } = (-1, -1, new PlayerState(), 0);

	/// <summary>Gets or sets when the player may next ask for the leaderboard.</summary>
	public double NextLeaderboardAllowed { get; set; } = double.NegativeInfinity;

	/// <summary>Gets or sets whether the player was scoped on the last tick.</summary>
	public bool WasScoped { get; set; }

	/// <summary>Gets or sets the entity index of the weapon the player held on the last tick.</summary>
	public uint LastWeaponIndex { get; set; }

	/// <summary>Gets or sets when the player was last told about a skill shot, to keep chat readable.</summary>
	public double LastSkillShotMessage { get; set; } = double.NegativeInfinity;

	/// <summary>Gets or sets the rank last set as the player's clan tag, or -1 to set it again.</summary>
	public int ClanTagRank { get; set; } = -1;

	/// <summary>Gets the player's panel state.</summary>
	public PanelState Panel { get; } = new();
}

/// <summary>
/// One player's progress panel: whether it's open, where they are in it, and how it's drawn.
/// </summary>
internal sealed class PanelState
{
	/// <summary>Gets or sets a value indicating whether the panel is open.</summary>
	public bool IsOpen { get; set; }

	/// <summary>Gets or sets the page shown, where 0 is the overview.</summary>
	public int Page { get; set; }

	/// <summary>Gets or sets the first visible body line.</summary>
	public int Scroll { get; set; }

	/// <summary>Gets or sets the buttons held on the last tick, for detecting presses.</summary>
	public PlayerButtons LastButtons { get; set; }

	/// <summary>Gets or sets the way the panel is drawn for this player, which steps down when one fails.</summary>
	public PanelMode Mode { get; set; } = PanelMode.Auto;

	/// <summary>Gets or sets a value indicating whether the text needs rebuilding.</summary>
	public bool IsStale { get; set; } = true;

	/// <summary>Gets or sets the text last drawn.</summary>
	public string Text { get; set; } = string.Empty;

	/// <summary>Gets or sets when the center card was last sent.</summary>
	public double LastCenterSend { get; set; } = double.NegativeInfinity;
}

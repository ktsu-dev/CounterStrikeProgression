// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression;

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using ktsu.CounterStrikeProgression.Core.Events;
using ktsu.CounterStrikeProgression.Core.Players;
using ktsu.CounterStrikeProgression.Core.Recap;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// Reads players, weapons, and the match from the game into the plain values Core works with.
/// </summary>
internal static class GameState
{
	// Finding the game rules walks every entity, so it's done once per map rather than on every shot.
	private static CCSGameRulesProxy? gameRules;
	private static ConVar? teammatesAreEnemies;

	/// <summary>
	/// Gets a value indicating whether the match is in warmup, when nothing counts.
	/// </summary>
	public static bool IsWarmup
	{
		get
		{
			if ((gameRules is null) || !gameRules.IsValid)
			{
				gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
			}

			return gameRules?.GameRules?.WarmupPeriod ?? false;
		}
	}

	/// <summary>
	/// Gets a value indicating whether teammates can hurt each other for credit, as in free-for-all deathmatch.
	/// </summary>
	public static bool TeammatesAreEnemies
	{
		get
		{
			teammatesAreEnemies ??= ConVar.Find("mp_teammates_are_enemies");
			return teammatesAreEnemies?.GetPrimitiveValue<bool>() ?? false;
		}
	}

	/// <summary>
	/// Drops the cached game rules, which belong to the map that just ended.
	/// </summary>
	public static void ForgetMap() => gameRules = null;

	/// <summary>
	/// Gets how fast a victim is moving across the attacker's view: their horizontal speed perpendicular to
	/// the line between the two.
	/// </summary>
	/// <param name="attacker">The attacker's pawn.</param>
	/// <param name="victim">The victim's pawn.</param>
	/// <returns>The sideways speed in units per second, or 0 when either position is unknown.</returns>
	public static double SidewaysSpeed(CCSPlayerPawn? attacker, CCSPlayerPawn? victim)
	{
		Vector? from = attacker?.AbsOrigin;
		Vector? to = victim?.AbsOrigin;
		if ((from is null) || (to is null) || (victim is null))
		{
			return 0;
		}

		double dx = to.X - from.X;
		double dy = to.Y - from.Y;
		double length = Math.Sqrt((dx * dx) + (dy * dy));
		if (length < 1)
		{
			return 0;
		}

		Vector velocity = victim.AbsVelocity;
		return Math.Abs(((velocity.X * dy) - (velocity.Y * dx)) / length);
	}

	/// <summary>
	/// Gets the side a player is on for trades and clutches. When teammates are enemies every player is
	/// their own side.
	/// </summary>
	/// <param name="player">The player.</param>
	/// <param name="teammatesAreEnemies">Whether teammates are enemies.</param>
	/// <returns>An id for the player's side.</returns>
	public static int SideOf(CCSPlayerController player, bool teammatesAreEnemies) =>
		teammatesAreEnemies ? 1000 + player.Slot : player.TeamNum;

	/// <summary>
	/// Gets a value indicating whether a controller is a real, connected human.
	/// </summary>
	/// <param name="player">The controller.</param>
	/// <returns>Whether it's a human player.</returns>
	public static bool IsHuman(CCSPlayerController? player) =>
		(player is not null) && player.IsValid && !player.IsBot && !player.IsHLTV && (player.SteamID != 0);

	/// <summary>
	/// Gets a value indicating whether a controller is a player whose progress is kept: a human with a SteamID, or
	/// a bot with a name to key its persona by.
	/// </summary>
	/// <param name="player">The controller.</param>
	/// <returns>Whether the player can have a session.</returns>
	public static bool IsTracked(CCSPlayerController? player) =>
		(player is not null) && player.IsValid && !player.IsHLTV
		&& (player.IsBot ? !string.IsNullOrWhiteSpace(player.PlayerName) : player.SteamID != 0);

	/// <summary>
	/// Gets the id a player's progress is keyed by: the SteamID64 for a human, and the name's bot id for a bot.
	/// </summary>
	/// <param name="player">The controller.</param>
	/// <returns>The id, or 0 when there is no player or no usable id.</returns>
	public static ulong PlayerIdOf(CCSPlayerController? player) =>
		player is null ? 0 : PlayerIds.Of(player.IsBot, player.SteamID, player.PlayerName);

	/// <summary>
	/// Reads a player's kills and deaths off the scoreboard, which add up over the whole map.
	/// </summary>
	/// <param name="player">The controller.</param>
	/// <returns>The reading, or an empty one when the controller is gone or has no stats.</returns>
	public static Scoreboard ScoreboardOf(CCSPlayerController? player)
	{
		CSMatchStats_t? stats = player is { IsValid: true } ? player.ActionTrackingServices?.MatchStats : null;
		return new Scoreboard(stats?.Kills ?? 0, stats?.Deaths ?? 0);
	}

	/// <summary>
	/// Gets a player's live pawn.
	/// </summary>
	/// <param name="player">The controller.</param>
	/// <returns>The pawn, or <see langword="null"/> when there isn't a valid one.</returns>
	public static CCSPlayerPawn? PawnOf(CCSPlayerController? player)
	{
		CCSPlayerPawn? pawn = player?.PlayerPawn.Value;
		return (pawn is not null) && pawn.IsValid ? pawn : null;
	}

	/// <summary>
	/// Reads what a player is doing.
	/// </summary>
	/// <param name="pawn">The player's pawn.</param>
	/// <returns>The player's state.</returns>
	public static PlayerState StateOf(CCSPlayerPawn? pawn)
	{
		if (pawn is null)
		{
			return new PlayerState();
		}

		Vector velocity = pawn.AbsVelocity;
		PlayerFlags flags = (PlayerFlags)pawn.Flags;
		CCSPlayer_MovementServices? movement = pawn.MovementServices?.As<CCSPlayer_MovementServices>();
		return new PlayerState
		{
			Speed = Math.Sqrt((velocity.X * velocity.X) + (velocity.Y * velocity.Y)),
			OnGround = flags.HasFlag(PlayerFlags.FL_ONGROUND),
			Ducked = (movement?.Ducked ?? false) || flags.HasFlag(PlayerFlags.FL_DUCKING),
			OnLadder = pawn.ActualMoveType == MoveType_t.MOVETYPE_LADDER,
			InWater = flags.HasFlag(PlayerFlags.FL_INWATER),
			Walking = pawn.IsWalking,
			Scoped = pawn.IsScoped,
			Defusing = pawn.IsDefusing,
			Health = pawn.Health,
		};
	}

	/// <summary>
	/// Gets the distance between two pawns in game units.
	/// </summary>
	/// <param name="a">One pawn.</param>
	/// <param name="b">The other pawn.</param>
	/// <returns>The distance, or 0 when either position is unknown.</returns>
	public static double Distance(CCSPlayerPawn? a, CCSPlayerPawn? b) => Distance(a?.AbsOrigin, b?.AbsOrigin);

	/// <summary>
	/// Gets the distance between two positions in game units.
	/// </summary>
	/// <param name="from">One position.</param>
	/// <param name="to">The other position.</param>
	/// <returns>The distance, or 0 when either position is unknown.</returns>
	public static double Distance(Vector? from, Vector? to)
	{
		if ((from is null) || (to is null))
		{
			return 0;
		}

		double dx = from.X - to.X;
		double dy = from.Y - to.Y;
		double dz = from.Z - to.Z;
		return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
	}

	/// <summary>
	/// Gets the weapon a player is holding.
	/// </summary>
	/// <param name="pawn">The player's pawn.</param>
	/// <returns>The weapon entity, or <see langword="null"/>.</returns>
	public static CBasePlayerWeapon? ActiveWeapon(CCSPlayerPawn? pawn)
	{
		CBasePlayerWeapon? weapon = pawn?.WeaponServices?.ActiveWeapon.Value;
		return (weapon is not null) && weapon.IsValid ? weapon : null;
	}

	/// <summary>
	/// Gets the tracked weapon a player is holding, telling alternates such as the USP-S and P2000 apart.
	/// </summary>
	/// <param name="pawn">The player's pawn.</param>
	/// <returns>The weapon, or <see langword="null"/> when they hold nothing tracked.</returns>
	public static WeaponInfo? HeldWeapon(CCSPlayerPawn? pawn) =>
		ActiveWeapon(pawn) is { } held ? WeaponCatalog.Resolve(held.DesignerName, held.AttributeManager.Item.ItemDefinitionIndex) : null;

	/// <summary>
	/// Works out which tracked weapon an event's weapon name means, using the attacker's held weapon to tell
	/// alternates such as the USP-S and P2000 apart.
	/// </summary>
	/// <param name="reportedName">The weapon name in the event.</param>
	/// <param name="attackerPawn">The attacker's pawn.</param>
	/// <returns>The weapon, or <see langword="null"/> when it isn't tracked.</returns>
	public static WeaponInfo? ResolveWeapon(string? reportedName, CCSPlayerPawn? attackerPawn)
	{
		WeaponInfo? fromEvent = WeaponCatalog.Resolve(reportedName);
		if ((fromEvent is null) || !fromEvent.IsGun)
		{
			return fromEvent;
		}

		CBasePlayerWeapon? held = ActiveWeapon(attackerPawn);
		if (held is null)
		{
			return fromEvent;
		}

		// Only trust the held weapon when it's the same entity name the event reported, which is the case
		// where an alternate hides behind its base weapon's name.
		return WeaponCatalog.Resolve(held.DesignerName) == fromEvent
			? WeaponCatalog.Resolve(held.DesignerName, held.AttributeManager.Item.ItemDefinitionIndex) ?? fromEvent
			: fromEvent;
	}
}

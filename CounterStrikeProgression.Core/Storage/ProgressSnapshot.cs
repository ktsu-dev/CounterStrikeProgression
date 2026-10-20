// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Storage;

using ktsu.CounterStrikeProgression.Core.Maps;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>A player row as stored.</summary>
/// <param name="SteamId">The 64-bit Steam ID.</param>
/// <param name="Name">The last known name.</param>
/// <param name="RankXp">The rank XP.</param>
/// <param name="Rank">The rank index.</param>
/// <param name="FirstSeen">When the player was first seen, in UTC.</param>
/// <param name="LastSeen">When the player was last seen, in UTC.</param>
/// <param name="WelcomeBackXp">The welcome-back bonus still to pay, rounded up.</param>
internal sealed record PlayerRow(ulong SteamId, string Name, long RankXp, int Rank, DateTime FirstSeen, DateTime LastSeen, long WelcomeBackXp = 0);

/// <summary>A weapon row as stored.</summary>
/// <param name="SteamId">The 64-bit Steam ID.</param>
/// <param name="Weapon">The weapon key.</param>
/// <param name="Xp">The weapon XP.</param>
/// <param name="Level">The weapon level.</param>
/// <param name="Kills">Kills with the weapon.</param>
/// <param name="Headshots">Headshot kills with the weapon.</param>
/// <param name="Hits">Hits with the weapon.</param>
/// <param name="Shots">Shots fired with the weapon.</param>
internal sealed record WeaponRow(ulong SteamId, string Weapon, long Xp, int Level, long Kills, long Headshots, long Hits, long Shots);

/// <summary>A challenge row as stored.</summary>
/// <param name="SteamId">The 64-bit Steam ID.</param>
/// <param name="Weapon">The weapon key.</param>
/// <param name="Challenge">The challenge id.</param>
/// <param name="Count">The challenge count.</param>
/// <param name="Tier">The number of tiers finished.</param>
internal sealed record ChallengeRow(ulong SteamId, string Weapon, string Challenge, long Count, int Tier);

/// <summary>A contract row as stored.</summary>
/// <param name="SteamId">The 64-bit Steam ID.</param>
/// <param name="Period">The key of the day or week, such as <c>2026-10-17</c> or <c>2026-W42</c>.</param>
/// <param name="Contract">The contract id.</param>
/// <param name="Count">How many matching things the player has done.</param>
/// <param name="Completed">Whether the contract is finished and paid.</param>
internal sealed record ContractRow(ulong SteamId, string Period, string Contract, long Count, bool Completed);

/// <summary>A player's kills of one other human, as stored.</summary>
/// <param name="SteamId">The killer's 64-bit Steam ID.</param>
/// <param name="VictimId">The victim's 64-bit Steam ID.</param>
/// <param name="Kills">How many times the killer has killed the victim.</param>
internal sealed record RivalRow(ulong SteamId, ulong VictimId, long Kills);

/// <summary>A player's XP in one season, as stored.</summary>
/// <param name="SteamId">The 64-bit Steam ID.</param>
/// <param name="Start">The season's first day, at midnight UTC.</param>
/// <param name="Xp">The rank XP earned in the season.</param>
internal sealed record SeasonRow(ulong SteamId, DateTime Start, long Xp);

/// <summary>
/// The rows of one player that changed since the last save, copied on the game thread so the database
/// write can happen elsewhere. Every row holds full values, never increments, so saving the same snapshot
/// twice is harmless.
/// </summary>
/// <param name="Player">The player row, or <see langword="null"/> when it didn't change.</param>
/// <param name="Weapons">The weapon rows that changed.</param>
/// <param name="Challenges">The challenge rows that changed.</param>
internal sealed record ProgressSnapshot(PlayerRow? Player, IReadOnlyList<WeaponRow> Weapons, IReadOnlyList<ChallengeRow> Challenges)
{
	/// <summary>Gets the contract rows that changed.</summary>
	public IReadOnlyList<ContractRow> Contracts { get; init; } = [];

	/// <summary>
	/// Gets the season rows that changed: usually the current season alone, and the season before it too when it
	/// ended between saves.
	/// </summary>
	public IReadOnlyList<SeasonRow> Seasons { get; init; } = [];

	/// <summary>Gets the map rows that changed.</summary>
	public IReadOnlyList<MapRow> Maps { get; init; } = [];

	/// <summary>Gets the rival rows that changed.</summary>
	public IReadOnlyList<RivalRow> Rivals { get; init; } = [];

	/// <summary>Gets a value indicating whether there's nothing to save.</summary>
	public bool IsEmpty => (Player is null) && (Weapons.Count == 0) && (Challenges.Count == 0) && (Contracts.Count == 0) && (Seasons.Count == 0) && (Maps.Count == 0) && (Rivals.Count == 0);

	/// <summary>
	/// Copies a player's changed rows and clears their dirty flags.
	/// </summary>
	/// <param name="progress">The player's progress.</param>
	/// <returns>The rows to save.</returns>
	public static ProgressSnapshot Capture(PlayerProgress progress)
	{
		Ensure.NotNull(progress);

		PlayerRow? player = null;
		if (progress.IsDirty)
		{
			player = ToRow(progress);
			progress.IsDirty = false;
		}

		List<WeaponRow> weapons = [];
		List<ChallengeRow> challenges = [];
		foreach (WeaponProgress weapon in progress.Weapons.Values)
		{
			if (weapon.IsDirty)
			{
				weapons.Add(new(progress.SteamId, weapon.Weapon.WeakString, weapon.Xp, weapon.Level, weapon.Kills, weapon.Headshots, weapon.Hits, weapon.Shots));
				weapon.IsDirty = false;
			}

			foreach ((string id, ChallengeProgress challenge) in weapon.Challenges)
			{
				if (challenge.IsDirty)
				{
					challenges.Add(new(progress.SteamId, weapon.Weapon.WeakString, id, challenge.Count, challenge.TierReached));
					challenge.IsDirty = false;
				}
			}
		}

		List<ContractRow> contracts = [];
		foreach (((string period, string id), ContractProgress contract) in progress.Contracts)
		{
			if (contract.IsDirty)
			{
				contracts.Add(new(progress.SteamId, period, id, contract.Count, contract.Completed));
				contract.IsDirty = false;
			}
		}

		List<SeasonRow> seasons = [];
		if (progress.EndedSeason is { } ended)
		{
			seasons.Add(new(progress.SteamId, ended.Start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), ended.Xp));
			progress.EndedSeason = null;
		}

		if (progress.IsSeasonDirty && (ToSeasonRow(progress) is { } season))
		{
			seasons.Add(season);
			progress.IsSeasonDirty = false;
		}

		List<MapRow> maps = [];
		foreach ((string map, MapProgress totals) in progress.Maps)
		{
			if (totals.IsDirty)
			{
				maps.Add(ToMapRow(progress.SteamId, map, totals));
				totals.IsDirty = false;
			}
		}

		List<RivalRow> rivals = [];
		foreach ((ulong victim, RivalProgress rival) in progress.Victims)
		{
			if (rival.IsDirty)
			{
				rivals.Add(new(progress.SteamId, victim, rival.Kills));
				rival.IsDirty = false;
			}
		}

		return new(player, weapons, challenges) { Contracts = contracts, Seasons = seasons, Maps = maps, Rivals = rivals };
	}

	/// <summary>
	/// Copies every row of a player, whether or not it changed. The dirty flags that saving relies on stay as they are.
	/// </summary>
	/// <param name="progress">The player's progress.</param>
	/// <returns>All of the player's rows.</returns>
	public static ProgressSnapshot CaptureAll(PlayerProgress progress)
	{
		Ensure.NotNull(progress);

		List<WeaponRow> weapons = [];
		List<ChallengeRow> challenges = [];
		foreach (WeaponProgress weapon in progress.Weapons.Values)
		{
			weapons.Add(new(progress.SteamId, weapon.Weapon.WeakString, weapon.Xp, weapon.Level, weapon.Kills, weapon.Headshots, weapon.Hits, weapon.Shots));
			foreach ((string id, ChallengeProgress challenge) in weapon.Challenges)
			{
				challenges.Add(new(progress.SteamId, weapon.Weapon.WeakString, id, challenge.Count, challenge.TierReached));
			}
		}

		List<ContractRow> contracts = [.. progress.Contracts.Select(c => new ContractRow(progress.SteamId, c.Key.Period, c.Key.Contract, c.Value.Count, c.Value.Completed))];
		List<MapRow> maps = [.. progress.Maps.Select(m => ToMapRow(progress.SteamId, m.Key, m.Value))];
		List<RivalRow> rivals = [.. progress.Victims.Select(v => new RivalRow(progress.SteamId, v.Key, v.Value.Kills))];
		return new(ToRow(progress), weapons, challenges) { Contracts = contracts, Seasons = ToSeasonRow(progress) is { } season ? [season] : [], Maps = maps, Rivals = rivals };
	}

	/// <summary>
	/// Makes the row for a player's latest season.
	/// </summary>
	/// <param name="progress">The player's progress.</param>
	/// <returns>The row, or <see langword="null"/> when the player hasn't earned XP in any season.</returns>
	public static SeasonRow? ToSeasonRow(PlayerProgress progress)
	{
		Ensure.NotNull(progress);
		return progress.SeasonStart is { } start
			? new(progress.SteamId, start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), progress.SeasonXp)
			: null;
	}

	/// <summary>
	/// Makes a player row from progress, whether or not it changed.
	/// </summary>
	/// <param name="progress">The player's progress.</param>
	/// <returns>The row.</returns>
	public static PlayerRow ToRow(PlayerProgress progress)
	{
		Ensure.NotNull(progress);
		return new(progress.SteamId, progress.Name, progress.RankXp, progress.Rank, progress.FirstSeen, progress.LastSeen, (long)Math.Ceiling(progress.WelcomeBackXp));
	}

	/// <summary>
	/// Makes the row for a player's totals on one map.
	/// </summary>
	/// <param name="steamId">The player's 64-bit Steam ID.</param>
	/// <param name="map">The map's stored name.</param>
	/// <param name="totals">The player's totals on it.</param>
	/// <returns>The row.</returns>
	public static MapRow ToMapRow(ulong steamId, string map, MapProgress totals)
	{
		Ensure.NotNull(totals);
		return new(steamId, map, totals.Xp, totals.Kills, totals.Deaths, totals.Seconds, totals.Sessions);
	}

	/// <summary>
	/// Flags this snapshot's rows as dirty again after a failed save, so the next save retries them.
	/// </summary>
	/// <param name="progress">The same player's progress.</param>
	public void Restore(PlayerProgress progress)
	{
		Ensure.NotNull(progress);

		if (Player is not null)
		{
			progress.IsDirty = true;
		}

		foreach (WeaponRow row in Weapons)
		{
			progress.Weapon(WeaponKey.Create(row.Weapon)).IsDirty = true;
		}

		foreach (ChallengeRow row in Challenges)
		{
			progress.Weapon(WeaponKey.Create(row.Weapon)).Challenge(row.Challenge).IsDirty = true;
		}

		foreach (ContractRow row in Contracts)
		{
			progress.Contract(row.Period, row.Contract).IsDirty = true;
		}

		foreach (MapRow row in Maps)
		{
			progress.Map(row.Map).IsDirty = true;
		}

		foreach (RivalRow row in Rivals)
		{
			progress.Victim(row.VictimId).IsDirty = true;
		}

		foreach (SeasonRow row in Seasons)
		{
			DateOnly start = DateOnly.FromDateTime(row.Start);
			if (progress.SeasonStart == start)
			{
				progress.IsSeasonDirty = true;
			}
			else
			{
				progress.EndedSeason ??= (start, row.Xp);
			}
		}
	}

	/// <summary>
	/// Rebuilds a player's progress from stored rows.
	/// </summary>
	/// <param name="player">The player row.</param>
	/// <param name="weapons">The player's weapon rows.</param>
	/// <param name="challenges">The player's challenge rows.</param>
	/// <param name="season">The player's latest season row, if they have one.</param>
	/// <param name="contracts">The player's contract rows, if any.</param>
	/// <param name="maps">The player's map rows, if any.</param>
	/// <param name="daily">The player's daily rows, earliest first, which the play streak is worked out from.</param>
	/// <returns>The progress, with nothing flagged dirty.</returns>
	public static PlayerProgress Rebuild(PlayerRow player, IEnumerable<WeaponRow> weapons, IEnumerable<ChallengeRow> challenges, SeasonRow? season = null, IEnumerable<ContractRow>? contracts = null, IEnumerable<MapRow>? maps = null, IEnumerable<DailyRow>? daily = null)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(weapons);
		Ensure.NotNull(challenges);

		PlayerProgress progress = new(player.SteamId)
		{
			Name = player.Name,
			RankXp = player.RankXp,
			Rank = player.Rank,
			FirstSeen = player.FirstSeen,
			LastSeen = player.LastSeen,
			WelcomeBackXp = player.WelcomeBackXp,
			SeasonStart = season is null ? null : DateOnly.FromDateTime(season.Start),
			SeasonXp = season?.Xp ?? 0,
			Streak = PlayStreak.FromHistory([.. (daily ?? []).Select(r => new DailyXp(DateOnly.FromDateTime(r.Day), r.RankXp))], player.FirstSeen),
		};

		foreach (WeaponRow row in weapons)
		{
			if (!WeaponKey.TryCreate(row.Weapon, out WeaponKey? key) || key is null)
			{
				continue;
			}

			WeaponProgress weapon = progress.Weapon(key);
			weapon.Xp = row.Xp;
			weapon.Level = row.Level;
			weapon.Kills = row.Kills;
			weapon.Headshots = row.Headshots;
			weapon.Hits = row.Hits;
			weapon.Shots = row.Shots;
		}

		foreach (ChallengeRow row in challenges)
		{
			if (!WeaponKey.TryCreate(row.Weapon, out WeaponKey? key) || key is null)
			{
				continue;
			}

			ChallengeProgress challenge = progress.Weapon(key).Challenge(row.Challenge);
			challenge.Count = row.Count;
			challenge.TierReached = row.Tier;
		}

		foreach (ContractRow row in contracts ?? [])
		{
			ContractProgress contract = progress.Contract(row.Period, row.Contract);
			contract.Count = row.Count;
			contract.Completed = row.Completed;
		}

		foreach (MapRow row in maps ?? [])
		{
			MapProgress totals = progress.Map(row.Map);
			totals.Xp = row.Xp;
			totals.Kills = row.Kills;
			totals.Deaths = row.Deaths;
			totals.Seconds = row.Seconds;
			totals.Sessions = row.Sessions;
		}

		return progress;
	}
}

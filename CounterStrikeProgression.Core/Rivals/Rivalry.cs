// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Rivals;

using ktsu.CounterStrikeProgression.Core.Progression;

/// <summary>
/// A player's record against one other human.
/// </summary>
/// <param name="SteamId">The other player's 64-bit Steam ID.</param>
/// <param name="Name">The other player's last known name.</param>
/// <param name="Kills">How many times the player has killed them.</param>
/// <param name="Deaths">How many times they have killed the player.</param>
internal sealed record HeadToHead(ulong SteamId, string Name, long Kills, long Deaths);

/// <summary>
/// Kills between pairs of human players: who each player kills most, and who kills them most. Each player's kills
/// are saved on their own <c>csp_rivals</c> rows, and the deaths are the other side of someone else's rows, kept in
/// memory so a nemesis can be found without a query. Counts only go up.
/// </summary>
internal static class Rivalry
{
	/// <summary>
	/// Counts a kill on both players.
	/// </summary>
	/// <param name="killer">The killer's progress.</param>
	/// <param name="victim">The victim's progress.</param>
	public static void Count(PlayerProgress killer, PlayerProgress victim)
	{
		Ensure.NotNull(killer);
		Ensure.NotNull(victim);

		RivalProgress row = killer.Victim(victim.SteamId);
		row.Kills++;
		row.IsDirty = true;
		victim.KilledBy[killer.SteamId] = Math.Max(victim.KilledBy.GetValueOrDefault(killer.SteamId) + 1, row.Kills);
		killer.RivalNames[victim.SteamId] = victim.Name;
		victim.RivalNames[killer.SteamId] = killer.Name;
	}

	/// <summary>
	/// Brings a joining player and the players already on the server up to date with each other. Kills made since the
	/// last save are only in memory, so a load from the database alone could miss them.
	/// </summary>
	/// <param name="joining">The joining player's progress, as loaded.</param>
	/// <param name="online">The progress of the players already on the server.</param>
	public static void Join(PlayerProgress joining, IEnumerable<PlayerProgress> online)
	{
		Ensure.NotNull(joining);
		Ensure.NotNull(online);

		foreach (PlayerProgress other in online)
		{
			if (other.SteamId == joining.SteamId)
			{
				continue;
			}

			if (other.Victims.TryGetValue(joining.SteamId, out RivalProgress? theirs) && (theirs.Kills > 0))
			{
				joining.KilledBy[other.SteamId] = Math.Max(joining.KilledBy.GetValueOrDefault(other.SteamId), theirs.Kills);
				joining.RivalNames[other.SteamId] = other.Name;
				other.RivalNames[joining.SteamId] = joining.Name;
			}

			if (joining.Victims.TryGetValue(other.SteamId, out RivalProgress? mine) && (mine.Kills > 0))
			{
				other.KilledBy[joining.SteamId] = Math.Max(other.KilledBy.GetValueOrDefault(joining.SteamId), mine.Kills);
				joining.RivalNames[other.SteamId] = other.Name;
				other.RivalNames[joining.SteamId] = joining.Name;
			}
		}
	}

	/// <summary>
	/// Fills a player's kills, deaths, and rivals' names from stored head-to-head rows.
	/// </summary>
	/// <param name="progress">The player's progress.</param>
	/// <param name="rows">The player's record against each other player.</param>
	public static void Load(PlayerProgress progress, IEnumerable<HeadToHead> rows)
	{
		Ensure.NotNull(progress);
		Ensure.NotNull(rows);

		foreach (HeadToHead row in rows)
		{
			if (row.SteamId == progress.SteamId)
			{
				continue;
			}

			if (row.Kills > 0)
			{
				progress.Victim(row.SteamId).Kills = row.Kills;
			}

			if (row.Deaths > 0)
			{
				progress.KilledBy[row.SteamId] = row.Deaths;
			}

			progress.RivalNames[row.SteamId] = row.Name;
		}
	}

	/// <summary>
	/// Gets a player's record against everyone they've killed or been killed by.
	/// </summary>
	/// <param name="progress">The player's progress.</param>
	/// <returns>One row per other player, most kills and deaths together first.</returns>
	public static IReadOnlyList<HeadToHead> Of(PlayerProgress progress)
	{
		Ensure.NotNull(progress);
		return
		[
			.. progress.Victims.Where(v => v.Value.Kills > 0).Select(v => v.Key)
				.Union(progress.KilledBy.Where(k => k.Value > 0).Select(k => k.Key))
				.Select(id => new HeadToHead(
					id,
					progress.RivalNames.GetValueOrDefault(id) ?? string.Empty,
					progress.Victims.GetValueOrDefault(id)?.Kills ?? 0,
					progress.KilledBy.GetValueOrDefault(id)))
				.OrderByDescending(h => h.Kills + h.Deaths)
				.ThenBy(h => h.SteamId),
		];
	}

	/// <summary>
	/// Gets a player's nemesis: whoever has killed them most, at least <paramref name="minimumKills"/> times. A tie
	/// goes to the more one-sided record.
	/// </summary>
	/// <param name="rows">The player's record against each other player.</param>
	/// <param name="minimumKills">The fewest kills that make someone a nemesis.</param>
	/// <returns>The nemesis, or <see langword="null"/> when nobody has killed the player that often.</returns>
	public static HeadToHead? Nemesis(IEnumerable<HeadToHead> rows, int minimumKills)
	{
		Ensure.NotNull(rows);
		return rows
			.Where(r => r.Deaths >= Math.Max(1, minimumKills))
			.OrderByDescending(r => r.Deaths)
			.ThenBy(r => r.Kills)
			.ThenBy(r => r.SteamId)
			.FirstOrDefault();
	}

	/// <summary>
	/// Gets a player's favourite victim: whoever they've killed most, at least <paramref name="minimumKills"/> times.
	/// A tie goes to the more one-sided record.
	/// </summary>
	/// <param name="rows">The player's record against each other player.</param>
	/// <param name="minimumKills">The fewest kills that make someone a victim.</param>
	/// <returns>The victim, or <see langword="null"/> when the player hasn't killed anyone that often.</returns>
	public static HeadToHead? Victim(IEnumerable<HeadToHead> rows, int minimumKills)
	{
		Ensure.NotNull(rows);
		return rows
			.Where(r => r.Kills >= Math.Max(1, minimumKills))
			.OrderByDescending(r => r.Kills)
			.ThenBy(r => r.Deaths)
			.ThenBy(r => r.SteamId)
			.FirstOrDefault();
	}
}

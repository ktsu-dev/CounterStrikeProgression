// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Views;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;

/// <summary>
/// Turns the stored rankings and the live overlay into the rows of the server ranks page.
/// </summary>
internal static class RanksViewBuilder
{
	/// <summary>
	/// Builds the ranks rows.
	/// </summary>
	/// <param name="stored">The rankings read from the database.</param>
	/// <param name="overlay">Gets the live rows received for a player.</param>
	/// <param name="online">Tells whether a player is on the server now.</param>
	/// <param name="settings">The plugin's settings, used to rank by the ladder. Without them the stored rank is used.</param>
	/// <param name="overlayIds">The Steam IDs that have live rows, so a player not yet saved still appears.</param>
	/// <param name="feats">Gets the ids of the feats a player has earned. Omitted, no row lists any.</param>
	/// <returns>The rows, highest rank XP first, numbered from 1.</returns>
	public static IReadOnlyList<RankRow> Build(
		IReadOnlyList<RankingEntry> stored,
		Func<ulong, ProgressSnapshot?> overlay,
		Func<ulong, bool> online,
		ProgressionSettings? settings = null,
		IEnumerable<ulong>? overlayIds = null,
		Func<ulong, IReadOnlyList<string>>? feats = null)
	{
		Ensure.NotNull(stored);
		Ensure.NotNull(overlay);
		Ensure.NotNull(online);

		RankLadder? ladder = settings is null ? null : new RankLadder(settings.FirstRankXp, settings.RankGrowth);
		Dictionary<ulong, RankingEntry> entries = [];
		foreach (RankingEntry entry in stored)
		{
			entries[entry.SteamId] = entry;
		}

		foreach (ulong steamId in overlayIds ?? [])
		{
			if (!entries.ContainsKey(steamId) && overlay(steamId)?.Player is { } player)
			{
				entries[steamId] = new(steamId, player.Name, player.RankXp, player.Rank, player.LastSeen);
			}
		}

		List<(RankingEntry Entry, int Rank)> merged = [];
		foreach (RankingEntry entry in entries.Values)
		{
			RankingEntry current = entry;
			if (overlay(entry.SteamId)?.Player is { } live)
			{
				current = entry with
				{
					Name = string.IsNullOrEmpty(live.Name) ? entry.Name : live.Name,
					RankXp = Math.Max(entry.RankXp, live.RankXp),
					Rank = Math.Max(entry.Rank, live.Rank),
					LastSeen = entry.LastSeen >= live.LastSeen ? entry.LastSeen : live.LastSeen,
				};
			}

			int ladderRank = ladder?.RankFor(current.RankXp) ?? 0;
			merged.Add((current, Math.Clamp(Math.Max(ladderRank, current.Rank), 0, RankLadder.TopRank)));
		}

		List<RankRow> rows = [];
		foreach ((RankingEntry entry, int rank) in merged.OrderByDescending(m => m.Entry.RankXp).ThenBy(m => m.Entry.SteamId))
		{
			rows.Add(new(
				rows.Count + 1,
				entry.SteamId.ToString(CultureInfo.InvariantCulture),
				entry.Name,
				rank,
				RankLadder.Names[rank],
				entry.RankXp,
				entry.LastSeen,
				online(entry.SteamId),
				feats?.Invoke(entry.SteamId) ?? []));
		}

		return rows;
	}
}

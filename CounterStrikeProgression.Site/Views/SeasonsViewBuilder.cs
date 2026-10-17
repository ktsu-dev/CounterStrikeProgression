// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Views;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Rotation;
using ktsu.CounterStrikeProgression.Core.Storage;

/// <summary>One row of a season's standings.</summary>
/// <param name="Position">The place in the season, from 1.</param>
/// <param name="SteamId">The 64-bit Steam ID as a decimal string.</param>
/// <param name="Name">The player's last known name.</param>
/// <param name="Rank">The player's lifetime rank index.</param>
/// <param name="RankName">The lifetime rank name.</param>
/// <param name="Xp">The rank XP earned in the season.</param>
/// <param name="Online">Whether the player is on the server now.</param>
internal sealed record SeasonRowView(int Position, string SteamId, string Name, int Rank, string RankName, long Xp, bool Online);

/// <summary>One season's standings.</summary>
/// <param name="Name">The season's name, such as <c>Oct–Dec 2026</c>.</param>
/// <param name="Start">The season's first day.</param>
/// <param name="End">The day after the season's last day.</param>
/// <param name="Current">Whether the season is running now.</param>
/// <param name="Previous">The first day of the season before, for the page's link back.</param>
/// <param name="Next">The first day of the season after, or <see langword="null"/> for the current season.</param>
/// <param name="Rows">The standings, most season XP first.</param>
internal sealed record SeasonView(string Name, DateOnly Start, DateOnly End, bool Current, DateOnly Previous, DateOnly? Next, IReadOnlyList<SeasonRowView> Rows);

/// <summary>A season badge on a player's page.</summary>
/// <param name="Season">The season's name.</param>
/// <param name="Start">The season's first day.</param>
/// <param name="Place">The player's place in the season, from 1.</param>
/// <param name="Players">How many players earned XP in the season.</param>
/// <param name="Xp">The rank XP the player earned in the season.</param>
/// <param name="Current">Whether the season is still running, so the place can still change.</param>
internal sealed record SeasonBadge(string Season, DateOnly Start, long Place, long Players, long Xp, bool Current);

/// <summary>A player's seasons.</summary>
/// <param name="Season">The current season's name.</param>
/// <param name="Xp">The rank XP the player has earned this season.</param>
/// <param name="Place">The player's place this season, or <see langword="null"/> before they've earned any.</param>
/// <param name="Players">How many players have earned XP this season.</param>
/// <param name="Badges">The current season when the player has played it, then every top 3 finish in an ended season, latest first.</param>
internal sealed record PlayerSeasonsView(string Season, long Xp, long? Place, long Players, IReadOnlyList<SeasonBadge> Badges);

/// <summary>
/// Turns stored season XP and the live overlay into the seasons page and the player page's season badges.
/// </summary>
internal static class SeasonsViewBuilder
{
	/// <summary>The places that earn a badge for an ended season.</summary>
	public const int BadgePlaces = 3;

	/// <summary>
	/// Builds one season's standings. Live rows count only for the current season.
	/// </summary>
	/// <param name="season">The season.</param>
	/// <param name="current">The season running now.</param>
	/// <param name="settings">The plugin's settings, for the season length and the ladder.</param>
	/// <param name="stored">The standings read from the database.</param>
	/// <param name="overlay">Gets the live rows received for a player.</param>
	/// <param name="online">Tells whether a player is on the server now.</param>
	/// <param name="overlayIds">The Steam IDs that have live rows, so a player not yet saved still appears.</param>
	/// <returns>The season's standings.</returns>
	public static SeasonView Build(
		Season season,
		Season current,
		ProgressionSettings settings,
		IReadOnlyList<SeasonStanding> stored,
		Func<ulong, ProgressSnapshot?> overlay,
		Func<ulong, bool> online,
		IEnumerable<ulong>? overlayIds = null)
	{
		Ensure.NotNull(season);
		Ensure.NotNull(current);
		Ensure.NotNull(settings);
		Ensure.NotNull(stored);
		Ensure.NotNull(overlay);
		Ensure.NotNull(online);

		bool isCurrent = season.Start == current.Start;
		Dictionary<ulong, SeasonStanding> entries = stored.ToDictionary(s => s.SteamId);
		if (isCurrent)
		{
			foreach (ulong steamId in entries.Keys.Union(overlayIds ?? []).ToList())
			{
				if (overlay(steamId) is { } live && LiveSeasonXp(live, season.Start) is var liveXp and > 0)
				{
					entries[steamId] = WithLive(entries.GetValueOrDefault(steamId), steamId, live.Player, liveXp);
				}
			}
		}

		List<SeasonRowView> rows = [];
		foreach (SeasonStanding entry in entries.Values.OrderByDescending(e => e.Xp).ThenBy(e => e.SteamId))
		{
			int rank = Math.Clamp(entry.Rank, 0, RankLadder.TopRank);
			rows.Add(new(
				rows.Count + 1,
				entry.SteamId.ToString(CultureInfo.InvariantCulture),
				entry.Name,
				rank,
				RankLadder.Names[rank],
				entry.Xp,
				online(entry.SteamId)));
		}

		DateOnly previous = season.Previous(settings.SeasonMonths).Start;
		DateOnly? next = isCurrent ? null : Season.For(season.End, settings.SeasonMonths).Start;
		return new(season.Name, season.Start, season.End, isCurrent, previous, next, rows);
	}

	/// <summary>
	/// Builds a player's season badges.
	/// </summary>
	/// <param name="steamId">The player's 64-bit Steam ID.</param>
	/// <param name="seasonXp">The player's XP this season, with the live rows merged in.</param>
	/// <param name="current">The current season's standings, with the live rows merged in.</param>
	/// <param name="results">Where the player finished in each season, read from the database.</param>
	/// <param name="settings">The plugin's settings, for the season length.</param>
	/// <returns>The badges.</returns>
	public static PlayerSeasonsView BuildPlayer(ulong steamId, long seasonXp, SeasonView current, IReadOnlyList<SeasonResult> results, ProgressionSettings settings)
	{
		Ensure.NotNull(current);
		Ensure.NotNull(results);
		Ensure.NotNull(settings);

		string id = steamId.ToString(CultureInfo.InvariantCulture);
		SeasonRowView? row = current.Rows.FirstOrDefault(r => r.SteamId == id);
		SeasonResult? stored = results.FirstOrDefault(r => DateOnly.FromDateTime(r.Start) == current.Start);
		long xp = Math.Max(seasonXp, row?.Xp ?? 0);
		long? place = row?.Position ?? stored?.Place;
		long players = Math.Max(current.Rows.Count, stored?.Players ?? 0);

		List<SeasonBadge> badges = [];
		if ((xp > 0) && (place is { } now))
		{
			badges.Add(new(current.Name, current.Start, now, players, xp, true));
		}

		foreach (SeasonResult result in results.Where(r => DateOnly.FromDateTime(r.Start) < current.Start && r.Place <= BadgePlaces))
		{
			Season season = Season.For(DateOnly.FromDateTime(result.Start), settings.SeasonMonths);
			badges.Add(new(season.Name, season.Start, result.Place, result.Players, result.Xp, false));
		}

		return new(current.Name, xp, xp > 0 ? place : null, players, badges);
	}

	private static long LiveSeasonXp(ProgressSnapshot live, DateOnly start) =>
		live.Seasons.Where(s => DateOnly.FromDateTime(s.Start) == start).Select(s => s.Xp).DefaultIfEmpty(0).Max();

	private static SeasonStanding WithLive(SeasonStanding? stored, ulong steamId, PlayerRow? player, long liveXp)
	{
		if (stored is null)
		{
			return new(steamId, player?.Name ?? string.Empty, player?.Rank ?? 0, liveXp);
		}

		if (player is null)
		{
			return stored with { Xp = Math.Max(stored.Xp, liveXp) };
		}

		return stored with
		{
			Name = string.IsNullOrEmpty(player.Name) ? stored.Name : player.Name,
			Rank = Math.Max(stored.Rank, player.Rank),
			Xp = Math.Max(stored.Xp, liveXp),
		};
	}
}

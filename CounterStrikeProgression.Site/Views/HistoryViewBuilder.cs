// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Views;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;

/// <summary>A player's XP graph.</summary>
/// <param name="Days">One point per day, earliest first, ending today.</param>
/// <param name="Today">The rank XP earned today, in UTC.</param>
/// <param name="ThisWeek">The rank XP earned since Monday, in UTC.</param>
internal sealed record HistoryView(IReadOnlyList<XpDay> Days, long Today, long ThisWeek);

/// <summary>One row of the "XP this week" board.</summary>
/// <param name="Position">The place on the board, from 1.</param>
/// <param name="SteamId">The 64-bit Steam ID as a decimal string.</param>
/// <param name="Name">The player's last known name.</param>
/// <param name="Rank">The rank index.</param>
/// <param name="RankName">The rank name.</param>
/// <param name="RankXp">The player's total rank XP.</param>
/// <param name="Gained">The rank XP earned this week.</param>
/// <param name="Online">Whether the player is on the server now.</param>
internal sealed record WeeklyRow(int Position, string SteamId, string Name, int Rank, string RankName, long RankXp, long Gained, bool Online);

/// <summary>The "XP this week" board.</summary>
/// <param name="Start">The Monday the week started, in UTC.</param>
/// <param name="Rows">The players who earned XP this week, most first.</param>
internal sealed record WeeklyView(DateOnly Start, IReadOnlyList<WeeklyRow> Rows);

/// <summary>
/// Builds the XP graph and the weekly board from the stored daily rows, with live progress laid over them so
/// both move while a match runs.
/// </summary>
internal static class HistoryViewBuilder
{
	/// <summary>The most days a graph shows.</summary>
	public const int MaxDays = 365;

	/// <summary>How many days a graph shows when none are asked for.</summary>
	public const int DefaultDays = 30;

	/// <summary>How many players the weekly board shows.</summary>
	public const int WeeklyCount = 10;

	/// <summary>
	/// Builds a player's XP graph.
	/// </summary>
	/// <param name="rows">The player's stored daily rows from <see cref="ProgressFrom"/> on, earliest first.</param>
	/// <param name="merged">The player's stored and live progress, merged.</param>
	/// <param name="today">The current UTC day.</param>
	/// <param name="days">How many days to show, ending today.</param>
	/// <returns>The graph.</returns>
	public static HistoryView Build(IReadOnlyList<DailyXp> rows, PlayerProgress merged, DateOnly today, int days)
	{
		Ensure.NotNull(rows);
		Ensure.NotNull(merged);

		IReadOnlyList<XpDay> points = XpHistory.Days(rows, today.AddDays(1 - days), today, merged.FirstSeen, merged.RankXp);
		long thisWeek = XpHistory.GainedSince(rows, XpHistory.WeekStart(today), merged.FirstSeen, merged.RankXp);
		long todayXp = points.Count > 0 ? points[^1].Gained : 0;
		return new(points, todayXp, thisWeek);
	}

	/// <summary>
	/// Gets the first day whose rows a graph of some length needs, which covers the week so far as well.
	/// </summary>
	/// <param name="today">The current UTC day.</param>
	/// <param name="days">How many days the graph shows.</param>
	/// <returns>The day to read rows from.</returns>
	public static DateOnly ProgressFrom(DateOnly today, int days)
	{
		DateOnly graphStart = today.AddDays(1 - days);
		DateOnly weekStart = XpHistory.WeekStart(today);
		return graphStart < weekStart ? graphStart : weekStart;
	}

	/// <summary>
	/// Builds the weekly board.
	/// </summary>
	/// <param name="start">The Monday the week started.</param>
	/// <param name="stored">The stored gains since <paramref name="start"/>.</param>
	/// <param name="overlay">Gets the live rows received for a player.</param>
	/// <param name="online">Tells whether a player is on the server now.</param>
	/// <param name="settings">The plugin's settings, used to rank by the ladder.</param>
	/// <param name="overlayIds">The Steam IDs that have live rows, so a player first seen this week who isn't saved yet still appears.</param>
	/// <returns>The board.</returns>
	public static WeeklyView BuildWeekly(
		DateOnly start,
		IReadOnlyList<XpGainEntry> stored,
		Func<ulong, ProgressSnapshot?> overlay,
		Func<ulong, bool> online,
		ProgressionSettings settings,
		IEnumerable<ulong> overlayIds)
	{
		Ensure.NotNull(stored);
		Ensure.NotNull(overlay);
		Ensure.NotNull(online);
		Ensure.NotNull(settings);
		Ensure.NotNull(overlayIds);

		Dictionary<ulong, XpGainEntry> entries = [];
		foreach (XpGainEntry entry in stored)
		{
			entries[entry.SteamId] = entry;
		}

		DateTime since = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
		foreach (ulong steamId in overlayIds)
		{
			if (!entries.ContainsKey(steamId) && overlay(steamId)?.Player is { } player && player.FirstSeen >= since && player.RankXp > 0)
			{
				entries[steamId] = new(steamId, player.Name, 0, player.Rank, 0);
			}
		}

		RankLadder ladder = new(settings.FirstRankXp, settings.RankGrowth);
		List<(XpGainEntry Entry, int Rank)> merged = [];
		foreach (XpGainEntry entry in entries.Values)
		{
			XpGainEntry current = entry;
			if (overlay(entry.SteamId)?.Player is { } live && live.RankXp > entry.RankXp)
			{
				current = entry with
				{
					Name = string.IsNullOrEmpty(live.Name) ? entry.Name : live.Name,
					RankXp = live.RankXp,
					Rank = Math.Max(entry.Rank, live.Rank),
					Gained = entry.Gained + (live.RankXp - entry.RankXp),
				};
			}

			merged.Add((current, Math.Clamp(Math.Max(ladder.RankFor(current.RankXp), current.Rank), 0, RankLadder.TopRank)));
		}

		List<WeeklyRow> rows = [];
		foreach ((XpGainEntry entry, int rank) in merged.OrderByDescending(m => m.Entry.Gained).ThenBy(m => m.Entry.SteamId).Take(WeeklyCount))
		{
			rows.Add(new(
				rows.Count + 1,
				entry.SteamId.ToString(CultureInfo.InvariantCulture),
				entry.Name,
				rank,
				RankLadder.Names[rank],
				entry.RankXp,
				entry.Gained,
				online(entry.SteamId)));
		}

		return new(start, rows);
	}
}

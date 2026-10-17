// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Storage;

/// <summary>
/// A player's rank XP at the end of one UTC day they played, or as it stands for today.
/// </summary>
/// <param name="Day">The UTC day.</param>
/// <param name="RankXp">The player's total rank XP.</param>
internal sealed record DailyXp(DateOnly Day, long RankXp);

/// <summary>
/// One day on a player's XP graph.
/// </summary>
/// <param name="Day">The UTC day.</param>
/// <param name="RankXp">The player's total rank XP at the end of the day.</param>
/// <param name="Gained">The rank XP earned that day.</param>
internal sealed record XpDay(DateOnly Day, long RankXp, long Gained);

/// <summary>
/// One row of a board ranked by the rank XP earned since a day, such as "XP this week".
/// </summary>
/// <param name="SteamId">The 64-bit Steam ID.</param>
/// <param name="Name">The player's last known name.</param>
/// <param name="RankXp">Their total rank XP.</param>
/// <param name="Rank">Their rank index.</param>
/// <param name="Gained">The rank XP earned since the start day.</param>
internal sealed record XpGainEntry(ulong SteamId, string Name, long RankXp, int Rank, long Gained);

/// <summary>
/// Reads XP earned over time out of the daily rows in <c>csp_daily</c>. Each row holds a player's total rank XP
/// at the end of a day they played, so the XP earned between two days is the difference between them, and a day
/// with no row earned nothing. Players who were already playing before the table existed get one starting row,
/// so their earlier XP never counts as earned on a later day.
/// </summary>
internal static class XpHistory
{
	/// <summary>
	/// Gets the start of the ISO week a day falls in, which is the Monday on or before it.
	/// </summary>
	/// <param name="day">The day.</param>
	/// <returns>The Monday.</returns>
	public static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

	/// <summary>
	/// Gets a player's rank XP just before a day, which is what XP earned from that day on is measured against.
	/// </summary>
	/// <param name="rows">The player's daily rows, earliest first.</param>
	/// <param name="start">The first day to count.</param>
	/// <param name="firstSeen">When the player was first seen, in UTC.</param>
	/// <returns>
	/// The rank XP of the latest row before <paramref name="start"/>; else 0 for a player first seen on or after it;
	/// else the earliest row's, since nothing earlier is known; or <see langword="null"/> when there are no rows at all.
	/// </returns>
	public static long? Baseline(IReadOnlyList<DailyXp> rows, DateOnly start, DateTime firstSeen)
	{
		Ensure.NotNull(rows);

		DailyXp? before = rows.LastOrDefault(r => r.Day < start);
		if (before is not null)
		{
			return before.RankXp;
		}

		if (DateOnly.FromDateTime(firstSeen) >= start)
		{
			return 0;
		}

		return rows.Count > 0 ? rows[0].RankXp : null;
	}

	/// <summary>
	/// Gets the rank XP a player earned from a day on.
	/// </summary>
	/// <param name="rows">The player's daily rows, earliest first.</param>
	/// <param name="start">The first day to count.</param>
	/// <param name="firstSeen">When the player was first seen, in UTC.</param>
	/// <param name="currentXp">The player's rank XP now.</param>
	/// <returns>The XP earned, never below 0.</returns>
	public static long GainedSince(IReadOnlyList<DailyXp> rows, DateOnly start, DateTime firstSeen, long currentXp) =>
		Math.Max(0, currentXp - (Baseline(rows, start, firstSeen) ?? currentXp));

	/// <summary>
	/// Lays a player's daily rows out as one point per day, from a day (or the day they were first seen, if later)
	/// to today. Days with no row carry the total forward and earned nothing.
	/// </summary>
	/// <param name="rows">The player's daily rows, earliest first.</param>
	/// <param name="from">The first day to show.</param>
	/// <param name="today">The last day to show.</param>
	/// <param name="firstSeen">When the player was first seen, in UTC.</param>
	/// <param name="currentXp">The player's rank XP now, which is today's total even before it's saved.</param>
	/// <returns>The days, earliest first.</returns>
	public static IReadOnlyList<XpDay> Days(IReadOnlyList<DailyXp> rows, DateOnly from, DateOnly today, DateTime firstSeen, long currentXp)
	{
		Ensure.NotNull(rows);

		DateOnly first = DateOnly.FromDateTime(firstSeen);
		DateOnly start = first > from ? first : from;
		if (start > today)
		{
			start = today;
		}

		Dictionary<DateOnly, long> byDay = rows.ToDictionary(r => r.Day, r => r.RankXp);
		long previous = Baseline(rows, start, firstSeen) ?? currentXp;
		List<XpDay> days = [];
		for (DateOnly day = start; day <= today; day = day.AddDays(1))
		{
			long total = day == today ? Math.Max(currentXp, byDay.GetValueOrDefault(day, previous)) : byDay.GetValueOrDefault(day, previous);
			days.Add(new(day, total, Math.Max(0, total - previous)));
			previous = total;
		}

		return days;
	}
}

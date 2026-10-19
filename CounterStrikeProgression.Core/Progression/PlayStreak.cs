// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Progression;

using ktsu.CounterStrikeProgression.Core.Storage;

/// <summary>
/// A player's run of consecutive UTC days with any rank XP. Nothing extra is saved: the streak is worked out from
/// the daily rows in <c>csp_daily</c> when a player loads, and kept up as XP arrives.
/// </summary>
/// <param name="LastDay">The latest day with rank XP, or <see langword="null"/> before the player has earned any.</param>
/// <param name="Length">The number of consecutive days ending on <paramref name="LastDay"/>.</param>
/// <param name="Best">The longest run the player has had.</param>
internal readonly record struct PlayStreak(DateOnly? LastDay, int Length, int Best)
{
	/// <summary>
	/// Gets the streak as it stands on a day. A streak stays alive until the end of the day after its last day,
	/// so a player who played yesterday still holds it today.
	/// </summary>
	/// <param name="today">The current UTC day.</param>
	/// <returns>The streak's length, or 0 once it has broken.</returns>
	public int CurrentOn(DateOnly today) => LastDay is { } last && last >= today.AddDays(-1) ? Length : 0;

	/// <summary>
	/// Gets a value indicating whether the player has already earned rank XP on a day.
	/// </summary>
	/// <param name="today">The current UTC day.</param>
	/// <returns><see langword="true"/> when the streak already counts the day.</returns>
	public bool PlayedOn(DateOnly today) => LastDay == today;

	/// <summary>
	/// Counts a day with rank XP.
	/// </summary>
	/// <param name="day">The UTC day.</param>
	/// <returns>The streak with the day counted. A day already counted, or an earlier one, changes nothing.</returns>
	public PlayStreak Record(DateOnly day)
	{
		if (LastDay is { } last && day <= last)
		{
			return this;
		}

		int length = LastDay is { } previous && previous.AddDays(1) == day ? Length + 1 : 1;
		return new(day, length, Math.Max(Best, length));
	}

	/// <summary>
	/// Works a streak out from a player's daily rows. A day counts when its total is higher than the row before it.
	/// The earliest row counts only when it's the day the player was first seen with some XP, because a player who
	/// played before the table existed got a starting row that doesn't stand for a day they played.
	/// </summary>
	/// <param name="rows">The player's daily rows, earliest first.</param>
	/// <param name="firstSeen">When the player was first seen, in UTC.</param>
	/// <returns>The streak.</returns>
	public static PlayStreak FromHistory(IReadOnlyList<DailyXp> rows, DateTime firstSeen)
	{
		Ensure.NotNull(rows);

		PlayStreak streak = default;
		for (int i = 0; i < rows.Count; i++)
		{
			bool earned = i == 0
				? (rows[0].RankXp > 0) && (rows[0].Day == DateOnly.FromDateTime(firstSeen))
				: rows[i].RankXp > rows[i - 1].RankXp;
			if (earned)
			{
				streak = streak.Record(rows[i].Day);
			}
		}

		return streak;
	}
}

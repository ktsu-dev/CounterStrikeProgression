// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Rotation;

using System.Globalization;

/// <summary>
/// One season: a stretch of months whose XP is ranked on its own, while lifetime rank keeps going up.
/// </summary>
/// <param name="Start">The first day of the season.</param>
/// <param name="End">The day after the season's last day.</param>
internal sealed record Season(DateOnly Start, DateOnly End)
{
	/// <summary>
	/// Gets the season a day falls in. Seasons start on 1 January and every <paramref name="months"/> months after.
	/// </summary>
	/// <param name="day">The day.</param>
	/// <param name="months">How long a season lasts, in months. It must divide 12, so a season never crosses a year; other values count as 3.</param>
	/// <returns>The season.</returns>
	public static Season For(DateOnly day, int months)
	{
		int length = months is 1 or 2 or 3 or 4 or 6 or 12 ? months : 3;
		int index = (day.Month - 1) / length;
		DateOnly start = new(day.Year, (index * length) + 1, 1);
		return new(start, start.AddMonths(length));
	}

	/// <summary>
	/// Gets the season before this one.
	/// </summary>
	/// <param name="months">How long a season lasts, in months.</param>
	/// <returns>The previous season.</returns>
	public Season Previous(int months) => For(Start.AddDays(-1), months);

	/// <summary>Gets the name players see, such as <c>Oct–Dec 2026</c>.</summary>
	public string Name
	{
		get
		{
			DateOnly last = End.AddDays(-1);
			string first = Start.ToString("MMM", CultureInfo.InvariantCulture);
			string final = last.ToString("MMM", CultureInfo.InvariantCulture);
			string year = last.Year.ToString(CultureInfo.InvariantCulture);
			return (Start.Month == last.Month) ? $"{first} {year}" : $"{first}–{final} {year}";
		}
	}
}

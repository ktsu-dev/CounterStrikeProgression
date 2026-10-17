// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Views;

using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Progression;

/// <summary>
/// Builds the rank distribution: every step of the ladder in order, with how many players stand on each.
/// </summary>
internal static class LadderViewBuilder
{
	/// <summary>
	/// Builds the ladder steps.
	/// </summary>
	/// <param name="rows">Every player's ranks row, with the live rows already laid over the stored ones.</param>
	/// <param name="settings">The plugin's settings, for the XP each step needs.</param>
	/// <returns>Every step, lowest first.</returns>
	public static IReadOnlyList<LadderStep> Build(IReadOnlyList<RankRow> rows, ProgressionSettings settings)
	{
		Ensure.NotNull(rows);
		Ensure.NotNull(settings);

		RankLadder ladder = new(settings.FirstRankXp, settings.RankGrowth);
		int[] counts = new int[RankLadder.Names.Count];
		foreach (RankRow row in rows)
		{
			counts[Math.Clamp(row.Rank, 0, RankLadder.TopRank)]++;
		}

		return
		[
			.. Enumerable.Range(0, RankLadder.Names.Count).Select(rank => new LadderStep(
				rank,
				RankLadder.Names[rank],
				RankLadder.RankNames[rank / RankLadder.StagesPerRank],
				(rank % RankLadder.StagesPerRank) + 1,
				ladder.ThresholdFor(rank),
				counts[rank])),
		];
	}
}

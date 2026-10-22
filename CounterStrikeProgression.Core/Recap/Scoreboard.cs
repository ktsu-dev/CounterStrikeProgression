// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Recap;

/// <summary>
/// A player's kills and deaths as the game's scoreboard shows them, which add up over the whole map.
/// </summary>
/// <param name="Kills">Kills on the scoreboard.</param>
/// <param name="Deaths">Deaths on the scoreboard.</param>
internal readonly record struct Scoreboard(int Kills, int Deaths)
{
	/// <summary>
	/// Gets the kills and deaths added since an earlier reading. A count below the earlier one means the scoreboard
	/// was reset in between, so everything now on it is new.
	/// </summary>
	/// <param name="start">The earlier reading.</param>
	/// <returns>The kills and deaths since then, never below zero.</returns>
	public Scoreboard Since(Scoreboard start) => new(Count(Kills, start.Kills), Count(Deaths, start.Deaths));

	private static int Count(int now, int start) => now >= start ? now - start : Math.Max(0, now);
}

// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Live;

/// <summary>
/// Names CS2's game type and game mode pairs.
/// </summary>
internal static class GameModes
{
	/// <summary>
	/// Gets the display name for a game type and mode.
	/// </summary>
	/// <param name="gameType">The game type.</param>
	/// <param name="gameMode">The game mode.</param>
	/// <returns>The name, or "Custom" for a pair that isn't a stock mode.</returns>
	public static string Name(int gameType, int gameMode) => (gameType, gameMode) switch
	{
		(0, 0) => "Casual",
		(0, 1) => "Competitive",
		(0, 2) => "Wingman",
		(1, 0) => "Arms Race",
		(1, 2) => "Deathmatch",
		_ => "Custom",
	};
}

// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Views;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Rivals;

/// <summary>A player's record against one other player.</summary>
/// <param name="SteamId">The other player's 64-bit Steam ID as a decimal string.</param>
/// <param name="Name">The other player's last known name.</param>
/// <param name="Kills">How many times the player has killed them.</param>
/// <param name="Deaths">How many times they have killed the player.</param>
internal sealed record RivalView(string SteamId, string Name, long Kills, long Deaths);

/// <summary>A player's rivals.</summary>
/// <param name="Nemesis">Whoever has killed the player most, or <see langword="null"/> when nobody has done it often enough.</param>
/// <param name="Victim">Whoever the player has killed most, or <see langword="null"/> when they haven't killed anyone often enough.</param>
/// <param name="MinimumKills">The fewest kills that make a nemesis or a victim.</param>
/// <param name="Rivals">The players they've met most, by kills and deaths together.</param>
internal sealed record RivalsView(RivalView? Nemesis, RivalView? Victim, int MinimumKills, IReadOnlyList<RivalView> Rivals);

/// <summary>
/// Turns a player's stored head-to-head rows into the rivals the player page shows.
/// </summary>
internal static class RivalsViewBuilder
{
	/// <summary>How many rivals the list shows.</summary>
	public const int Shown = 10;

	/// <summary>
	/// Builds a player's rivals.
	/// </summary>
	/// <param name="rows">The player's record against each other player.</param>
	/// <param name="settings">The plugin's settings, which decide how many kills make a nemesis.</param>
	/// <returns>The rivals.</returns>
	public static RivalsView Build(IReadOnlyList<HeadToHead> rows, ProgressionSettings settings)
	{
		Ensure.NotNull(rows);
		Ensure.NotNull(settings);

		int minimum = Math.Max(1, settings.NemesisMinimumKills);
		return new(
			View(Rivalry.Nemesis(rows, minimum)),
			View(Rivalry.Victim(rows, minimum)),
			minimum,
			[.. rows.OrderByDescending(r => r.Kills + r.Deaths).ThenBy(r => r.SteamId).Take(Shown).Select(r => View(r)!)]);
	}

	private static RivalView? View(HeadToHead? row) =>
		row is null ? null : new(row.SteamId.ToString(CultureInfo.InvariantCulture), row.Name, row.Kills, row.Deaths);
}

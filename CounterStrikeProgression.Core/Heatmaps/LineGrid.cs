// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Heatmaps;

/// <summary>
/// One line on the heatmaps' Lines tab: from the cell a killer stood in to the cell the victim fell in, on the
/// <see cref="HeatmapGrid"/> grid, and how many kills ran between those two cells.
/// </summary>
/// <param name="X1">The killer's column, from 0 at the left.</param>
/// <param name="Y1">The killer's row, from 0 at the top.</param>
/// <param name="X2">The victim's column.</param>
/// <param name="Y2">The victim's row.</param>
/// <param name="Death">Whether the line is a player's death rather than their kill. Always false server-wide.</param>
/// <param name="Count">The kills along the line.</param>
internal readonly record struct HeatLine(int X1, int Y1, int X2, int Y2, bool Death, int Count);

/// <summary>
/// Turns kills into lines from killer to victim, with both ends snapped to <see cref="HeatmapGrid"/> cells, so no
/// exact position leaves the server. Kills between the same two cells merge into one line with a count.
/// </summary>
internal static class LineGrid
{
	/// <summary>
	/// The most lines built for one view. It bounds the payload and the page's canvas pass for a full map, where most of
	/// the server's kills are distinct segments, and the lines past it are the least used.
	/// </summary>
	public const int MaxLines = 20_000;

	/// <summary>
	/// Builds the lines for the server or one player, keeping the busiest <see cref="MaxLines"/>. Suicides, and kills with
	/// either end off the radar image, are left out.
	/// </summary>
	/// <param name="kills">The kills.</param>
	/// <param name="steamId">
	/// The player whose kills and deaths to draw, or <see langword="null"/> for every kill. With a player, kills they're
	/// not in are left out, and the kills of them are marked <see cref="HeatLine.Death"/>.
	/// </param>
	/// <param name="overview">How the radar lines up with the world.</param>
	/// <returns>The lines, most kills first.</returns>
	public static IReadOnlyList<HeatLine> Build(IEnumerable<KillPoint> kills, ulong? steamId, MapOverview overview) =>
		Build(kills, steamId, overview, MaxLines);

	/// <summary>
	/// Builds the lines for the server or one player, keeping the busiest <paramref name="maxLines"/>. Suicides, and kills
	/// with either end off the radar image, are left out.
	/// </summary>
	/// <param name="kills">The kills.</param>
	/// <param name="steamId">
	/// The player whose kills and deaths to draw, or <see langword="null"/> for every kill. With a player, kills they're
	/// not in are left out, and the kills of them are marked <see cref="HeatLine.Death"/>.
	/// </param>
	/// <param name="overview">How the radar lines up with the world.</param>
	/// <param name="maxLines">The most lines to keep.</param>
	/// <returns>The lines, most kills first.</returns>
	public static IReadOnlyList<HeatLine> Build(IEnumerable<KillPoint> kills, ulong? steamId, MapOverview overview, int maxLines)
	{
		Ensure.NotNull(kills);
		Ensure.NotNull(overview);

		Dictionary<(int X1, int Y1, int X2, int Y2, bool Death), int> counts = [];
		foreach (KillPoint kill in kills)
		{
			if (kill.KillerId == kill.VictimId)
			{
				continue;
			}

			bool death = false;
			if (steamId is ulong id)
			{
				if (kill.VictimId == id)
				{
					death = true;
				}
				else if (kill.KillerId != id)
				{
					continue;
				}
			}

			if ((CellOf(overview, kill.KillerX, kill.KillerY) is not (int x1, int y1)) || (CellOf(overview, kill.VictimX, kill.VictimY) is not (int x2, int y2)))
			{
				continue;
			}

			(int, int, int, int, bool) key = (x1, y1, x2, y2, death);
			counts[key] = counts.GetValueOrDefault(key) + 1;
		}

		return [.. counts
			.Select(c => new HeatLine(c.Key.X1, c.Key.Y1, c.Key.X2, c.Key.Y2, c.Key.Death, c.Value))
			.OrderByDescending(l => l.Count)
			.ThenBy(l => l.Y1)
			.ThenBy(l => l.X1)
			.ThenBy(l => l.Y2)
			.ThenBy(l => l.X2)
			.ThenBy(l => l.Death)
			.Take(maxLines)];
	}

	private static (int X, int Y)? CellOf(MapOverview overview, int x, int y)
	{
		(double px, double py) = overview.ToPixel(x, y);
		return px is < 0 or >= MapOverview.Size || py is < 0 or >= MapOverview.Size
			? null
			: ((int)(px / HeatmapGrid.CellPixels), (int)(py / HeatmapGrid.CellPixels));
	}
}

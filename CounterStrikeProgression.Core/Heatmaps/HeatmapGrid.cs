// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Heatmaps;

/// <summary>One cell of a heatmap: its column and row on the radar grid, and how many kills fell in it.</summary>
/// <param name="X">The column, from 0 at the left.</param>
/// <param name="Y">The row, from 0 at the top.</param>
/// <param name="Count">The kills in the cell.</param>
internal readonly record struct HeatCell(int X, int Y, int Count);

/// <summary>
/// Counts positions into square cells over the radar image, so the site sends a few thousand cells rather
/// than every kill, and no exact position leaves the server.
/// </summary>
internal static class HeatmapGrid
{
	/// <summary>The cell size in radar pixels.</summary>
	public const int CellPixels = 8;

	/// <summary>The cells along each side of the grid.</summary>
	public const int Cells = MapOverview.Size / CellPixels;

	/// <summary>
	/// Counts positions into cells. Positions off the radar image are left out.
	/// </summary>
	/// <param name="points">The world positions.</param>
	/// <param name="overview">How the radar lines up with the world.</param>
	/// <returns>The cells holding any kills, in row then column order.</returns>
	public static IReadOnlyList<HeatCell> Build(IEnumerable<HeatPoint> points, MapOverview overview)
	{
		Ensure.NotNull(points);
		Ensure.NotNull(overview);

		Dictionary<(int X, int Y), int> counts = [];
		foreach (HeatPoint point in points)
		{
			(double x, double y) = overview.ToPixel(point);
			if (x is < 0 or >= MapOverview.Size || y is < 0 or >= MapOverview.Size)
			{
				continue;
			}

			(int, int) cell = ((int)(x / CellPixels), (int)(y / CellPixels));
			counts[cell] = counts.GetValueOrDefault(cell) + 1;
		}

		return [.. counts.Select(c => new HeatCell(c.Key.X, c.Key.Y, c.Value)).OrderBy(c => c.Y).ThenBy(c => c.X)];
	}
}

// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Views;

using ktsu.CounterStrikeProgression.Core.Heatmaps;

/// <summary>A map in the heatmap picker.</summary>
/// <param name="Map">The map.</param>
/// <param name="Points">The kills kept for it.</param>
/// <param name="Calibrated">Whether its kills line up with the game's radar.</param>
internal sealed record HeatmapMapView(string Map, long Points, bool Calibrated);

/// <summary>The player a heatmap belongs to.</summary>
/// <param name="SteamId">The 64-bit Steam ID.</param>
/// <param name="Name">Their last known name, or empty when the database doesn't have them.</param>
internal sealed record HeatmapPlayerView(ulong SteamId, string Name);

/// <summary>Which tab of the heatmap page a view is for.</summary>
internal enum HeatmapTab
{
	/// <summary>Where the killers stood.</summary>
	Kills,

	/// <summary>Where the victims fell.</summary>
	Deaths,

	/// <summary>Lines from each killer to their victim.</summary>
	Lines,
}

/// <summary>
/// One heatmap: the kill counts in each cell of a <see cref="HeatmapGrid.Cells"/>-square grid over the radar
/// image, or on the Lines tab the lines from killer cell to victim cell, and the cells anyone has fought in at
/// all, which outline the map when there's no radar image.
/// </summary>
/// <param name="Maps">Every map with kills.</param>
/// <param name="Map">The map shown, or <see langword="null"/> before any kill is recorded.</param>
/// <param name="Kind">The tab.</param>
/// <param name="Player">The player, or <see langword="null"/> for the whole server.</param>
/// <param name="Calibrated">Whether the cells line up with the game's radar image for the map.</param>
/// <param name="Radar">The radar image's URL, or <see langword="null"/> when the site has none for the map.</param>
/// <param name="Size">The radar image's width and height in pixels.</param>
/// <param name="CellPixels">One cell's width and height in radar pixels.</param>
/// <param name="Cells">The cells with kills, each <c>[column, row, count]</c>. Empty on the Lines tab.</param>
/// <param name="Outline">The cells with any kill or death on the server, each <c>[column, row]</c>.</param>
/// <param name="Max">The highest count in one cell, or on one line on the Lines tab.</param>
/// <param name="Points">The kills counted.</param>
/// <param name="Lines">
/// On the Lines tab of a calibrated map, the lines, each <c>[killer column, killer row, victim column, victim row,
/// death, count]</c>, with death 1 for a kill of the player and 0 otherwise. Empty on any other tab or map.
/// </param>
internal sealed record HeatmapView(
	IReadOnlyList<HeatmapMapView> Maps,
	string? Map,
	HeatmapTab Kind,
	HeatmapPlayerView? Player,
	bool Calibrated,
	string? Radar,
	int Size,
	int CellPixels,
	IReadOnlyList<int[]> Cells,
	IReadOnlyList<int[]> Outline,
	int Max,
	int Points,
	IReadOnlyList<int[]> Lines);

/// <summary>
/// Builds the heatmap page's view.
/// </summary>
internal static class HeatmapViewBuilder
{
	/// <summary>
	/// Builds a heatmap. A map without a known overview gets a square fitted to the server's kills, so a
	/// player's map and the server's line up with each other.
	/// </summary>
	/// <param name="maps">Every map with kills.</param>
	/// <param name="map">The map shown, or <see langword="null"/> when there are none.</param>
	/// <param name="kind">The tab.</param>
	/// <param name="player">The player, or <see langword="null"/> for the whole server.</param>
	/// <param name="points">The positions to count, on the Kills and Deaths tabs.</param>
	/// <param name="outline">The server's kill and death positions on the map.</param>
	/// <param name="radar">The radar image's URL, or <see langword="null"/>.</param>
	/// <param name="fights">The kills to draw as lines on the Lines tab, or <see langword="null"/> on any other.</param>
	/// <returns>The view. Lines are drawn on a calibrated map only, because a fitted square has no radar under it.</returns>
	public static HeatmapView Build(
		IReadOnlyList<MapPointCount> maps,
		string? map,
		HeatmapTab kind,
		HeatmapPlayerView? player,
		IReadOnlyCollection<HeatPoint> points,
		IReadOnlyCollection<HeatPoint> outline,
		string? radar,
		IReadOnlyCollection<KillPoint>? fights = null)
	{
		Ensure.NotNull(maps);
		Ensure.NotNull(points);
		Ensure.NotNull(outline);

		List<HeatmapMapView> pickable = [.. maps.Select(m => new HeatmapMapView(m.Map, m.Points, MapOverview.For(m.Map) is not null))];
		MapOverview overview = (map is null ? null : MapOverview.For(map)) ?? MapOverview.Fit(outline.Count > 0 ? outline : points);
		IReadOnlyList<HeatCell> cells = HeatmapGrid.Build(points, overview);
		IReadOnlyList<HeatCell> outlineCells = HeatmapGrid.Build(outline, overview);
		IReadOnlyList<HeatLine> lines = (kind == HeatmapTab.Lines) && overview.IsCalibrated && (fights is not null) ? LineGrid.Build(fights, player?.SteamId, overview) : [];
		bool drawsLines = kind == HeatmapTab.Lines;

		return new HeatmapView(
			pickable,
			map,
			kind,
			player,
			overview.IsCalibrated,
			overview.IsCalibrated ? radar : null,
			MapOverview.Size,
			HeatmapGrid.CellPixels,
			[.. cells.Select(c => new[] { c.X, c.Y, c.Count })],
			[.. outlineCells.Select(c => new[] { c.X, c.Y })],
			drawsLines ? (lines.Count == 0 ? 0 : lines.Max(l => l.Count)) : (cells.Count == 0 ? 0 : cells.Max(c => c.Count)),
			drawsLines ? lines.Sum(l => l.Count) : cells.Sum(c => c.Count),
			[.. lines.Select(l => new[] { l.X1, l.Y1, l.X2, l.Y2, l.Death ? 1 : 0, l.Count })]);
	}
}

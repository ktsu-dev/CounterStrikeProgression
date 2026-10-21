// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Heatmaps;

/// <summary>
/// How a map's radar image lines up with the world: the world position of its top left corner and the world
/// units per pixel, as in the map's <c>resource/overviews/&lt;map&gt;.txt</c>. Radar images are
/// <see cref="Size"/> pixels square.
/// </summary>
/// <param name="PosX">The world X at the image's left edge.</param>
/// <param name="PosY">The world Y at the image's top edge.</param>
/// <param name="Scale">World units per pixel.</param>
/// <param name="IsCalibrated">Whether this came from the game's overview, rather than fitted to the kills.</param>
internal sealed record MapOverview(double PosX, double PosY, double Scale, bool IsCalibrated)
{
	/// <summary>The width and height of a radar image, in pixels.</summary>
	public const int Size = 1024;

	// From each map's overview file in the game. A map that isn't here gets a square fitted to its kills.
	private static readonly Dictionary<string, MapOverview> Known = new(StringComparer.OrdinalIgnoreCase)
	{
		["de_ancient"] = new(-2953, 2164, 5, true),
		["de_anubis"] = new(-2796, 3328, 5.22, true),
		["de_dust2"] = new(-2476, 3239, 4.4, true),
		["de_inferno"] = new(-2087, 3870, 4.9, true),
		["de_mirage"] = new(-3230, 1713, 5, true),
		["de_nuke"] = new(-3453, 2887, 7, true),
		["de_overpass"] = new(-4831, 1781, 5.2, true),
		["de_train"] = new(-2308, 2078, 4.082077, true),
		["de_vertigo"] = new(-3168, 1762, 4, true),
		["cs_italy"] = new(-2647, 2592, 4.6, true),
		["cs_office"] = new(-1838, 1858, 4.1, true),
	};

	/// <summary>
	/// Gets the game's overview for a map.
	/// </summary>
	/// <param name="map">The map.</param>
	/// <returns>The overview, or <see langword="null"/> for a map without one here.</returns>
	public static MapOverview? For(string map) => Known.GetValueOrDefault(map ?? string.Empty);

	/// <summary>
	/// Fits a square around a set of positions, with a margin, for a map without a known overview.
	/// </summary>
	/// <param name="points">The positions.</param>
	/// <returns>An overview that shows every position.</returns>
	public static MapOverview Fit(IReadOnlyCollection<HeatPoint> points)
	{
		Ensure.NotNull(points);
		if (points.Count == 0)
		{
			return new(-Size * 2, Size * 2, 4, false);
		}

		int minX = points.Min(p => p.X);
		int maxX = points.Max(p => p.X);
		int minY = points.Min(p => p.Y);
		int maxY = points.Max(p => p.Y);
		double side = Math.Max(Math.Max(maxX - minX, maxY - minY) * 1.1, 512);
		double centerX = (minX + maxX) / 2.0;
		double centerY = (minY + maxY) / 2.0;
		return new(centerX - (side / 2), centerY + (side / 2), side / Size, false);
	}

	/// <summary>
	/// Turns a world position into a pixel on the radar image. World Y grows north and pixel Y grows down.
	/// </summary>
	/// <param name="point">The world position.</param>
	/// <returns>The pixel, which can be off the image.</returns>
	public (double X, double Y) ToPixel(HeatPoint point) => ToPixel(point.X, point.Y);

	/// <summary>
	/// Turns a world position into a pixel on the radar image. World Y grows north and pixel Y grows down.
	/// </summary>
	/// <param name="x">The world X.</param>
	/// <param name="y">The world Y.</param>
	/// <returns>The pixel, which can be off the image.</returns>
	public (double X, double Y) ToPixel(double x, double y) => ((x - PosX) / Scale, (PosY - y) / Scale);
}

// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site;

using System.Data.Common;
using System.Text.RegularExpressions;
using ktsu.CounterStrikeProgression.Core.Heatmaps;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Views;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

/// <summary>
/// The route handlers for the kill heatmaps and the radar images under them.
/// </summary>
internal static partial class HeatmapEndpoints
{
	/// <summary>
	/// Adds the heatmap page, its API, and the radar images.
	/// </summary>
	/// <param name="app">The app.</param>
	public static void Map(WebApplication app)
	{
		Ensure.NotNull(app);
		app.MapGet("/heatmaps", (IWebHostEnvironment environment) => Endpoints.Page(environment, "heatmaps.html"));
		app.MapGet("/api/heatmaps", HeatmapsAsync);
		app.MapGet("/radar/{map}.png", Radar);
	}

	/// <summary>
	/// Gets a map's heatmap, for the server or one player.
	/// </summary>
	/// <param name="map">The map, or empty for the map with the most kills.</param>
	/// <param name="kind"><c>deaths</c> for where victims fell; anything else shows where killers stood.</param>
	/// <param name="player">A SteamID64, or empty for the whole server.</param>
	/// <param name="heatmaps">Reads the kills.</param>
	/// <param name="reader">Reads the player's name.</param>
	/// <param name="radars">Finds the radar image under the heat.</param>
	/// <param name="cancellationToken">Stops waiting when the request is aborted.</param>
	/// <returns>The heatmap, 404 for a bad map name or player id, or 503 when the database can't be read.</returns>
	public static async Task<IResult> HeatmapsAsync(string? map, string? kind, string? player, HeatmapReader heatmaps, ProgressReader reader, RadarImages radars, CancellationToken cancellationToken)
	{
		Ensure.NotNull(heatmaps);
		Ensure.NotNull(reader);
		Ensure.NotNull(radars);

		if (!string.IsNullOrEmpty(map) && !IsMapName(map))
		{
			return TypedResults.NotFound();
		}

		ulong? steamId = null;
		if (!string.IsNullOrEmpty(player))
		{
			if (!ProgressReader.TryParseSteamId(player, out ulong parsed))
			{
				return TypedResults.NotFound();
			}

			steamId = parsed;
		}

		HeatmapKind heat = string.Equals(kind, "deaths", StringComparison.OrdinalIgnoreCase) ? HeatmapKind.Deaths : HeatmapKind.Kills;
		try
		{
			IReadOnlyList<MapPointCount> maps = await heatmaps.MapsAsync(cancellationToken).ConfigureAwait(false);
			string? shown = string.IsNullOrEmpty(map) ? (maps.Count > 0 ? maps[0].Map : null) : map;
			IReadOnlyList<HeatPoint> points = [];
			List<HeatPoint> outline = [];
			HeatmapPlayerView? owner = null;
			if (shown is not null)
			{
				points = await heatmaps.PointsAsync(shown, heat, steamId, cancellationToken).ConfigureAwait(false);
				outline.AddRange(await heatmaps.PointsAsync(shown, HeatmapKind.Kills, null, cancellationToken).ConfigureAwait(false));
				outline.AddRange(await heatmaps.PointsAsync(shown, HeatmapKind.Deaths, null, cancellationToken).ConfigureAwait(false));
			}

			if (steamId is ulong id)
			{
				PlayerProgress? stored = await reader.PlayerAsync(id, cancellationToken).ConfigureAwait(false);
				owner = new HeatmapPlayerView(id, stored?.Name ?? string.Empty);
			}

			// Only a calibrated map's radar lines up with its kills, so no other is looked for.
			string? radar = (shown is not null) && (MapOverview.For(shown)?.IsCalibrated == true) && (radars.Find(shown) is not null) ? $"/radar/{shown}.png" : null;
			return TypedResults.Ok(HeatmapViewBuilder.Build(maps, shown, heat, owner, points, outline, radar));
		}
		catch (DbException)
		{
			return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
		}
	}

	/// <summary>
	/// Serves a map's radar image, put under the site's data path by hand or read out of the game files.
	/// </summary>
	/// <param name="map">The map.</param>
	/// <param name="radars">Finds the image.</param>
	/// <returns>The image, or 404 when the site has none for the map.</returns>
	public static IResult Radar(string map, RadarImages radars)
	{
		Ensure.NotNull(radars);
		return IsMapName(map) && (radars.Find(map) is string path) ? TypedResults.PhysicalFile(path, "image/png") : TypedResults.NotFound();
	}

	/// <summary>
	/// Gets a value indicating whether text can be a map name. Only these reach a file path or a query.
	/// </summary>
	/// <param name="map">The text.</param>
	/// <returns>Whether it's lowercase letters, digits, and underscores, up to 64 long.</returns>
	public static bool IsMapName(string? map) => (map is not null) && MapName().IsMatch(map);

	[GeneratedRegex("^[a-z0-9_]{1,64}$", RegexOptions.CultureInvariant)]
	private static partial Regex MapName();
}

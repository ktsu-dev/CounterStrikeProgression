// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site;

using System.Data.Common;
using ktsu.CounterStrikeProgression.Core.Maps;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Live;
using ktsu.CounterStrikeProgression.Site.Views;
using Microsoft.AspNetCore.Http;

/// <summary>
/// The route handlers for the map board and a player's maps.
/// </summary>
internal static class MapEndpoints
{
	/// <summary>
	/// Gets one map's board, with every map anyone has played for the picker.
	/// </summary>
	/// <param name="map">The map to show, or empty for the most played.</param>
	/// <param name="reader">Reads the stored totals.</param>
	/// <param name="live">The live state, for who is online.</param>
	/// <param name="cancellationToken">Stops waiting when the request is aborted.</param>
	/// <returns>The board, or 503 when the database can't be read.</returns>
	public static async Task<IResult> MapsAsync(string? map, ProgressReader reader, LiveState live, CancellationToken cancellationToken)
	{
		Ensure.NotNull(reader);
		Ensure.NotNull(live);

		try
		{
			IReadOnlyList<MapSummary> maps = await reader.MapSummariesAsync(cancellationToken).ConfigureAwait(false);

			// Only a map someone has played is ever queried, so the URL can't fill the cache with made-up names.
			string? shown = MapsViewBuilder.Pick(map, maps);
			IReadOnlyList<MapStanding> standings = shown is null ? [] : await reader.MapStandingsAsync(shown, cancellationToken).ConfigureAwait(false);
			return TypedResults.Ok(MapsViewBuilder.Build(shown, maps, standings, live.IsPlaying));
		}
		catch (DbException)
		{
			return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
		}
	}

	/// <summary>
	/// Gets a player's totals on each map, and their best map.
	/// </summary>
	/// <param name="id">The SteamID64 from the URL.</param>
	/// <param name="reader">Reads the stored progress.</param>
	/// <param name="cancellationToken">Stops waiting when the request is aborted.</param>
	/// <returns>The maps, 404 for a bad or unknown id, or 503 when the database can't be read.</returns>
	public static async Task<IResult> PlayerMapsAsync(string id, ProgressReader reader, CancellationToken cancellationToken)
	{
		Ensure.NotNull(reader);

		if (!ProgressReader.TryParseSteamId(id, out ulong steamId))
		{
			return TypedResults.NotFound();
		}

		PlayerProgress? stored;
		try
		{
			stored = await reader.PlayerAsync(steamId, cancellationToken).ConfigureAwait(false);
		}
		catch (DbException)
		{
			return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
		}

		return stored is null ? TypedResults.NotFound() : TypedResults.Ok(MapsViewBuilder.BuildPlayer(stored));
	}
}

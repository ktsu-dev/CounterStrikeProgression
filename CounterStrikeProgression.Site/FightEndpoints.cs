// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site;

using System.Data.Common;
using ktsu.CounterStrikeProgression.Core.Heatmaps;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Live;
using Microsoft.AspNetCore.Http;

/// <summary>A player's won and lost fights on one map, counted into cells over the radar image.</summary>
/// <param name="Map">The map shown, or <see langword="null"/> when there is none to show.</param>
/// <param name="Scope"><c>match</c> for the match being played, or <c>all</c> for the stored kills.</param>
/// <param name="Calibrated">Whether the map's radar lines up with the world, so the cells mean something.</param>
/// <param name="Radar">The radar image's URL, or <see langword="null"/> when the site has none.</param>
/// <param name="Size">The radar image's width and height in pixels.</param>
/// <param name="CellPixels">The cell size in radar pixels.</param>
/// <param name="Cells">The cells holding any fights.</param>
internal sealed record FightView(string? Map, string Scope, bool Calibrated, string? Radar, int Size, int CellPixels, IReadOnlyList<FightCell> Cells);

/// <summary>
/// The route handler for where a player wins and loses fights.
/// </summary>
internal static class FightEndpoints
{
	/// <summary>
	/// Gets a player's fights on a map, for the match being played or from the stored kills.
	/// </summary>
	/// <param name="id">The SteamID64 from the URL.</param>
	/// <param name="scope"><c>match</c> for the current match. Anything else reads the stored kills.</param>
	/// <param name="map">The map for the stored kills, or empty for the current map when the player is online and otherwise the last map they fought on.</param>
	/// <param name="store">Reads the stored kills.</param>
	/// <param name="match">Holds the current match's kills.</param>
	/// <param name="live">The live state, which says whether the player is online.</param>
	/// <param name="radars">Finds the radar image.</param>
	/// <param name="cancellationToken">Stops waiting when the request is aborted.</param>
	/// <returns>The fights, 404 for a bad id or map name, or 503 when the database can't be read.</returns>
	public static async Task<IResult> FightsAsync(string id, string? scope, string? map, IKillMapStore store, MatchFights match, LiveState live, RadarImages radars, CancellationToken cancellationToken)
	{
		Ensure.NotNull(store);
		Ensure.NotNull(match);
		Ensure.NotNull(live);
		Ensure.NotNull(radars);

		if (!ProgressReader.TryParseSteamId(id, out ulong steamId) || (!string.IsNullOrEmpty(map) && !HeatmapEndpoints.IsMapName(map)))
		{
			return TypedResults.NotFound();
		}

		bool matchScope = string.Equals(scope, "match", StringComparison.OrdinalIgnoreCase);
		try
		{
			string? shown;
			IReadOnlyList<KillPoint> kills = [];
			if (matchScope)
			{
				shown = match.Map;
				kills = await match.CurrentAsync(cancellationToken).ConfigureAwait(false);
			}
			else
			{
				ServerSnapshot? snapshot = live.Snapshot;
				bool online = snapshot?.Players.Any(p => p.SteamId == steamId) == true;
				shown = !string.IsNullOrEmpty(map) ? map
					: online ? snapshot!.Map
					: await store.LastMapAsync(steamId, cancellationToken).ConfigureAwait(false);
				if (shown is not null)
				{
					kills = await store.FightsAsync(shown, steamId, null, HeatmapReader.PlayerLimit, cancellationToken).ConfigureAwait(false);
				}
			}

			MapOverview? overview = shown is null ? null : MapOverview.For(shown);
			bool calibrated = overview?.IsCalibrated == true;
			IReadOnlyList<FightCell> cells = calibrated ? FightGrid.Build(kills, steamId, overview!) : [];
			string? radar = calibrated && (radars.Find(shown!) is not null) ? $"/radar/{shown}.png" : null;
			return TypedResults.Ok(new FightView(shown, matchScope ? "match" : "all", calibrated, radar, MapOverview.Size, FightGrid.CellPixels, cells));
		}
		catch (DbException)
		{
			return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
		}
	}
}

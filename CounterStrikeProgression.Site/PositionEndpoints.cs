// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site;

using ktsu.CounterStrikeProgression.Core.Heatmaps;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Live;
using Microsoft.AspNetCore.Http;

/// <summary>
/// The route handler for where the players are right now.
/// </summary>
internal static class PositionEndpoints
{
	/// <summary>
	/// Gets everyone's position on the current map's radar, for a page's first load before the
	/// <c>positions</c> events arrive.
	/// </summary>
	/// <param name="live">The live state, which holds the latest snapshot.</param>
	/// <param name="radars">Finds the radar image.</param>
	/// <returns>The positions.</returns>
	public static IResult Positions(LiveState live, RadarImages radars)
	{
		Ensure.NotNull(live);
		Ensure.NotNull(radars);

		ServerSnapshot? snapshot = live.Snapshot;
		string? map = snapshot?.Map;

		// Only a calibrated map's radar lines up with the positions, so no other is looked for.
		string? radar = HeatmapEndpoints.IsMapName(map) && (MapOverview.For(map!)?.IsCalibrated == true) && (radars.Find(map!) is not null) ? $"/radar/{map}.png" : null;
		return TypedResults.Ok(LivePositions.From(snapshot, radar));
	}
}

// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site;

using System.Data.Common;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Rotation;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Live;
using ktsu.CounterStrikeProgression.Site.Views;
using Microsoft.AspNetCore.Http;

/// <summary>
/// The route handler for comparing two players.
/// </summary>
internal static class CompareEndpoints
{
	/// <summary>
	/// Gets two players side by side: rank, season, totals, weapons, and skill shots.
	/// </summary>
	/// <param name="a">The first player's SteamID64.</param>
	/// <param name="b">The second player's SteamID64.</param>
	/// <param name="reader">Reads the stored progress.</param>
	/// <param name="live">The live state.</param>
	/// <param name="time">The clock that decides the current season.</param>
	/// <param name="cancellationToken">Stops waiting when the request is aborted.</param>
	/// <returns>The comparison, 400 when both ids are the same player, 404 for a bad or unknown id, or 503 when the database can't be read.</returns>
	public static async Task<IResult> CompareAsync(string? a, string? b, ProgressReader reader, LiveState live, TimeProvider time, CancellationToken cancellationToken)
	{
		Ensure.NotNull(reader);
		Ensure.NotNull(live);
		Ensure.NotNull(time);

		if (!ProgressReader.TryParseSteamId(a, out ulong aId) || !ProgressReader.TryParseSteamId(b, out ulong bId))
		{
			return TypedResults.NotFound();
		}

		if (aId == bId)
		{
			return TypedResults.BadRequest();
		}

		ProgressionSettings settings = live.Settings;
		Season current = Season.For(DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime), settings.SeasonMonths);
		PlayerProgress? aStored;
		PlayerProgress? bStored;
		IReadOnlyList<SeasonStanding> standings;
		try
		{
			aStored = await reader.PlayerAsync(aId, cancellationToken).ConfigureAwait(false);
			bStored = await reader.PlayerAsync(bId, cancellationToken).ConfigureAwait(false);
			standings = await reader.SeasonStandingsAsync(current.Start, cancellationToken).ConfigureAwait(false);
		}
		catch (DbException)
		{
			return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
		}

		if ((Merged(aId, aStored, live) is not { } aMerged) || (Merged(bId, bStored, live) is not { } bMerged))
		{
			return TypedResults.NotFound();
		}

		SeasonView board = SeasonsViewBuilder.Build(current, current, settings, standings, live.Overlay, live.IsPlaying, live.OverlayIds);
		return TypedResults.Ok(CompareViewBuilder.Build(
			aMerged,
			live.IsPlaying(aId),
			SeasonsViewBuilder.BuildPlayer(aId, SeasonXp(aMerged, current), board, [], settings),
			bMerged,
			live.IsPlaying(bId),
			SeasonsViewBuilder.BuildPlayer(bId, SeasonXp(bMerged, current), board, [], settings),
			settings));
	}

	private static PlayerProgress? Merged(ulong steamId, PlayerProgress? stored, LiveState live)
	{
		ProgressSnapshot? overlay = live.Overlay(steamId);
		return (stored is null) && (ProgressMerge.SteamIdOf(overlay) is null) ? null : ProgressMerge.Merge(stored, overlay);
	}

	private static long SeasonXp(PlayerProgress merged, Season current) =>
		merged.SeasonStart == current.Start ? merged.SeasonXp : 0;
}

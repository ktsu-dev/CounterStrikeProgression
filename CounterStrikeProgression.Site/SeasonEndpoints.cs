// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site;

using System.Data.Common;
using System.Globalization;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Rotation;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Live;
using ktsu.CounterStrikeProgression.Site.Views;
using Microsoft.AspNetCore.Http;

/// <summary>
/// The route handlers for the season board and a player's season badges.
/// </summary>
internal static class SeasonEndpoints
{
	/// <summary>
	/// Gets one season's standings, with the live rows laid over the current season.
	/// </summary>
	/// <param name="start">The season's first day as <c>yyyy-MM-dd</c>, or empty for the current season.</param>
	/// <param name="reader">Reads the stored standings.</param>
	/// <param name="live">The live state.</param>
	/// <param name="time">The clock that decides the current season.</param>
	/// <param name="cancellationToken">Stops waiting when the request is aborted.</param>
	/// <returns>The season, 404 for a day that doesn't start a season or a season not yet begun, or 503 when the database can't be read.</returns>
	public static async Task<IResult> SeasonsAsync(string? start, ProgressReader reader, LiveState live, TimeProvider time, CancellationToken cancellationToken)
	{
		Ensure.NotNull(reader);
		Ensure.NotNull(live);
		Ensure.NotNull(time);

		ProgressionSettings settings = live.Settings;
		Season current = CurrentSeason(time, settings);
		Season season = current;
		if (!string.IsNullOrEmpty(start))
		{
			if (!DateOnly.TryParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day))
			{
				return TypedResults.NotFound();
			}

			season = Season.For(day, settings.SeasonMonths);
			if ((season.Start != day) || (season.Start > current.Start))
			{
				return TypedResults.NotFound();
			}
		}

		IReadOnlyList<SeasonStanding> stored;
		try
		{
			stored = await reader.SeasonStandingsAsync(season.Start, cancellationToken).ConfigureAwait(false);
		}
		catch (DbException)
		{
			return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
		}

		return TypedResults.Ok(SeasonsViewBuilder.Build(season, current, settings, stored, live.Overlay, live.IsPlaying, live.OverlayIds));
	}

	/// <summary>
	/// Gets a player's season badges: this season's place, and every top 3 finish before it.
	/// </summary>
	/// <param name="id">The SteamID64 from the URL.</param>
	/// <param name="reader">Reads the stored progress.</param>
	/// <param name="live">The live state.</param>
	/// <param name="time">The clock that decides the current season.</param>
	/// <param name="cancellationToken">Stops waiting when the request is aborted.</param>
	/// <returns>The badges, 404 for a bad id, or 503 when the database can't be read.</returns>
	public static async Task<IResult> PlayerSeasonsAsync(string id, ProgressReader reader, LiveState live, TimeProvider time, CancellationToken cancellationToken)
	{
		Ensure.NotNull(reader);
		Ensure.NotNull(live);
		Ensure.NotNull(time);

		if (!ProgressReader.TryParseSteamId(id, out ulong steamId))
		{
			return TypedResults.NotFound();
		}

		ProgressionSettings settings = live.Settings;
		Season current = CurrentSeason(time, settings);
		IReadOnlyList<SeasonStanding> standings;
		IReadOnlyList<SeasonResult> results;
		PlayerProgress? stored;
		try
		{
			standings = await reader.SeasonStandingsAsync(current.Start, cancellationToken).ConfigureAwait(false);
			results = await reader.SeasonResultsAsync(steamId, cancellationToken).ConfigureAwait(false);
			stored = await reader.PlayerAsync(steamId, cancellationToken).ConfigureAwait(false);
		}
		catch (DbException)
		{
			return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
		}

		ProgressSnapshot? overlay = live.Overlay(steamId);
		long seasonXp = 0;
		if ((stored is not null) || (ProgressMerge.SteamIdOf(overlay) is not null))
		{
			PlayerProgress merged = ProgressMerge.Merge(stored, overlay);
			seasonXp = merged.SeasonStart == current.Start ? merged.SeasonXp : 0;
		}

		SeasonView board = SeasonsViewBuilder.Build(current, current, settings, standings, live.Overlay, live.IsPlaying, live.OverlayIds);
		return TypedResults.Ok(SeasonsViewBuilder.BuildPlayer(steamId, seasonXp, board, results, settings));
	}

	private static Season CurrentSeason(TimeProvider time, ProgressionSettings settings) =>
		Season.For(DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime), settings.SeasonMonths);
}

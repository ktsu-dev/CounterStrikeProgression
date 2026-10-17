// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site;

using System.Data.Common;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Live;
using ktsu.CounterStrikeProgression.Site.Views;
using Microsoft.AspNetCore.Http;

/// <summary>
/// The XP history route handlers: a player's XP graph and the weekly board.
/// </summary>
internal static class HistoryEndpoints
{
	/// <summary>
	/// Gets one player's XP graph: their rank XP at the end of each day, and what they earned today and this week.
	/// </summary>
	/// <param name="id">The SteamID64 from the URL.</param>
	/// <param name="days">How many days to show, ending today. Omitted or out of range, 30.</param>
	/// <param name="reader">Reads the stored progress.</param>
	/// <param name="live">The live state.</param>
	/// <param name="time">The clock that decides today.</param>
	/// <param name="cancellationToken">Stops waiting when the request is aborted.</param>
	/// <returns>The graph, 404 for a bad or unknown id, or 503 when the database can't be read.</returns>
	public static async Task<IResult> PlayerHistoryAsync(string id, int? days, ProgressReader reader, LiveState live, TimeProvider time, CancellationToken cancellationToken)
	{
		Ensure.NotNull(reader);
		Ensure.NotNull(live);
		Ensure.NotNull(time);

		if (!ProgressReader.TryParseSteamId(id, out ulong steamId))
		{
			return TypedResults.NotFound();
		}

		int span = days is >= 1 and <= HistoryViewBuilder.MaxDays ? days.Value : HistoryViewBuilder.DefaultDays;
		DateOnly today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
		PlayerProgress? stored;
		IReadOnlyList<DailyXp> rows;
		try
		{
			stored = await reader.PlayerAsync(steamId, cancellationToken).ConfigureAwait(false);
			rows = await reader.HistoryAsync(steamId, HistoryViewBuilder.ProgressFrom(today, span), cancellationToken).ConfigureAwait(false);
		}
		catch (DbException)
		{
			return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
		}

		ProgressSnapshot? overlay = live.Overlay(steamId);
		if (stored is null && ProgressMerge.SteamIdOf(overlay) is null)
		{
			return TypedResults.NotFound();
		}

		return TypedResults.Ok(HistoryViewBuilder.Build(rows, ProgressMerge.Merge(stored, overlay), today, span));
	}

	/// <summary>
	/// Gets the "XP this week" board: the players who earned the most rank XP since Monday, in UTC.
	/// </summary>
	/// <param name="reader">Reads the stored gains.</param>
	/// <param name="live">The live state.</param>
	/// <param name="time">The clock that decides the week.</param>
	/// <param name="cancellationToken">Stops waiting when the request is aborted.</param>
	/// <returns>The board, or 503 when the database can't be read.</returns>
	public static async Task<IResult> WeeklyAsync(ProgressReader reader, LiveState live, TimeProvider time, CancellationToken cancellationToken)
	{
		Ensure.NotNull(reader);
		Ensure.NotNull(live);
		Ensure.NotNull(time);

		DateOnly start = XpHistory.WeekStart(DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime));
		IReadOnlyList<XpGainEntry> stored;
		try
		{
			// Read more than the board shows, since live progress can lift a player past the stored top ten.
			stored = await reader.GainsSinceAsync(start, HistoryViewBuilder.WeeklyCount * 5, cancellationToken).ConfigureAwait(false);
		}
		catch (DbException)
		{
			return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
		}

		return TypedResults.Ok(HistoryViewBuilder.BuildWeekly(start, stored, live.Overlay, live.IsPlaying, live.Settings, live.OverlayIds));
	}
}

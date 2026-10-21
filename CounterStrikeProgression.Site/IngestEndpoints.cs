// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site;

using System.Text.Json;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Site.Live;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

/// <summary>
/// The route handler that takes batches from the plugin on <see cref="Endpoints.IngestRoute"/>.
/// </summary>
internal static class IngestEndpoints
{
	/// <summary>The largest batch the ingest endpoint reads, in bytes.</summary>
	public const long IngestBodyLimit = 4 * 1024 * 1024;

	/// <summary>
	/// Takes a batch from the plugin, applies it, and tells the browsers what changed.
	/// </summary>
	/// <param name="context">The request.</param>
	/// <param name="options">The site settings, for the token.</param>
	/// <param name="live">The live state.</param>
	/// <param name="broadcaster">Where the change events go.</param>
	/// <param name="feats">The feats each player has earned, kept current here.</param>
	/// <param name="matchFights">The current match's kills, kept current here.</param>
	/// <returns>204 on success, 401 without the token, 400 for a body that isn't a batch, or 413 for one over the limit.</returns>
	public static async Task<IResult> IngestAsync(HttpContext context, SiteOptions options, LiveState live, Broadcaster broadcaster, FeatIndex feats, MatchFights matchFights)
	{
		Ensure.NotNull(context);
		Ensure.NotNull(options);
		Ensure.NotNull(live);
		Ensure.NotNull(broadcaster);
		Ensure.NotNull(matchFights);

		if (!IngestAuth.IsAuthorized(context.Request.Headers.Authorization, options.Token))
		{
			return TypedResults.Unauthorized();
		}

		if (context.Request.ContentLength > IngestBodyLimit)
		{
			return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
		}

		if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeLimit)
		{
			sizeLimit.MaxRequestBodySize = IngestBodyLimit;
		}

		LiveBatch? batch;
		try
		{
			batch = await JsonSerializer.DeserializeAsync<LiveBatch>(context.Request.Body, LiveJson.Options, context.RequestAborted).ConfigureAwait(false);
		}
		catch (JsonException)
		{
			return TypedResults.BadRequest();
		}
		catch (BadHttpRequestException ex)
		{
			return TypedResults.StatusCode(ex.StatusCode);
		}

		if (!IsComplete(batch))
		{
			return TypedResults.BadRequest();
		}

		LiveChanges changes = live.Apply(batch);
		feats.Add(changes.NewFeed);
		LivePublisher.Publish(broadcaster, batch, changes, matchFights.Apply(batch.Snapshot, batch.Kills));
		return TypedResults.NoContent();
	}

	private static bool IsComplete([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] LiveBatch? batch) =>
		batch is { Snapshot.Players: { } players, Feed: { } feed, Progress: { } progress, Settings: not null }
		&& players.All(p => p is not null)
		&& feed.All(e => e is not null)
		&& progress.All(p => p is { Weapons: { } weapons, Challenges: { } challenges, Contracts: { } contracts }
			&& weapons.All(w => w is not null)
			&& challenges.All(c => c is not null)
			&& contracts.All(c => c is { Period: not null, Contract: not null }))
		&& (batch.Recaps?.All(r => r is { PlayerName: not null, Map: not null }) ?? true)
		&& (batch.Kills?.All(k => k is { Map: not null, Weapon: not null }) ?? true);
}

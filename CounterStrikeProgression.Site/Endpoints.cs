// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site;

using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using ktsu.CounterStrikeProgression.Core.Feats;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Live;
using ktsu.CounterStrikeProgression.Site.Views;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

/// <summary>What the live page loads first.</summary>
/// <param name="Online">Whether the server sent a batch within the offline window.</param>
/// <param name="Snapshot">The latest server snapshot, or <see langword="null"/> before the first batch.</param>
/// <param name="Feed">The feed, newest first.</param>
/// <param name="Feats">The ids of the feats each player on the server has earned, keyed by Steam ID.</param>
internal sealed record LiveView(bool Online, ServerSnapshot? Snapshot, IReadOnlyList<FeedItem> Feed, IReadOnlyDictionary<string, IReadOnlyList<string>> Feats);

/// <summary>
/// The site's route handlers.
/// </summary>
internal static class Endpoints
{
	/// <summary>The port the public pages and APIs listen on.</summary>
	public const int PublicPort = 8080;

	/// <summary>The in-cluster port that takes batches from the plugin, and nothing else.</summary>
	public const int IngestPort = 8081;

	/// <summary>The largest batch the ingest endpoint reads, in bytes.</summary>
	public const long IngestBodyLimit = 4 * 1024 * 1024;

	/// <summary>The ingest route.</summary>
	public const string IngestRoute = "/api/ingest";

	/// <summary>
	/// Checks whether a path may be served on a port. The ingest route answers only on <see cref="IngestPort"/>,
	/// and that port answers nothing else. This reads the port the connection arrived on, not the
	/// <c>Host</c> header, which the client controls.
	/// </summary>
	/// <param name="localPort">The port the connection arrived on.</param>
	/// <param name="path">The request path.</param>
	/// <returns><see langword="true"/> when the request may go on.</returns>
	public static bool IsAllowedOnPort(int localPort, PathString path)
	{
		bool onIngestPort = localPort == IngestPort;
		bool isIngestRoute = path.StartsWithSegments(IngestRoute, StringComparison.OrdinalIgnoreCase);
		return onIngestPort == isIngestRoute;
	}

	/// <summary>Answers the health probe.</summary>
	/// <returns>200 with <c>ok</c>.</returns>
	public static IResult Health() => TypedResults.Text("ok");

	/// <summary>
	/// Serves one of the pages from the web root.
	/// </summary>
	/// <param name="environment">The host environment.</param>
	/// <param name="file">The file name in the web root.</param>
	/// <returns>The page, or 404 when the file is missing.</returns>
	public static IResult Page(IWebHostEnvironment environment, string file)
	{
		Ensure.NotNull(environment);
		IFileInfo info = environment.WebRootFileProvider.GetFileInfo(file);
		return info is { Exists: true, PhysicalPath: { } path }
			? TypedResults.PhysicalFile(path, "text/html; charset=utf-8")
			: TypedResults.NotFound();
	}

	/// <summary>
	/// Serves the player page for a well-formed SteamID64.
	/// </summary>
	/// <param name="id">The SteamID64 from the URL.</param>
	/// <param name="environment">The host environment.</param>
	/// <returns>The page, or 404 when the id isn't a SteamID64 or the file is missing.</returns>
	public static IResult PlayerPage(string id, IWebHostEnvironment environment) =>
		ProgressReader.TryParseSteamId(id, out _) ? Page(environment, "player.html") : TypedResults.NotFound();

	/// <summary>
	/// Gets the live state for the live page.
	/// </summary>
	/// <param name="live">The live state.</param>
	/// <param name="feats">The feats each player has earned.</param>
	/// <returns>The online flag, the snapshot, the feed, and the feats of the players on the server.</returns>
	public static IResult Live(LiveState live, FeatIndex feats)
	{
		Ensure.NotNull(live);
		Ensure.NotNull(feats);

		ServerSnapshot? snapshot = live.Snapshot;
		Dictionary<string, IReadOnlyList<string>> earned = [];
		foreach (LivePlayer player in snapshot?.Players ?? [])
		{
			if (!player.IsBot && (feats.For(player.SteamId) is { Count: > 0 } ids))
			{
				earned[player.SteamId.ToString(CultureInfo.InvariantCulture)] = ids;
			}
		}

		return TypedResults.Ok(new LiveView(live.IsOnline, snapshot, FeedCaptions.For(live.Feed, live.Settings), earned));
	}

	/// <summary>
	/// Gets the server ranks, with the live rows laid over the stored ones.
	/// </summary>
	/// <param name="reader">Reads the stored rankings.</param>
	/// <param name="live">The live state.</param>
	/// <param name="cancellationToken">Stops waiting when the request is aborted.</param>
	/// <param name="feats">The feats each player has earned, kept current here.</param>
	/// <returns>The rows, or 503 when the database can't be read.</returns>
	public static async Task<IResult> RanksAsync(ProgressReader reader, LiveState live, FeatIndex feats, CancellationToken cancellationToken)
	{
		Ensure.NotNull(reader);
		Ensure.NotNull(live);

		IReadOnlyList<RankingEntry> stored;
		try
		{
			stored = await reader.RankingsAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (DbException)
		{
			return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
		}

		return TypedResults.Ok(RanksViewBuilder.Build(stored, live.Overlay, live.IsPlaying, live.Settings, live.OverlayIds, feats.For));
	}

	/// <summary>
	/// Gets the rank distribution: every ladder step, lowest first, with how many players stand on it.
	/// </summary>
	/// <param name="reader">Reads the stored rankings.</param>
	/// <param name="live">The live state.</param>
	/// <param name="cancellationToken">Stops waiting when the request is aborted.</param>
	/// <returns>The steps, or 503 when the database can't be read.</returns>
	public static async Task<IResult> LadderAsync(ProgressReader reader, LiveState live, CancellationToken cancellationToken)
	{
		Ensure.NotNull(reader);
		Ensure.NotNull(live);

		IReadOnlyList<RankingEntry> stored;
		try
		{
			stored = await reader.RankingsAsync(ProgressReader.AllPlayers, cancellationToken).ConfigureAwait(false);
		}
		catch (DbException)
		{
			return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
		}

		IReadOnlyList<RankRow> rows = RanksViewBuilder.Build(stored, live.Overlay, live.IsPlaying, live.Settings, live.OverlayIds);
		return TypedResults.Ok(LadderViewBuilder.Build(rows, live.Settings));
	}

	/// <summary>
	/// Gets one player's page.
	/// </summary>
	/// <param name="id">The SteamID64 from the URL.</param>
	/// <param name="reader">Reads the stored progress.</param>
	/// <param name="live">The live state.</param>
	/// <param name="cancellationToken">Stops waiting when the request is aborted.</param>
	/// <param name="feats">The feats each player has earned, kept current here.</param>
	/// <returns>The player, 404 for a bad or unknown id, or 503 when the database can't be read.</returns>
	public static async Task<IResult> PlayerAsync(string id, ProgressReader reader, LiveState live, FeatIndex feats, CancellationToken cancellationToken)
	{
		Ensure.NotNull(reader);
		Ensure.NotNull(live);

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

		ProgressSnapshot? overlay = live.Overlay(steamId);
		if (stored is null && ProgressMerge.SteamIdOf(overlay) is null)
		{
			return TypedResults.NotFound();
		}

		PlayerProgress merged = ProgressMerge.Merge(stored, overlay);
		PlayerView view = PlayerViewBuilder.Build(merged, live.Settings, live.IsPlaying(steamId), live.PlayerFeed(steamId), live.LastSession(steamId));
		feats.Set(steamId, view.Feats.Where(f => f.Earned).Select(f => FeatCatalog.All.First(d => d.Id == f.Id)));
		return TypedResults.Ok(view);
	}

	/// <summary>
	/// Gets the leaderboards: each weapon by level, each skill shot by count, the most feats, and the rarest skill shots.
	/// </summary>
	/// <param name="boards">Every ranked player's tally.</param>
	/// <param name="reader">Reads the stored progress of the players on the server.</param>
	/// <param name="live">The live state.</param>
	/// <param name="cancellationToken">Stops waiting when the request is aborted.</param>
	/// <returns>The boards.</returns>
	public static async Task<IResult> LeaderboardsAsync(BoardIndex boards, ProgressReader reader, LiveState live, CancellationToken cancellationToken)
	{
		Ensure.NotNull(boards);
		await boards.RefreshPlayingAsync(reader, live, cancellationToken).ConfigureAwait(false);
		return TypedResults.Ok(LeaderboardViewBuilder.Build(boards.All, boards.Updated));
	}

	/// <summary>
	/// Gets the skill shot guide: every skill shot, what it asks for, the weapons that can earn it, and how many players have.
	/// </summary>
	/// <param name="boards">Every ranked player's tally.</param>
	/// <param name="reader">Reads the stored progress of the players on the server.</param>
	/// <param name="live">The live state.</param>
	/// <param name="cancellationToken">Stops waiting when the request is aborted.</param>
	/// <returns>The guide.</returns>
	public static async Task<IResult> SkillShotsAsync(BoardIndex boards, ProgressReader reader, LiveState live, CancellationToken cancellationToken)
	{
		Ensure.NotNull(boards);
		Ensure.NotNull(live);
		await boards.RefreshPlayingAsync(reader, live, cancellationToken).ConfigureAwait(false);
		return TypedResults.Ok(SkillShotGuideBuilder.Build(boards.All, live.Settings, boards.Updated));
	}

	/// <summary>
	/// Opens the live event stream.
	/// </summary>
	/// <param name="live">The live state.</param>
	/// <param name="broadcaster">Where the events come from.</param>
	/// <param name="lifetime">Ends the stream when the site stops.</param>
	/// <returns>The stream.</returns>
	public static IResult Stream(LiveState live, Broadcaster broadcaster, IHostApplicationLifetime lifetime)
	{
		Ensure.NotNull(lifetime);
		return new EventStreamResult(live, broadcaster, lifetime.ApplicationStopping);
	}

	/// <summary>
	/// Takes a batch from the plugin, applies it, and tells the browsers what changed.
	/// </summary>
	/// <param name="context">The request.</param>
	/// <param name="options">The site settings, for the token.</param>
	/// <param name="live">The live state.</param>
	/// <param name="broadcaster">Where the change events go.</param>
	/// <param name="feats">The feats each player has earned, kept current here.</param>
	/// <returns>204 on success, 401 without the token, 400 for a body that isn't a batch, or 413 for one over the limit.</returns>
	public static async Task<IResult> IngestAsync(HttpContext context, SiteOptions options, LiveState live, Broadcaster broadcaster, FeatIndex feats)
	{
		Ensure.NotNull(context);
		Ensure.NotNull(options);
		Ensure.NotNull(live);
		Ensure.NotNull(broadcaster);

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
		Publish(broadcaster, batch, changes);
		return TypedResults.NoContent();
	}

	private static void Publish(Broadcaster broadcaster, LiveBatch batch, LiveChanges changes)
	{
		if (changes.SnapshotChanged)
		{
			broadcaster.Publish("snapshot", batch.Snapshot);
		}

		foreach (FeedEntry entry in changes.NewFeed.OrderBy(e => e.At))
		{
			broadcaster.Publish("feed", FeedCaptions.For(entry, batch.Settings));
		}

		if (changes.ChangedPlayers.Count > 0)
		{
			string[] steamIds = [.. changes.ChangedPlayers.Select(id => id.ToString(CultureInfo.InvariantCulture))];
			broadcaster.Publish("progress", steamIds);
		}
	}

	private static bool IsComplete([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] LiveBatch? batch) =>
		batch is { Snapshot.Players: { } players, Feed: { } feed, Progress: { } progress, Settings: not null }
		&& players.All(p => p is not null)
		&& feed.All(e => e is not null)
		&& progress.All(p => p is { Weapons: { } weapons, Challenges: { } challenges } && weapons.All(w => w is not null) && challenges.All(c => c is not null))
		&& (batch.Recaps?.All(r => r is { PlayerName: not null, Map: not null }) ?? true);
}

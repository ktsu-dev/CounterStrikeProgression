// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site;

using System.Diagnostics.CodeAnalysis;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using ktsu.CounterStrikeProgression.Core.Heatmaps;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Recap;
using ktsu.CounterStrikeProgression.Core.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// Gathers the server's state, the feed, and changed progress into live batches for the progression site.
/// </summary>
/// <remarks>
/// Every member runs on the game thread, and each batch holds only copies, so the sender can serialize it on
/// the thread pool while the game moves on.
/// </remarks>
internal sealed class SiteReporter : IDisposable
{
	private const int PendingFeedLimit = 200;

	private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);

	private readonly PluginConfig pluginConfig;
	private readonly ILogger log;
	private readonly SessionManager sessionManager;
	private readonly LiveSender sender;
	private readonly ProgressChangeTracker tracker = new();
	private readonly List<FeedEntry> pendingFeed = [];
	private readonly List<MapRecap> pendingRecaps = [];
	private readonly List<ulong> pendingCorrected = [];
	private readonly List<KillPoint> pendingKills = [];
	private DateTime? mapStartedAt;
	private ConVar? gameType;
	private ConVar? gameMode;
	private int batchesSinceReset;

	/// <summary>
	/// Creates a reporter and starts its sender.
	/// </summary>
	/// <param name="config">The plugin config, whose <see cref="PluginConfig.Site"/> URL must be an absolute http or https URL.</param>
	/// <param name="sessions">The connected players.</param>
	/// <param name="logger">Where collect and send failures are reported.</param>
	/// <exception cref="ArgumentException">The site URL isn't an absolute http or https URL.</exception>
	public SiteReporter(PluginConfig config, SessionManager sessions, ILogger logger)
	{
		Ensure.NotNull(config);
		Ensure.NotNull(sessions);
		Ensure.NotNull(logger);

		Uri ingestUrl = ParseUrl(config.Site.Url) ?? throw new ArgumentException("The site URL isn't an absolute http or https URL.", nameof(config));
		pluginConfig = config;
		log = logger;
		sessionManager = sessions;
		sender = new LiveSender(
			ingestUrl,
			config.Site.Token ?? string.Empty,
			Math.Max(1, config.Site.QueueLimit),
			RequestTimeout,
			exception => Log.SiteFailed(logger, exception));
	}

	/// <summary>
	/// Creates a reporter when the config names a site.
	/// </summary>
	/// <param name="config">The plugin config.</param>
	/// <param name="sessions">The connected players.</param>
	/// <param name="logger">Where an invalid URL and send failures are reported.</param>
	/// <returns>The reporter, or <see langword="null"/> when the URL is blank or invalid.</returns>
	public static SiteReporter? Create(PluginConfig config, SessionManager sessions, ILogger logger)
	{
		Ensure.NotNull(config);
		Ensure.NotNull(logger);

		string? url = config.Site?.Url;
		if (string.IsNullOrWhiteSpace(url))
		{
			return null;
		}

		if (ParseUrl(url) is null)
		{
			Log.SiteUrlInvalid(logger, url);
			return null;
		}

		return new SiteReporter(config, sessions, logger);
	}

	/// <summary>
	/// Adds the feed entries for what a player earned to the next batch.
	/// </summary>
	/// <param name="session">The player's session.</param>
	/// <param name="name">The player's name.</param>
	/// <param name="result">What they earned.</param>
	public void Record(PlayerSession session, string name, ProgressionResult result)
	{
		Ensure.NotNull(session);
		Ensure.NotNull(name);
		Ensure.NotNull(result);

		pendingFeed.AddRange(FeedBuilder.From(result, session.SteamId, name, DateTime.UtcNow));
		if (pendingFeed.Count > PendingFeedLimit)
		{
			pendingFeed.RemoveRange(0, pendingFeed.Count - PendingFeedLimit);
		}
	}

	/// <summary>
	/// Adds a finished map recap to the next batch.
	/// </summary>
	/// <param name="recap">The recap.</param>
	public void Record(MapRecap recap)
	{
		Ensure.NotNull(recap);

		pendingRecaps.Add(recap);
		if (pendingRecaps.Count > PendingFeedLimit)
		{
			pendingRecaps.RemoveRange(0, pendingRecaps.Count - PendingFeedLimit);
		}
	}

	/// <summary>
	/// Adds a kill to the next batch, for the live fight maps. A kill between two bots is left out, because it can't be anyone's fight.
	/// </summary>
	/// <param name="point">The kill.</param>
	public void Record(KillPoint point)
	{
		Ensure.NotNull(point);

		if ((point.KillerId == 0) && (point.VictimId == 0))
		{
			return;
		}

		pendingKills.Add(point);
		if (pendingKills.Count > PendingFeedLimit)
		{
			pendingKills.RemoveRange(0, pendingKills.Count - PendingFeedLimit);
		}
	}

	/// <summary>
	/// Notes that a map just started, so the site can tell this match's kills from the last one's.
	/// </summary>
	public void MapStarted() => mapStartedAt = DateTime.UtcNow;

	/// <summary>
	/// Reads the server's state and queues a batch for the site. Call it on the game thread. A failure is
	/// logged and skips this batch, so the site never breaks the game.
	/// </summary>
	[SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Collect runs from a game timer, where an exception would surface in the host. The site is optional, so any failure is logged and the batch skipped.")]
	public void Collect()
	{
		try
		{
			CollectBatch();
		}
		catch (Exception exception)
		{
			Log.EventFailed(log, "the site report", exception);
		}
	}

	/// <summary>
	/// Forgets what was sent for a player who left, so a return sends all of their rows.
	/// </summary>
	/// <param name="steamId">The player's 64-bit Steam ID.</param>
	public void Forget(ulong steamId) => tracker.Forget(steamId);

	/// <summary>
	/// Tells the site that an admin corrected a player, so it drops the live rows it holds for them, and sends
	/// all of their rows again with the next batch.
	/// </summary>
	/// <param name="steamId">The player's 64-bit Steam ID.</param>
	public void Corrected(ulong steamId)
	{
		tracker.Forget(steamId);
		if (!pendingCorrected.Contains(steamId))
		{
			pendingCorrected.Add(steamId);
		}
	}

	/// <summary>
	/// Stops the sender. Batches still queued are dropped.
	/// </summary>
	public void Dispose() => sender.Dispose();

	private static Uri? ParseUrl(string? url) =>
		Uri.TryCreate(url?.Trim(), UriKind.Absolute, out Uri? parsed) && ((parsed.Scheme == Uri.UriSchemeHttp) || (parsed.Scheme == Uri.UriSchemeHttps))
			? parsed
			: null;

	private void CollectBatch()
	{
		gameType ??= ConVar.Find("game_type");
		gameMode ??= ConVar.Find("game_mode");
		string mode = GameModes.Name(gameType?.GetPrimitiveValue<int>() ?? 0, gameMode?.GetPrimitiveValue<int>() ?? 0);
		ServerSnapshot snapshot = new(DateTime.UtcNow, Server.MapName ?? string.Empty, mode, Server.MaxPlayers, ReadPlayers(), mapStartedAt);

		int fullResendEvery = pluginConfig.Site.FullResendEvery;
		if ((fullResendEvery > 0) && (batchesSinceReset >= fullResendEvery))
		{
			tracker.Reset();
			batchesSinceReset = 0;
		}

		batchesSinceReset++;

		List<ProgressSnapshot> progress = [];
		foreach (PlayerSession session in sessionManager.All)
		{
			if ((session.Progress is PlayerProgress playerProgress) && (tracker.Changes(playerProgress) is ProgressSnapshot changes))
			{
				progress.Add(changes);
			}
		}

		FeedEntry[] feed = [.. pendingFeed];
		pendingFeed.Clear();
		MapRecap[] recaps = [.. pendingRecaps];
		pendingRecaps.Clear();

		ulong[] corrected = [.. pendingCorrected];
		pendingCorrected.Clear();
		KillPoint[] kills = [.. pendingKills];
		pendingKills.Clear();

		PluginStats stats = new(sender.SendFailures, sender.BatchesDropped, sessionManager.SaveFailures);
		sender.TryEnqueue(new LiveBatch(snapshot, feed, progress, pluginConfig.Progression, recaps, corrected, stats, kills));
	}

	private List<LivePlayer> ReadPlayers()
	{
		List<LivePlayer> players = [];
		foreach (CCSPlayerController controller in Utilities.GetPlayers())
		{
			if (!controller.IsValid || controller.IsHLTV || (controller.Connected != PlayerConnectedState.Connected))
			{
				continue;
			}

			CSMatchStats_t? stats = controller.ActionTrackingServices?.MatchStats;
			int kills = stats?.Kills ?? 0;
			int deaths = stats?.Deaths ?? 0;
			string name = controller.PlayerName ?? string.Empty;

			if (controller.IsBot)
			{
				players.Add(new LivePlayer(controller.Slot, 0, name, IsBot: true, Rank: 0, kills, deaths, MapXp: 0));
			}
			else if (GameState.IsHuman(controller))
			{
				PlayerSession? session = sessionManager.Get(controller);
				players.Add(new LivePlayer(controller.Slot, controller.SteamID, name, IsBot: false, session?.Progress?.Rank ?? 0, kills, deaths, session?.MapXp ?? 0));
			}
		}

		return players;
	}
}

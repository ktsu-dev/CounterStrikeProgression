// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site;

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Core.Progression;
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
	private readonly SessionManager sessionManager;
	private readonly LiveSender sender;
	private readonly ProgressChangeTracker tracker = new();
	private readonly List<FeedEntry> pendingFeed = [];
	private ConVar? gameType;
	private ConVar? gameMode;
	private int batchesSinceReset;

	/// <summary>
	/// Creates a reporter and starts its sender.
	/// </summary>
	/// <param name="config">The plugin config, whose <see cref="PluginConfig.Site"/> URL must be an absolute http or https URL.</param>
	/// <param name="sessions">The connected players.</param>
	/// <param name="logger">Where send failures are reported.</param>
	/// <exception cref="ArgumentException">The site URL isn't an absolute http or https URL.</exception>
	public SiteReporter(PluginConfig config, SessionManager sessions, ILogger logger)
	{
		Ensure.NotNull(config);
		Ensure.NotNull(sessions);
		Ensure.NotNull(logger);

		Uri ingestUrl = ParseUrl(config.Site.Url) ?? throw new ArgumentException("The site URL isn't an absolute http or https URL.", nameof(config));
		pluginConfig = config;
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
	/// Reads the server's state and queues a batch for the site. Call it on the game thread.
	/// </summary>
	public void Collect()
	{
		gameType ??= ConVar.Find("game_type");
		gameMode ??= ConVar.Find("game_mode");
		string mode = GameModes.Name(gameType?.GetPrimitiveValue<int>() ?? 0, gameMode?.GetPrimitiveValue<int>() ?? 0);
		ServerSnapshot snapshot = new(DateTime.UtcNow, Server.MapName ?? string.Empty, mode, Server.MaxPlayers, ReadPlayers());

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

		sender.TryEnqueue(new LiveBatch(snapshot, feed, progress, pluginConfig.Progression));
	}

	/// <summary>
	/// Forgets what was sent for a player who left, so a return sends all of their rows.
	/// </summary>
	/// <param name="steamId">The player's 64-bit Steam ID.</param>
	public void Forget(ulong steamId) => tracker.Forget(steamId);

	/// <summary>
	/// Stops the sender. Batches still queued are dropped.
	/// </summary>
	public void Dispose() => sender.Dispose();

	private static Uri? ParseUrl(string? url) =>
		Uri.TryCreate(url?.Trim(), UriKind.Absolute, out Uri? parsed) && ((parsed.Scheme == Uri.UriSchemeHttp) || (parsed.Scheme == Uri.UriSchemeHttps))
			? parsed
			: null;

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

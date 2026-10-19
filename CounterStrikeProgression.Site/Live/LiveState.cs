// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Live;

using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Core.Recap;
using ktsu.CounterStrikeProgression.Core.Storage;
using Microsoft.Extensions.Logging;

/// <summary>What one applied batch changed.</summary>
/// <param name="SnapshotChanged">Whether the server snapshot was replaced.</param>
/// <param name="NewFeed">The feed entries the batch added.</param>
/// <param name="ChangedPlayers">The Steam IDs whose progress rows or map recaps the batch carried, or whose rows it cleared.</param>
internal sealed record LiveChanges(bool SnapshotChanged, IReadOnlyList<FeedEntry> NewFeed, IReadOnlyList<ulong> ChangedPlayers);

/// <summary>
/// The site's picture of the server right now: the latest snapshot, the feed history, and the progress
/// rows that arrived since the site started. Ingest and page requests run on different threads, so one
/// lock guards all of it.
/// </summary>
/// <param name="options">The site settings.</param>
/// <param name="feed">Where the feed history is saved.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">Where feed history write failures are reported.</param>
internal sealed partial class LiveState(SiteOptions options, FeedStore feed, TimeProvider time, ILogger<LiveState> logger)
{
	private readonly Lock gate = new();
	private readonly Dictionary<ulong, ProgressSnapshot> overlays = [];
	private List<FeedEntry> entries = [.. feed.Load().Take(options.FeedLength)];
	private readonly Dictionary<ulong, List<FeedEntry>> playerFeeds = LoadPlayerFeeds(feed, options);
	private readonly Dictionary<ulong, MapRecap> recaps = LoadRecaps(feed, options);
	private ServerSnapshot? snapshot;
	private DateTimeOffset lastBatchAt;
	private PluginStats? pluginStats;
	private bool feedSaveFailing;

	/// <summary>Gets the latest server snapshot, or <see langword="null"/> before the first batch.</summary>
	public ServerSnapshot? Snapshot
	{
		get
		{
			lock (gate)
			{
				return snapshot;
			}
		}
	}

	/// <summary>Gets a value indicating whether a batch arrived within the offline window.</summary>
	public bool IsOnline
	{
		get
		{
			lock (gate)
			{
				return IsOnlineLocked();
			}
		}
	}

	/// <summary>Gets when the last batch arrived, or <see langword="null"/> before the first one.</summary>
	public DateTimeOffset? LastBatchAt
	{
		get
		{
			lock (gate)
			{
				return snapshot is null ? null : lastBatchAt;
			}
		}
	}

	/// <summary>Gets the plugin's health counters from the last batch that carried them.</summary>
	public PluginStats? PluginStats
	{
		get
		{
			lock (gate)
			{
				return pluginStats;
			}
		}
	}

	/// <summary>Gets the feed, newest first.</summary>
	public IReadOnlyList<FeedEntry> Feed
	{
		get
		{
			lock (gate)
			{
				return [.. entries];
			}
		}
	}

	/// <summary>
	/// Gets one player's feed.
	/// </summary>
	/// <param name="steamId">The 64-bit Steam ID.</param>
	/// <returns>The player's entries, newest first, or an empty list when none were seen.</returns>
	public IReadOnlyList<FeedEntry> PlayerFeed(ulong steamId)
	{
		lock (gate)
		{
			return playerFeeds.TryGetValue(steamId, out List<FeedEntry>? playerFeed) ? [.. playerFeed] : [];
		}
	}

	/// <summary>
	/// Gets a player's last map recap.
	/// </summary>
	/// <param name="steamId">The 64-bit Steam ID.</param>
	/// <returns>The newest recap, or <see langword="null"/> when none arrived.</returns>
	public MapRecap? LastSession(ulong steamId)
	{
		lock (gate)
		{
			return recaps.GetValueOrDefault(steamId);
		}
	}

	/// <summary>Gets the Steam IDs that have live progress rows.</summary>
	public IReadOnlyList<ulong> OverlayIds
	{
		get
		{
			lock (gate)
			{
				return [.. overlays.Keys];
			}
		}
	}

	/// <summary>Gets the plugin's latest settings, or Core's defaults before the first batch. Replaced as a whole, never edited in place.</summary>
	public ProgressionSettings Settings { get; private set; } = new();

	/// <summary>
	/// Applies a batch from the plugin.
	/// </summary>
	/// <param name="batch">The batch.</param>
	/// <returns>What changed.</returns>
	public LiveChanges Apply(LiveBatch batch)
	{
		Ensure.NotNull(batch);

		lock (gate)
		{
			lastBatchAt = time.GetUtcNow();
			Settings = batch.Settings;
			pluginStats = batch.Stats ?? pluginStats;

			bool snapshotChanged = snapshot is null || batch.Snapshot.SentAt >= snapshot.SentAt;
			if (snapshotChanged)
			{
				snapshot = batch.Snapshot;
			}

			if (batch.Feed.Count > 0)
			{
				IEnumerable<FeedEntry> added = batch.Feed.OrderByDescending(e => e.At);
				entries = [.. added.Concat(entries).Take(options.FeedLength)];
				SaveFeed(AddToPlayerFeeds(batch.Feed));
			}

			List<ulong> changed = MergeProgress(batch.Corrected ?? [], batch.Progress);
			foreach (ulong steamId in AddRecaps(batch.Recaps ?? []))
			{
				if (!changed.Contains(steamId))
				{
					changed.Add(steamId);
				}
			}

			return new(snapshotChanged, batch.Feed, changed);
		}
	}

	/// <summary>
	/// Gets the live progress rows received for a player.
	/// </summary>
	/// <param name="steamId">The 64-bit Steam ID.</param>
	/// <returns>The combined rows, or <see langword="null"/> when none arrived.</returns>
	public ProgressSnapshot? Overlay(ulong steamId)
	{
		lock (gate)
		{
			return overlays.GetValueOrDefault(steamId);
		}
	}

	/// <summary>
	/// Checks whether a player is on the server now.
	/// </summary>
	/// <param name="steamId">The 64-bit Steam ID.</param>
	/// <returns><see langword="true"/> when the server is online and lists the player as a non-bot.</returns>
	public bool IsPlaying(ulong steamId)
	{
		lock (gate)
		{
			return IsOnlineLocked() && snapshot!.Players.Any(p => !p.IsBot && (p.SteamId == steamId));
		}
	}

	private List<ulong> MergeProgress(IReadOnlyList<ulong> corrected, IReadOnlyList<ProgressSnapshot> progress)
	{
		// Live rows only ever merge upward, so a correction that lowered someone's progress has to clear them first.
		List<ulong> changed = [];
		foreach (ulong steamId in corrected)
		{
			if (overlays.Remove(steamId) && !changed.Contains(steamId))
			{
				changed.Add(steamId);
			}
		}

		foreach (ProgressSnapshot rows in progress)
		{
			if (ProgressMerge.SteamIdOf(rows) is not { } steamId)
			{
				continue;
			}

			overlays[steamId] = overlays.TryGetValue(steamId, out ProgressSnapshot? existing) ? ProgressMerge.Combine(existing, rows) : rows;
			if (!changed.Contains(steamId))
			{
				changed.Add(steamId);
			}
		}

		return changed;
	}

	private static Dictionary<ulong, MapRecap> LoadRecaps(FeedStore feed, SiteOptions options) =>
		feed.LoadRecaps()
			.Where(r => r is not null && (r.SteamId != 0))
			.GroupBy(r => r.SteamId)
			.Select(g => g.MaxBy(r => r.EndedAt)!)
			.OrderByDescending(r => r.EndedAt)
			.Take(options.PlayerFeedPlayers)
			.ToDictionary(r => r.SteamId);

	/// <summary>
	/// Keeps each player's newest recap, and drops the oldest once more than <see cref="SiteOptions.PlayerFeedPlayers"/> have one.
	/// </summary>
	/// <returns>The players whose last session changed.</returns>
	private List<ulong> AddRecaps(IReadOnlyList<MapRecap> added)
	{
		List<ulong> changed = [];
		foreach (MapRecap recap in added.Where(r => r.SteamId != 0).OrderBy(r => r.EndedAt))
		{
			// A batch the plugin sends again after a timeout carries a recap this state already has.
			if (recaps.TryGetValue(recap.SteamId, out MapRecap? existing) && (existing.EndedAt >= recap.EndedAt))
			{
				continue;
			}

			recaps[recap.SteamId] = recap;
			if (!changed.Contains(recap.SteamId))
			{
				changed.Add(recap.SteamId);
			}
		}

		if (changed.Count == 0)
		{
			return changed;
		}

		if (recaps.Count > options.PlayerFeedPlayers)
		{
			foreach (MapRecap oldest in recaps.Values.OrderBy(r => r.EndedAt).Take(recaps.Count - options.PlayerFeedPlayers).ToList())
			{
				recaps.Remove(oldest.SteamId);
				changed.Remove(oldest.SteamId);
			}
		}

		SaveRecaps();
		return changed;
	}

	private void SaveRecaps()
	{
		try
		{
			feed.SaveRecaps([.. recaps.Values]);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// Like the feed, the recaps stay in memory, and the warning goes out once per failure streak.
			if (!feedSaveFailing)
			{
				feedSaveFailing = true;
				LogFeedSaveFailed(logger, ex, feed.RecapsPath);
			}
		}
	}

	private static Dictionary<ulong, List<FeedEntry>> LoadPlayerFeeds(FeedStore feed, SiteOptions options) =>
		feed.LoadPlayers()
			.OrderByDescending(p => p.Value.Max(e => e.At))
			.Take(options.PlayerFeedPlayers)
			.ToDictionary(p => p.Key, p => p.Value.OrderByDescending(e => e.At).Take(options.PlayerFeedLength).ToList());

	/// <summary>
	/// Adds a batch's entries to each player's feed, and drops the players whose newest entry is oldest once
	/// more than <see cref="SiteOptions.PlayerFeedPlayers"/> have one.
	/// </summary>
	/// <returns>The players whose feed changed, and the players dropped.</returns>
	private (List<ulong> Changed, List<ulong> Dropped) AddToPlayerFeeds(IReadOnlyList<FeedEntry> added)
	{
		List<ulong> changed = [];
		foreach (IGrouping<ulong, FeedEntry> group in added.Where(e => e.SteamId != 0).GroupBy(e => e.SteamId))
		{
			// A batch the plugin sends again after a timeout carries entries this feed already has.
			List<FeedEntry> existing = playerFeeds.GetValueOrDefault(group.Key) ?? [];
			List<FeedEntry> fresh = [.. group.Distinct().Where(e => !existing.Contains(e))];
			if (fresh.Count == 0)
			{
				continue;
			}

			playerFeeds[group.Key] = [.. fresh.Concat(existing).OrderByDescending(e => e.At).Take(options.PlayerFeedLength)];
			changed.Add(group.Key);
		}

		List<ulong> dropped = [];
		if (playerFeeds.Count > options.PlayerFeedPlayers)
		{
			dropped = [.. playerFeeds.OrderBy(p => p.Value[0].At).Take(playerFeeds.Count - options.PlayerFeedPlayers).Select(p => p.Key)];
			foreach (ulong steamId in dropped)
			{
				playerFeeds.Remove(steamId);
			}

			changed.RemoveAll(dropped.Contains);
		}

		return (changed, dropped);
	}

	private void SaveFeed((List<ulong> Changed, List<ulong> Dropped) players)
	{
		try
		{
			feed.Save(entries);
			foreach (ulong steamId in players.Changed)
			{
				feed.SavePlayer(steamId, playerFeeds[steamId]);
			}

			foreach (ulong steamId in players.Dropped)
			{
				feed.DeletePlayer(steamId);
			}

			if (feedSaveFailing)
			{
				feedSaveFailing = false;
				LogFeedSaveRecovered(logger, feed.Path);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// The batch stays applied in memory, so a disk fault can't block live updates. The warning goes out
			// once per failure streak, because batches arrive every few seconds.
			if (!feedSaveFailing)
			{
				feedSaveFailing = true;
				LogFeedSaveFailed(logger, ex, feed.Path);
			}
		}
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "Can't write the feed history to {Path}, so it won't survive a restart. Live updates go on.")]
	private static partial void LogFeedSaveFailed(ILogger logger, Exception exception, string path);

	[LoggerMessage(Level = LogLevel.Information, Message = "Writing the feed history to {Path} works again.")]
	private static partial void LogFeedSaveRecovered(ILogger logger, string path);

	private bool IsOnlineLocked() =>
		(snapshot is not null) && ((time.GetUtcNow() - lastBatchAt).TotalSeconds <= options.OfflineAfterSeconds);
}

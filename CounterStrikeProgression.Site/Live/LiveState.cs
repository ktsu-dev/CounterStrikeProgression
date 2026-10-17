// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Live;

using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Core.Storage;

/// <summary>What one applied batch changed.</summary>
/// <param name="SnapshotChanged">Whether the server snapshot was replaced.</param>
/// <param name="NewFeed">The feed entries the batch added.</param>
/// <param name="ChangedPlayers">The Steam IDs whose progress rows the batch carried.</param>
internal sealed record LiveChanges(bool SnapshotChanged, IReadOnlyList<FeedEntry> NewFeed, IReadOnlyList<ulong> ChangedPlayers);

/// <summary>
/// The site's picture of the server right now: the latest snapshot, the feed history, and the progress
/// rows that arrived since the site started. Ingest and page requests run on different threads, so one
/// lock guards all of it.
/// </summary>
/// <param name="options">The site settings.</param>
/// <param name="feed">Where the feed history is saved.</param>
/// <param name="time">The clock.</param>
internal sealed class LiveState(SiteOptions options, FeedStore feed, TimeProvider time)
{
	private readonly Lock gate = new();
	private readonly Dictionary<ulong, ProgressSnapshot> overlays = [];
	private List<FeedEntry> entries = [.. feed.Load().Take(options.FeedLength)];
	private ServerSnapshot? snapshot;
	private DateTimeOffset lastBatchAt;

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

			bool snapshotChanged = snapshot is null || batch.Snapshot.SentAt >= snapshot.SentAt;
			if (snapshotChanged)
			{
				snapshot = batch.Snapshot;
			}

			if (batch.Feed.Count > 0)
			{
				IEnumerable<FeedEntry> added = batch.Feed.OrderByDescending(e => e.At);
				entries = [.. added.Concat(entries).Take(options.FeedLength)];
				SaveFeed();
			}

			return new(snapshotChanged, batch.Feed, MergeProgress(batch.Progress));
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

	private List<ulong> MergeProgress(IReadOnlyList<ProgressSnapshot> progress)
	{
		List<ulong> changed = [];
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

	private void SaveFeed()
	{
		try
		{
			feed.Save(entries);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// The history is only a convenience across restarts, so a failed write keeps the batch applied in memory.
		}
	}

	private bool IsOnlineLocked() =>
		(snapshot is not null) && ((time.GetUtcNow() - lastBatchAt).TotalSeconds <= options.OfflineAfterSeconds);
}

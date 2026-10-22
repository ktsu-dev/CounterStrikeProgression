// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Live;

using System.Data.Common;
using ktsu.CounterStrikeProgression.Core.Heatmaps;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Site.Data;
using Microsoft.Extensions.Logging;

/// <summary>The current match: its map and its kills, read together.</summary>
/// <param name="Map">The map of the match, or <see langword="null"/> before the first batch.</param>
/// <param name="Kills">The match's kills, newest first.</param>
internal sealed record MatchView(string? Map, IReadOnlyList<KillPoint> Kills);

/// <summary>
/// The kills of the match being played now, taken from the plugin's live batches. It starts over when the map
/// changes, and after a site restart it reads the match so far back from the kill store, because the plugin
/// sends each kill only once. Ingest and page requests run on different threads, so one lock guards the list.
/// </summary>
/// <param name="store">The kill store, read after a restart or a map change.</param>
/// <param name="logger">Where store read failures are reported.</param>
internal sealed partial class MatchFights(IKillMapStore store, ILogger<MatchFights> logger)
{
	private readonly Lock gate = new();
	private readonly List<KillPoint> match = [];
	private readonly HashSet<KillPoint> seen = [];
	private string? map;
	private DateTime? startedAt;
	private DateTime lastSentAt = DateTime.MinValue;
	private int generation;
	private bool needsStore;

	/// <summary>
	/// Takes a batch's kills. A snapshot on a new map, or with a later <see cref="ServerSnapshot.MapStartedAt"/>,
	/// starts a new match first. Kills on another map, from before the match started, or already held are left out, and so
	/// are kills with no id on either end, between two bots that have no persona. A bot persona's kills are kept like a human's.
	/// </summary>
	/// <param name="snapshot">The batch's server snapshot.</param>
	/// <param name="kills">The batch's kills, or <see langword="null"/> when it carried none.</param>
	/// <returns>The kills kept, oldest first.</returns>
	public IReadOnlyList<KillPoint> Apply(ServerSnapshot snapshot, IReadOnlyList<KillPoint>? kills)
	{
		Ensure.NotNull(snapshot);

		lock (gate)
		{
			// A batch the plugin sends again after a timeout can carry an older snapshot, which mustn't undo a map change.
			if (snapshot.SentAt >= lastSentAt)
			{
				lastSentAt = snapshot.SentAt;
				bool newMap = !string.Equals(snapshot.Map, map, StringComparison.Ordinal);
				bool restarted = snapshot.MapStartedAt is { } started && ((startedAt is null) || (started > startedAt));
				if (newMap || restarted)
				{
					Start(snapshot.Map, snapshot.MapStartedAt);
				}
			}

			List<KillPoint> kept = [];
			foreach (KillPoint kill in (kills ?? []).Where(k => k is not null).OrderBy(k => k.At))
			{
				if (IsInMatch(kill) && seen.Add(kill))
				{
					match.Add(kill);
					kept.Add(kill);
				}
			}

			Trim();
			return kept;
		}
	}

	/// <summary>
	/// Gets the current match's map and kills, taken under one lock so they always belong together. The first call after a restart or a map change also reads the
	/// match so far from the store, when the plugin said when the match started.
	/// </summary>
	/// <param name="cancellationToken">Stops waiting for the store.</param>
	/// <returns>The map and the kills, newest first.</returns>
	public async Task<MatchView> CurrentAsync(CancellationToken cancellationToken)
	{
		string? storedMap;
		DateTime? since;
		int storedGeneration;
		lock (gate)
		{
			if (!needsStore)
			{
				return Snapshot();
			}

			// MySQL DATETIME keeps whole seconds, so a kill stored in the match's first second is dated before a fractional start.
			since = StoredSince();
			(storedMap, storedGeneration) = (map, generation);
		}

		IReadOnlyList<KillPoint> stored;
		try
		{
			stored = await store.FightsAsync(storedMap!, null, since, HeatmapReader.ServerLimit, cancellationToken).ConfigureAwait(false);
		}
		catch (DbException ex)
		{
			// The live kills still show, and the next call tries the store again.
			LogStoreReadFailed(logger, ex, storedMap!);
			lock (gate)
			{
				return Snapshot();
			}
		}

		lock (gate)
		{
			// A map change while the store was read makes what it returned another match's kills.
			if (needsStore && (generation == storedGeneration))
			{
				needsStore = false;
				AddStored(stored);
			}

			return Snapshot();
		}
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "Can't read this match's kills on {Map} from the kill store, so its fight maps show only the kills since the site started.")]
	private static partial void LogStoreReadFailed(ILogger logger, Exception exception, string map);

	private void Start(string newMap, DateTime? newStart)
	{
		map = newMap;
		startedAt = newStart;
		match.Clear();
		seen.Clear();
		generation++;
		needsStore = newStart is not null;
	}

	/// <summary>
	/// Adds the stored kills that arrived in no live batch. The store keeps whole seconds and no names, so a stored kill
	/// matches a live one with the same players, weapon, and positions less than a second apart.
	/// </summary>
	private void AddStored(IReadOnlyList<KillPoint> stored)
	{
		ILookup<KillPoint, DateTime> live = match.ToLookup(StoredKey, k => k.At);
		foreach (KillPoint kill in stored)
		{
			if (IsInMatch(kill, StoredSince()) && !live[StoredKey(kill)].Any(at => Math.Abs((at - kill.At).Ticks) < TimeSpan.TicksPerSecond) && seen.Add(kill))
			{
				match.Add(kill);
			}
		}

		match.Sort((a, b) => a.At.CompareTo(b.At));
		Trim();
	}

	/// <summary>Gets what the store keeps of a kill apart from its time, so live and stored copies compare equal.</summary>
	private static KillPoint StoredKey(KillPoint kill) => kill with { At = default, KillerName = null, VictimName = null };

	/// <summary>Gets the match start floored to a whole second, which is how far the store's dates reach.</summary>
	private DateTime? StoredSince() =>
		startedAt is { } start ? start.AddTicks(-(start.Ticks % TimeSpan.TicksPerSecond)) : null;

	private bool IsInMatch(KillPoint kill) => IsInMatch(kill, startedAt);

	private bool IsInMatch(KillPoint kill, DateTime? since) =>
		((kill.KillerId != 0) || (kill.VictimId != 0))
		&& string.Equals(kill.Map, map, StringComparison.Ordinal)
		&& ((since is null) || (kill.At >= since));

	private void Trim()
	{
		int over = match.Count - HeatmapReader.ServerLimit;
		if (over <= 0)
		{
			return;
		}

		foreach (KillPoint oldest in match.Take(over))
		{
			seen.Remove(oldest);
		}

		match.RemoveRange(0, over);
	}

	private MatchView Snapshot() => new(map, [.. Enumerable.Reverse(match)]);
}

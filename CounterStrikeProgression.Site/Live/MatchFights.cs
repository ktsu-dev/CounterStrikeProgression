// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Live;

using System.Data.Common;
using ktsu.CounterStrikeProgression.Core.Heatmaps;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Site.Data;
using Microsoft.Extensions.Logging;

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

	/// <summary>Gets the map of the current match, or <see langword="null"/> before the first batch.</summary>
	public string? Map
	{
		get
		{
			lock (gate)
			{
				return map;
			}
		}
	}

	/// <summary>
	/// Takes a batch's kills. A snapshot on a new map, or with a later <see cref="ServerSnapshot.MapStartedAt"/>,
	/// starts a new match first. Kills on another map, from before the match started, or already held are left out.
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
	/// Gets the kills of the current match. The first call after a restart or a map change also reads the
	/// match so far from the store, when the plugin said when the match started.
	/// </summary>
	/// <param name="cancellationToken">Stops waiting for the store.</param>
	/// <returns>The kills, newest first.</returns>
	public async Task<IReadOnlyList<KillPoint>> CurrentAsync(CancellationToken cancellationToken)
	{
		string? storedMap;
		DateTime? since;
		int storedGeneration;
		lock (gate)
		{
			if (!needsStore)
			{
				return NewestFirst();
			}

			(storedMap, since, storedGeneration) = (map, startedAt, generation);
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
				return NewestFirst();
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

			return NewestFirst();
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
	/// Adds the stored kills that arrived in no live batch. The store keeps whole seconds, so a stored kill
	/// matches a live one with the same players, weapon, and positions less than a second apart.
	/// </summary>
	private void AddStored(IReadOnlyList<KillPoint> stored)
	{
		ILookup<KillPoint, DateTime> live = match.ToLookup(k => k with { At = default }, k => k.At);
		foreach (KillPoint kill in stored)
		{
			if (IsInMatch(kill) && !live[kill with { At = default }].Any(at => Math.Abs((at - kill.At).Ticks) < TimeSpan.TicksPerSecond) && seen.Add(kill))
			{
				match.Add(kill);
			}
		}

		match.Sort((a, b) => a.At.CompareTo(b.At));
		Trim();
	}

	private bool IsInMatch(KillPoint kill) =>
		string.Equals(kill.Map, map, StringComparison.Ordinal)
		&& ((startedAt is null) || (kill.At >= startedAt));

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

	private List<KillPoint> NewestFirst() => [.. Enumerable.Reverse(match)];
}

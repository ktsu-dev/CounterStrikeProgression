// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Data;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;

/// <summary>
/// Reads stored progress for the pages, and keeps each answer for a few seconds so busy pages don't
/// query the database on every request. At most <see cref="MaxConcurrentQueries"/> queries run at once,
/// and requests that miss the cache for the same answer share one query. Only reads: it never
/// initializes or saves.
/// </summary>
/// <param name="store">The progress store.</param>
/// <param name="time">The clock, or the system clock when omitted.</param>
internal sealed class ProgressReader(IProgressionStore store, TimeProvider? time = null) : IDisposable
{
	/// <summary>How many players the ranks page reads.</summary>
	public const int RankingsCount = 500;

	/// <summary>A count large enough to read every player, for the rank distribution.</summary>
	public const int AllPlayers = 1_000_000;

	/// <summary>The lowest individual-account SteamID64.</summary>
	public const ulong MinSteamId = 76561197960265729;

	/// <summary>The highest individual-account SteamID64.</summary>
	public const ulong MaxSteamId = 76561202255233023;

	/// <summary>How many database queries may run at once, so the public pages can't tie up the game's database.</summary>
	public const int MaxConcurrentQueries = 4;

	/// <summary>How long an answer is kept.</summary>
	public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(10);

	private const int MaxCachedPlayers = 1024;

	private readonly TimeProvider clock = time ?? TimeProvider.System;
	private readonly SemaphoreSlim slots = new(MaxConcurrentQueries, MaxConcurrentQueries);
	private readonly Lock gate = new();
	private readonly Dictionary<ulong, (PlayerProgress? Progress, DateTimeOffset Until)> players = [];
	private readonly Dictionary<ulong, Lazy<Task<PlayerProgress?>>> loadingPlayers = [];
	private readonly Dictionary<int, (IReadOnlyList<RankingEntry> Rankings, DateTimeOffset Until)> rankings = [];
	private readonly Dictionary<int, Lazy<Task<IReadOnlyList<RankingEntry>>>> loadingRankings = [];

	/// <summary>
	/// Parses a SteamID64 from a URL.
	/// </summary>
	/// <param name="text">The text.</param>
	/// <param name="steamId">The Steam ID, or 0 when the text isn't one.</param>
	/// <returns><see langword="true"/> when the text is only ASCII digits and names an individual account.</returns>
	public static bool TryParseSteamId(string? text, out ulong steamId)
	{
		steamId = 0;
		if (string.IsNullOrEmpty(text) || text.Length != 17 || !text.All(char.IsAsciiDigit))
		{
			return false;
		}

		if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed) || parsed is < MinSteamId or > MaxSteamId)
		{
			return false;
		}

		steamId = parsed;
		return true;
	}

	/// <summary>
	/// Gets the stored rankings, highest rank XP first.
	/// </summary>
	/// <param name="cancellationToken">Stops this caller waiting. A query other callers share runs on.</param>
	/// <returns>Up to <see cref="RankingsCount"/> rows.</returns>
	public Task<IReadOnlyList<RankingEntry>> RankingsAsync(CancellationToken cancellationToken = default) => RankingsAsync(RankingsCount, cancellationToken);

	/// <summary>
	/// Gets the stored rankings, highest rank XP first, up to a count. Each count is cached on its own.
	/// </summary>
	/// <param name="count">How many rows to read.</param>
	/// <param name="cancellationToken">Stops this caller waiting. A query other callers share runs on.</param>
	/// <returns>Up to <paramref name="count"/> rows.</returns>
	public Task<IReadOnlyList<RankingEntry>> RankingsAsync(int count, CancellationToken cancellationToken = default)
	{
		Lazy<Task<IReadOnlyList<RankingEntry>>> load;
		lock (gate)
		{
			if (rankings.TryGetValue(count, out (IReadOnlyList<RankingEntry> Rankings, DateTimeOffset Until) cached) && clock.GetUtcNow() < cached.Until)
			{
				return Task.FromResult(cached.Rankings);
			}

			if (!loadingRankings.TryGetValue(count, out Lazy<Task<IReadOnlyList<RankingEntry>>>? existing))
			{
				existing = new(() => LoadRankingsAsync(count));
				loadingRankings[count] = existing;
			}

			load = existing;
		}

		return load.Value.WaitAsync(cancellationToken);
	}

	/// <summary>
	/// Gets a player's stored progress. The result is shared between requests, so callers must not change it.
	/// </summary>
	/// <param name="steamId">The 64-bit Steam ID.</param>
	/// <param name="cancellationToken">Stops this caller waiting. A query other callers share runs on.</param>
	/// <returns>The progress, or <see langword="null"/> for a player the database doesn't have.</returns>
	public Task<PlayerProgress?> PlayerAsync(ulong steamId, CancellationToken cancellationToken = default)
	{
		Lazy<Task<PlayerProgress?>> load;
		lock (gate)
		{
			if (players.TryGetValue(steamId, out (PlayerProgress? Progress, DateTimeOffset Until) cached) && clock.GetUtcNow() < cached.Until)
			{
				return Task.FromResult(cached.Progress);
			}

			if (!loadingPlayers.TryGetValue(steamId, out Lazy<Task<PlayerProgress?>>? existing))
			{
				existing = new(() => LoadPlayerAsync(steamId));
				loadingPlayers[steamId] = existing;
			}

			load = existing;
		}

		return load.Value.WaitAsync(cancellationToken);
	}

	/// <inheritdoc/>
	public void Dispose() => slots.Dispose();

	private async Task<IReadOnlyList<RankingEntry>> LoadRankingsAsync(int count)
	{
		try
		{
			DateTimeOffset now = clock.GetUtcNow();
			IReadOnlyList<RankingEntry> fresh = await QueryAsync(() => store.RankingsAsync(count, CancellationToken.None)).ConfigureAwait(false);
			lock (gate)
			{
				rankings[count] = (fresh, now + CacheFor);
			}

			return fresh;
		}
		finally
		{
			lock (gate)
			{
				loadingRankings.Remove(count);
			}
		}
	}

	private async Task<PlayerProgress?> LoadPlayerAsync(ulong steamId)
	{
		try
		{
			DateTimeOffset now = clock.GetUtcNow();
			PlayerProgress? fresh = await QueryAsync(() => store.LoadAsync(steamId, CancellationToken.None)).ConfigureAwait(false);
			lock (gate)
			{
				if (players.Count >= MaxCachedPlayers && !players.ContainsKey(steamId))
				{
					Prune(now);
				}

				players[steamId] = (fresh, now + CacheFor);
			}

			return fresh;
		}
		finally
		{
			lock (gate)
			{
				loadingPlayers.Remove(steamId);
			}
		}
	}

	private async Task<T> QueryAsync<T>(Func<Task<T>> query)
	{
		await slots.WaitAsync().ConfigureAwait(false);
		try
		{
			return await query().ConfigureAwait(false);
		}
		finally
		{
			slots.Release();
		}
	}

	private void Prune(DateTimeOffset now)
	{
		foreach (ulong key in players.Where(p => p.Value.Until <= now).Select(p => p.Key).ToList())
		{
			players.Remove(key);
		}

		if (players.Count >= MaxCachedPlayers)
		{
			players.Clear();
		}
	}
}

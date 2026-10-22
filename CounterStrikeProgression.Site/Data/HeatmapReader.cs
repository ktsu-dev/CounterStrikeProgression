// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Data;

using ktsu.CounterStrikeProgression.Core.Heatmaps;
/// <summary>
/// Reads kill positions for the heatmap page, and keeps each answer for <see cref="CacheFor"/>, because a map's
/// kills only change at the plugin's next write. At most <see cref="MaxConcurrentQueries"/> queries run at once,
/// and requests for the same answer share one query.
/// </summary>
/// <param name="store">The kill store.</param>
/// <param name="time">The clock, or the system clock when omitted.</param>
internal sealed class HeatmapReader(IKillMapStore store, TimeProvider? time = null) : IDisposable
{
	/// <summary>The most server-wide positions read for one map and kind.</summary>
	public const int ServerLimit = 30_000;

	/// <summary>The most positions read for one player on one map and kind.</summary>
	public const int PlayerLimit = 5_000;

	/// <summary>How many database queries may run at once.</summary>
	public const int MaxConcurrentQueries = 2;

	/// <summary>How long an answer is kept.</summary>
	public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

	private const int MaxCached = 512;

	private readonly TimeProvider clock = time ?? TimeProvider.System;
	private readonly SemaphoreSlim slots = new(MaxConcurrentQueries, MaxConcurrentQueries);
	private readonly Lock gate = new();
	private readonly Dictionary<object, (object Value, DateTimeOffset Until)> cache = [];
	private readonly Dictionary<object, Lazy<Task<object>>> loading = [];

	/// <summary>
	/// Gets every map with kills kept.
	/// </summary>
	/// <param name="cancellationToken">Stops this caller waiting. A query other callers share runs on.</param>
	/// <returns>The maps, most kills first.</returns>
	public async Task<IReadOnlyList<MapPointCount>> MapsAsync(CancellationToken cancellationToken = default) =>
		(IReadOnlyList<MapPointCount>)await Cached("maps", async () => await store.MapsAsync(CancellationToken.None).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

	/// <summary>
	/// Gets the latest positions on a map, for the server or one player.
	/// </summary>
	/// <param name="map">The map.</param>
	/// <param name="kind">Kills or deaths.</param>
	/// <param name="steamId">The player, or <see langword="null"/> for everyone.</param>
	/// <param name="cancellationToken">Stops this caller waiting. A query other callers share runs on.</param>
	/// <returns>The positions, newest first.</returns>
	public async Task<IReadOnlyList<HeatPoint>> PointsAsync(string map, HeatmapKind kind, ulong? steamId, CancellationToken cancellationToken = default)
	{
		int limit = steamId is null ? ServerLimit : PlayerLimit;
		return (IReadOnlyList<HeatPoint>)await Cached((map, kind, steamId), async () => await store.PointsAsync(map, kind, steamId, limit, CancellationToken.None).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Gets the latest kills on a map with both ends, for the server or the fights one player was in.
	/// </summary>
	/// <param name="map">The map.</param>
	/// <param name="steamId">The player who was the killer or the victim, or <see langword="null"/> for every kill.</param>
	/// <param name="cancellationToken">Stops this caller waiting. A query other callers share runs on.</param>
	/// <returns>The kills, newest first.</returns>
	public async Task<IReadOnlyList<KillPoint>> FightsAsync(string map, ulong? steamId, CancellationToken cancellationToken = default)
	{
		int limit = steamId is null ? ServerLimit : PlayerLimit;
		return (IReadOnlyList<KillPoint>)await Cached(("fights", map, steamId), async () => await store.FightsAsync(map, steamId, null, limit, CancellationToken.None).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public void Dispose() => slots.Dispose();

	private Task<object> Cached(object key, Func<Task<object>> query, CancellationToken cancellationToken)
	{
		Lazy<Task<object>> load;
		lock (gate)
		{
			if (cache.TryGetValue(key, out (object Value, DateTimeOffset Until) cached) && clock.GetUtcNow() < cached.Until)
			{
				return Task.FromResult(cached.Value);
			}

			if (!loading.TryGetValue(key, out Lazy<Task<object>>? existing))
			{
				existing = new(() => LoadAsync(key, query));
				loading[key] = existing;
			}

			load = existing;
		}

		return load.Value.WaitAsync(cancellationToken);
	}

	private async Task<object> LoadAsync(object key, Func<Task<object>> query)
	{
		try
		{
			object fresh;
			await slots.WaitAsync().ConfigureAwait(false);
			try
			{
				fresh = await query().ConfigureAwait(false);
			}
			finally
			{
				slots.Release();
			}

			lock (gate)
			{
				if ((cache.Count >= MaxCached) && !cache.ContainsKey(key))
				{
					DateTimeOffset now = clock.GetUtcNow();
					foreach (object stale in cache.Where(c => c.Value.Until <= now).Select(c => c.Key).ToList())
					{
						cache.Remove(stale);
					}

					if (cache.Count >= MaxCached)
					{
						cache.Clear();
					}
				}

				cache[key] = (fresh, clock.GetUtcNow() + CacheFor);
			}

			return fresh;
		}
		finally
		{
			lock (gate)
			{
				loading.Remove(key);
			}
		}
	}
}

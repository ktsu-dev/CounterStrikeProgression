// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Data;

using ktsu.CounterStrikeProgression.Core.Storage;

/// <summary>
/// The XP history reads, cached and limited like the others.
/// </summary>
internal sealed partial class ProgressReader
{
	private readonly SharedCache<(ulong SteamId, DateOnly From), IReadOnlyList<DailyXp>> histories = new(MaxCachedPlayers);
	private readonly SharedCache<(DateOnly Start, int Count), IReadOnlyList<XpGainEntry>> gains = new(MaxCachedPlayers);

	/// <summary>
	/// Gets a player's stored daily rows from a day on, with the rows <see cref="XpHistory"/> needs to measure the first day.
	/// </summary>
	/// <param name="steamId">The 64-bit Steam ID.</param>
	/// <param name="from">The first day wanted.</param>
	/// <param name="cancellationToken">Stops this caller waiting. A query other callers share runs on.</param>
	/// <returns>The rows, earliest first.</returns>
	public Task<IReadOnlyList<DailyXp>> HistoryAsync(ulong steamId, DateOnly from, CancellationToken cancellationToken = default) =>
		histories.GetAsync((steamId, from), clock, () => QueryAsync(() => store.HistoryAsync(steamId, from, CancellationToken.None)), cancellationToken);

	/// <summary>
	/// Gets the stored players who earned the most rank XP from a day on.
	/// </summary>
	/// <param name="start">The first day to count.</param>
	/// <param name="count">How many to read.</param>
	/// <param name="cancellationToken">Stops this caller waiting. A query other callers share runs on.</param>
	/// <returns>The players, most earned first.</returns>
	public Task<IReadOnlyList<XpGainEntry>> GainsSinceAsync(DateOnly start, int count, CancellationToken cancellationToken = default) =>
		gains.GetAsync((start, count), clock, () => QueryAsync(() => store.GainsSinceAsync(start, count, CancellationToken.None)), cancellationToken);

	/// <summary>
	/// Keeps each answer for <see cref="CacheFor"/>, and has callers that miss the cache for the same key share one load.
	/// </summary>
	/// <typeparam name="TKey">What an answer is for.</typeparam>
	/// <typeparam name="TValue">The answer.</typeparam>
	/// <param name="capacity">How many answers to keep before the expired ones are dropped.</param>
	private sealed class SharedCache<TKey, TValue>(int capacity)
		where TKey : notnull
	{
		private readonly Lock gate = new();
		private readonly Dictionary<TKey, (TValue Value, DateTimeOffset Until)> answers = [];
		private readonly Dictionary<TKey, Lazy<Task<TValue>>> loading = [];

		public Task<TValue> GetAsync(TKey key, TimeProvider clock, Func<Task<TValue>> load, CancellationToken cancellationToken)
		{
			Lazy<Task<TValue>> pending;
			lock (gate)
			{
				if (answers.TryGetValue(key, out (TValue Value, DateTimeOffset Until) cached) && clock.GetUtcNow() < cached.Until)
				{
					return Task.FromResult(cached.Value);
				}

				if (!loading.TryGetValue(key, out Lazy<Task<TValue>>? existing))
				{
					existing = new(() => LoadAsync(key, clock, load));
					loading[key] = existing;
				}

				pending = existing;
			}

			return pending.Value.WaitAsync(cancellationToken);
		}

		private async Task<TValue> LoadAsync(TKey key, TimeProvider clock, Func<Task<TValue>> load)
		{
			try
			{
				DateTimeOffset now = clock.GetUtcNow();
				TValue fresh = await load().ConfigureAwait(false);
				lock (gate)
				{
					if (answers.Count >= capacity && !answers.ContainsKey(key))
					{
						foreach (TKey stale in answers.Where(a => a.Value.Until <= now).Select(a => a.Key).ToList())
						{
							answers.Remove(stale);
						}

						if (answers.Count >= capacity)
						{
							answers.Clear();
						}
					}

					answers[key] = (fresh, now + CacheFor);
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
}

// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Data;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;

/// <summary>
/// Reads stored progress for the pages, and keeps each answer for a few seconds so busy pages don't
/// query the database on every request. Only reads: it never initializes or saves.
/// </summary>
/// <param name="store">The progress store.</param>
/// <param name="time">The clock, or the system clock when omitted.</param>
internal sealed class ProgressReader(IProgressionStore store, TimeProvider? time = null)
{
	/// <summary>How many players the rankings read.</summary>
	public const int RankingsCount = 500;

	/// <summary>The lowest individual-account SteamID64.</summary>
	public const ulong MinSteamId = 76561197960265729;

	/// <summary>The highest individual-account SteamID64.</summary>
	public const ulong MaxSteamId = 76561202255233023;

	/// <summary>How long an answer is kept.</summary>
	public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(10);

	private const int MaxCachedPlayers = 1024;

	private readonly TimeProvider clock = time ?? TimeProvider.System;
	private readonly Lock gate = new();
	private readonly Dictionary<ulong, (PlayerProgress? Progress, DateTimeOffset Until)> players = [];
	private (IReadOnlyList<RankingEntry> Rankings, DateTimeOffset Until)? rankings;

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
	/// <returns>Up to <see cref="RankingsCount"/> rows.</returns>
	public async Task<IReadOnlyList<RankingEntry>> RankingsAsync()
	{
		DateTimeOffset now = clock.GetUtcNow();
		lock (gate)
		{
			if (rankings is { } cached && now < cached.Until)
			{
				return cached.Rankings;
			}
		}

		IReadOnlyList<RankingEntry> fresh = await store.RankingsAsync(RankingsCount, CancellationToken.None).ConfigureAwait(false);
		lock (gate)
		{
			rankings = (fresh, now + CacheFor);
		}

		return fresh;
	}

	/// <summary>
	/// Gets a player's stored progress. The result is shared between requests, so callers must not change it.
	/// </summary>
	/// <param name="steamId">The 64-bit Steam ID.</param>
	/// <returns>The progress, or <see langword="null"/> for a player the database doesn't have.</returns>
	public async Task<PlayerProgress?> PlayerAsync(ulong steamId)
	{
		DateTimeOffset now = clock.GetUtcNow();
		lock (gate)
		{
			if (players.TryGetValue(steamId, out (PlayerProgress? Progress, DateTimeOffset Until) cached) && now < cached.Until)
			{
				return cached.Progress;
			}
		}

		PlayerProgress? fresh = await store.LoadAsync(steamId, CancellationToken.None).ConfigureAwait(false);
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

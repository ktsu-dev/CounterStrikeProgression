// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Cards;

using System.Collections.Concurrent;

/// <summary>
/// Keeps each player's share card for a few minutes, so a link pasted into a busy channel, where every client
/// fetches the image, draws it once. The PNG is drawn on first use, because the player page needs only the text.
/// </summary>
/// <param name="time">The clock that ages the cards.</param>
internal sealed class CardCache(TimeProvider time)
{
	/// <summary>How long a card is kept.</summary>
	public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

	/// <summary>The most cards kept. Past this, the expired ones go, then the oldest.</summary>
	public const int Capacity = 500;

	private readonly ConcurrentDictionary<ulong, Entry> entries = new();

	/// <summary>Gets how many cards are kept now.</summary>
	public int Count => entries.Count;

	/// <summary>
	/// Gets a player's card, building it when it isn't kept or has expired.
	/// </summary>
	/// <param name="steamId">The player's SteamID64.</param>
	/// <param name="build">Builds the card, or returns <see langword="null"/> for an unknown player, which isn't kept.</param>
	/// <returns>The card, or <see langword="null"/> for an unknown player.</returns>
	public async Task<CachedCard?> GetAsync(ulong steamId, Func<Task<PlayerCard?>> build)
	{
		Ensure.NotNull(build);

		DateTimeOffset now = time.GetUtcNow();
		if (entries.TryGetValue(steamId, out Entry? kept) && (kept.Expires > now))
		{
			return kept.Card;
		}

		PlayerCard? card = await build().ConfigureAwait(false);
		if (card is null)
		{
			entries.TryRemove(steamId, out _);
			return null;
		}

		CachedCard cached = new(card);
		entries[steamId] = new(cached, now + Lifetime);
		if (entries.Count > Capacity)
		{
			Trim(now);
		}

		return cached;
	}

	private void Trim(DateTimeOffset now)
	{
		foreach (KeyValuePair<ulong, Entry> entry in entries.Where(e => e.Value.Expires <= now).ToList())
		{
			entries.TryRemove(entry);
		}

		int extra = entries.Count - Capacity;
		if (extra > 0)
		{
			foreach (KeyValuePair<ulong, Entry> entry in entries.OrderBy(e => e.Value.Expires).Take(extra).ToList())
			{
				entries.TryRemove(entry);
			}
		}
	}

	private sealed record Entry(CachedCard Card, DateTimeOffset Expires);
}

/// <summary>A kept card, with its PNG drawn on first use.</summary>
/// <param name="card">What the card shows.</param>
internal sealed class CachedCard(PlayerCard card)
{
	private readonly Lazy<byte[]> png = new(() => CardRenderer.Render(card), LazyThreadSafetyMode.ExecutionAndPublication);

	/// <summary>Gets what the card shows.</summary>
	public PlayerCard Card { get; } = card;

	/// <summary>Gets the card as a PNG.</summary>
	public byte[] Png => png.Value;
}

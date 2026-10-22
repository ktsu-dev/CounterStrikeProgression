// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Players;

/// <summary>
/// Gives each bot name a stable player id, so a bot keeps one progression row across restarts. The ids sit in a
/// range below the lowest individual-account SteamID64 (76561197960265729), so a bot id can never collide with a human's.
/// </summary>
internal static class BotIds
{
	/// <summary>
	/// The lowest bot id. Bot ids have 17 digits like a SteamID64, so they pass the same id checks, and the whole range
	/// sits far below the lowest individual-account SteamID64.
	/// </summary>
	public const ulong First = 10_000_000_000_000_000;

	/// <summary>The number of ids in the bot range.</summary>
	public const ulong Count = 100_000_000;

	private const ulong OffsetBasis = 14695981039346656037;
	private const ulong Prime = 1099511628211;

	/// <summary>
	/// Gets the id for a bot name. The name is trimmed but keeps its case, and the id is an FNV-1a hash of its
	/// UTF-16 code units, so the same name always gives the same id on the plugin and the site.
	/// </summary>
	/// <param name="name">The bot's name.</param>
	/// <returns>The bot's id, within <see cref="First"/> and <see cref="First"/> + <see cref="Count"/>.</returns>
	/// <exception cref="ArgumentException">The name is blank.</exception>
	public static ulong For(string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);

		ReadOnlySpan<char> trimmed = name.AsSpan().Trim();
		ulong hash = OffsetBasis;
		foreach (char c in trimmed)
		{
			unchecked
			{
				hash ^= c;
				hash *= Prime;
			}
		}

		return First + (hash % Count);
	}

	/// <summary>
	/// Gets whether an id is in the bot range.
	/// </summary>
	/// <param name="id">The player id.</param>
	/// <returns>True for a bot id.</returns>
	public static bool IsBot(ulong id) => id is >= First and < First + Count;
}

/// <summary>
/// Picks the id a player's progression is keyed by.
/// </summary>
internal static class PlayerIds
{
	/// <summary>
	/// Gets the id for a player: the SteamID64 for a human, and the name's bot id for a bot.
	/// </summary>
	/// <param name="isBot">Whether the player is a bot.</param>
	/// <param name="steamId">The player's SteamID64, which a bot doesn't have.</param>
	/// <param name="name">The player's name.</param>
	/// <returns>The id, or 0 for a bot with no usable name.</returns>
	public static ulong Of(bool isBot, ulong steamId, string? name)
	{
		if (!isBot)
		{
			return steamId;
		}

		return string.IsNullOrWhiteSpace(name) ? 0 : BotIds.For(name);
	}
}

/// <summary>
/// Decides when a session may start, so bots are only tracked while a human is there to play with them.
/// </summary>
internal static class BotTracking
{
	/// <summary>
	/// Gets whether a player may start a session.
	/// </summary>
	/// <param name="id">The player's id, from <see cref="PlayerIds.Of"/>.</param>
	/// <param name="humansOnline">Whether at least one human is connected.</param>
	/// <param name="activeIds">The ids with a session already.</param>
	/// <returns>
	/// True for a human id, and for a bot id only when a human is online and no session has that id. False for 0.
	/// </returns>
	public static bool MayStart(ulong id, bool humansOnline, IEnumerable<ulong> activeIds)
	{
		Ensure.NotNull(activeIds);

		if (id == 0)
		{
			return false;
		}

		if (!BotIds.IsBot(id))
		{
			return true;
		}

		return humansOnline && !activeIds.Contains(id);
	}
}

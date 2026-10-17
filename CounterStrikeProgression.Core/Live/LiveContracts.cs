// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Live;

using ktsu.CounterStrikeProgression.Core.Storage;

/// <summary>
/// The kinds of moment the live feed shows.
/// </summary>
internal enum FeedKind
{
	SkillShot = 0,
	RankUp,
	WeaponLevelUp,
	TierCompleted,
}

/// <summary>One line of the live feed.</summary>
/// <param name="At">When it happened, in UTC.</param>
/// <param name="SteamId">The 64-bit Steam ID of the player who earned it.</param>
/// <param name="PlayerName">The player's name at the time.</param>
/// <param name="Kind">What kind of moment it was.</param>
/// <param name="Title">The text to show, such as a skill shot name or "Rank up: Gold Nova I".</param>
/// <param name="Weapon">The weapon's display name, if one was involved.</param>
/// <param name="Xp">The rank XP the moment paid.</param>
internal sealed record FeedEntry(DateTime At, ulong SteamId, string PlayerName, FeedKind Kind, string Title, string? Weapon, long Xp);

/// <summary>One player on the server right now.</summary>
/// <param name="Slot">The player's slot.</param>
/// <param name="SteamId">The 64-bit Steam ID.</param>
/// <param name="Name">The player's name.</param>
/// <param name="IsBot">Whether the player is a bot.</param>
/// <param name="Rank">The rank index.</param>
/// <param name="Kills">Kills this map.</param>
/// <param name="Deaths">Deaths this map.</param>
/// <param name="MapXp">The rank XP earned this map.</param>
internal sealed record LivePlayer(int Slot, ulong SteamId, string Name, bool IsBot, int Rank, int Kills, int Deaths, double MapXp);

/// <summary>The state of the server at one moment.</summary>
/// <param name="SentAt">When the snapshot was taken, in UTC.</param>
/// <param name="Map">The current map.</param>
/// <param name="GameMode">The game mode's display name.</param>
/// <param name="MaxPlayers">The player limit.</param>
/// <param name="Players">The players on the server.</param>
internal sealed record ServerSnapshot(DateTime SentAt, string Map, string GameMode, int MaxPlayers, IReadOnlyList<LivePlayer> Players);

/// <summary>Everything the plugin sends to the site in one request.</summary>
/// <param name="Snapshot">The server state.</param>
/// <param name="Feed">The feed entries since the last batch.</param>
/// <param name="Progress">The progress rows that changed since the last batch.</param>
/// <param name="Settings">The plugin's settings, so the site can show the same XP values.</param>
internal sealed record LiveBatch(ServerSnapshot Snapshot, IReadOnlyList<FeedEntry> Feed, IReadOnlyList<ProgressSnapshot> Progress, ProgressionSettings Settings);

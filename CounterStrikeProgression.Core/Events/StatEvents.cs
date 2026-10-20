// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Events;

using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// A reload, captured as it starts.
/// </summary>
/// <param name="Weapon">The weapon being reloaded.</param>
/// <param name="RoundsLeft">Rounds in the magazine when the reload began.</param>
/// <param name="MagazineSize">The weapon's full magazine.</param>
/// <param name="LoadsShellByShell">Whether the weapon reloads one shell at a time, which never counts as efficient.</param>
internal sealed record ReloadEvent(WeaponInfo Weapon, int RoundsLeft, int MagazineSize, bool LoadsShellByShell);

/// <summary>
/// How a weapon performed over one life, reported when the life ends.
/// </summary>
/// <param name="Weapon">The weapon.</param>
/// <param name="Shots">Shots fired.</param>
/// <param name="Hits">Shots that damaged an enemy.</param>
/// <param name="HeadshotHits">Hits that struck the head.</param>
/// <param name="MostlyBots">Whether most of the hits landed on bots, which reduces the XP.</param>
internal sealed record WeaponLifeStats(WeaponInfo Weapon, int Shots, int Hits, int HeadshotHits, bool MostlyBots);

/// <summary>
/// An assist on a kill.
/// </summary>
/// <param name="Weapon">The weapon the assister was holding, or the flashbang for a flash assist.</param>
/// <param name="ByFlash">Whether the assist came from blinding the victim.</param>
/// <param name="VictimIsBot">Whether the victim was a bot.</param>
internal sealed record AssistEvent(WeaponInfo Weapon, bool ByFlash, bool VictimIsBot);

/// <summary>
/// An enemy blinded by the player's flashbang.
/// </summary>
/// <param name="Weapon">The flashbang.</param>
/// <param name="BlindSeconds">How long the enemy was blinded.</param>
/// <param name="VictimIsBot">Whether the blinded enemy was a bot.</param>
internal sealed record FlashEvent(WeaponInfo Weapon, double BlindSeconds, bool VictimIsBot);

/// <summary>
/// A round the player's team won with them as its last player alive.
/// </summary>
/// <param name="EnemiesWhenAlone">How many enemies were alive when the player became the last one standing.</param>
/// <param name="LastWeapon">The weapon of the player's last kill that round, which takes the ladder progress, if any.</param>
/// <param name="VsBots">Whether every enemy alive at that moment was a bot.</param>
internal sealed record ClutchEvent(int EnemiesWhenAlone, WeaponInfo? LastWeapon, bool VsBots);

/// <summary>
/// What a player did over one whole life, across every weapon, for the server records.
/// </summary>
/// <param name="Kills">Kills that life, counting kills on bots.</param>
/// <param name="BotKills">Kills on bots that life.</param>
/// <param name="Shots">Gun shots fired.</param>
/// <param name="Hits">Gun shots that damaged an enemy, counting each weapon once a tick.</param>
/// <param name="BotHits">Hits that landed on bots.</param>
internal sealed record LifeTotals(int Kills, int BotKills, int Shots, int Hits, int BotHits);

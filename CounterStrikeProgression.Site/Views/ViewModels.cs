// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Views;

using ktsu.CounterStrikeProgression.Core.Live;

/// <summary>One challenge on a weapon's ladder.</summary>
/// <param name="Id">The challenge id.</param>
/// <param name="Name">The name players see.</param>
/// <param name="Count">How many times the challenge has been done.</param>
/// <param name="Tier">The number of tiers finished.</param>
/// <param name="TierCount">The number of tiers the challenge has.</param>
/// <param name="NextTarget">The count needed for the next tier, or <see langword="null"/> when every tier is done.</param>
internal sealed record ChallengeView(string Id, string Name, long Count, int Tier, int TierCount, int? NextTarget);

/// <summary>One weapon on a player's page.</summary>
/// <param name="Key">The weapon key.</param>
/// <param name="DisplayName">The name players see.</param>
/// <param name="Level">The weapon level.</param>
/// <param name="MaxLevel">The highest level a weapon can reach.</param>
/// <param name="LevelProgress">How far the weapon is to the next level, from 0 to 1.</param>
/// <param name="Kills">Kills with the weapon.</param>
/// <param name="HeadshotPercent">Headshot kills as a percentage of kills, from 0 to 100.</param>
/// <param name="Accuracy">Hits as a percentage of shots, from 0 to 100.</param>
/// <param name="Challenges">The weapon's challenges.</param>
internal sealed record WeaponView(
	string Key,
	string DisplayName,
	int Level,
	int MaxLevel,
	double LevelProgress,
	long Kills,
	double HeadshotPercent,
	double Accuracy,
	IReadOnlyList<ChallengeView> Challenges);

/// <summary>A player's page.</summary>
/// <param name="SteamId">The 64-bit Steam ID as a decimal string.</param>
/// <param name="Name">The player's last known name.</param>
/// <param name="Rank">The rank index.</param>
/// <param name="RankName">The rank name.</param>
/// <param name="NextRankName">The next rank's name, or <see langword="null"/> at the top rank.</param>
/// <param name="RankXp">The player's rank XP.</param>
/// <param name="XpToNextRank">The XP left to the next rank, or 0 at the top rank.</param>
/// <param name="RankProgress">How far the player is to the next rank, from 0 to 1.</param>
/// <param name="FirstSeen">When the player was first seen, in UTC.</param>
/// <param name="LastSeen">When the player was last seen, in UTC.</param>
/// <param name="Online">Whether the player is on the server now.</param>
/// <param name="Weapons">The player's weapons, most XP first.</param>
/// <param name="Suggestions">The challenges to try next, closest to a tier first.</param>
/// <param name="Feed">The player's feed, newest first.</param>
internal sealed record PlayerView(
	string SteamId,
	string Name,
	int Rank,
	string RankName,
	string? NextRankName,
	long RankXp,
	long XpToNextRank,
	double RankProgress,
	DateTime FirstSeen,
	DateTime LastSeen,
	bool Online,
	IReadOnlyList<WeaponView> Weapons,
	IReadOnlyList<SuggestionView> Suggestions,
	IReadOnlyList<FeedItem> Feed);

/// <summary>One feed line as the pages show it.</summary>
/// <param name="At">When it happened, in UTC.</param>
/// <param name="SteamId">The 64-bit Steam ID of the player who earned it.</param>
/// <param name="PlayerName">The player's name at the time.</param>
/// <param name="Kind">What kind of moment it was.</param>
/// <param name="Title">The text to show, such as a skill shot name.</param>
/// <param name="Weapon">The weapon's display name, if one was involved.</param>
/// <param name="Xp">The rank XP the moment paid.</param>
/// <param name="Caption">How the moment was earned, such as a skill shot's description, or <see langword="null"/> when the title says it all.</param>
internal sealed record FeedItem(DateTime At, ulong SteamId, string PlayerName, FeedKind Kind, string Title, string? Weapon, long Xp, string? Caption);

/// <summary>One suggested next challenge on a player's page.</summary>
/// <param name="WeaponKey">The weapon key.</param>
/// <param name="WeaponName">The weapon's display name.</param>
/// <param name="ChallengeId">The challenge id.</param>
/// <param name="ChallengeName">The name players see.</param>
/// <param name="Description">For a skill challenge, what the player has to do, otherwise <see langword="null"/>.</param>
/// <param name="NextTier">The tier the suggestion works toward, from 1.</param>
/// <param name="Count">How many times the challenge has been done.</param>
/// <param name="Target">The count that finishes the next tier.</param>
/// <param name="Progress">How far the player is from the last tier to the next, from 0 to 1.</param>
/// <param name="Xp">The rank XP the next tier pays, which is the tier XP times the tier number.</param>
/// <param name="IsNew">Whether the player hasn't started the challenge.</param>
internal sealed record SuggestionView(
	string WeaponKey,
	string WeaponName,
	string ChallengeId,
	string ChallengeName,
	string? Description,
	int NextTier,
	long Count,
	int Target,
	double Progress,
	int Xp,
	bool IsNew);

/// <summary>One row of the server ranks page.</summary>
/// <param name="Position">The place on the ranks, from 1.</param>
/// <param name="SteamId">The 64-bit Steam ID as a decimal string.</param>
/// <param name="Name">The player's last known name.</param>
/// <param name="Rank">The rank index.</param>
/// <param name="RankName">The rank name.</param>
/// <param name="RankXp">The player's rank XP.</param>
/// <param name="LastSeen">When the player was last seen, in UTC.</param>
/// <param name="Online">Whether the player is on the server now.</param>
internal sealed record RankRow(int Position, string SteamId, string Name, int Rank, string RankName, long RankXp, DateTime LastSeen, bool Online);

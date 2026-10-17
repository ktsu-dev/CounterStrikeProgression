// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Recap;

/// <summary>What one player earned on one map, from joining or the map starting until it ended or they left.</summary>
/// <param name="SteamId">The player's 64-bit Steam ID.</param>
/// <param name="PlayerName">The player's name at the end.</param>
/// <param name="Map">The map played.</param>
/// <param name="StartedAt">When the session started, in UTC.</param>
/// <param name="EndedAt">When the session ended, in UTC.</param>
/// <param name="Xp">The rank XP earned, rounded to whole points.</param>
/// <param name="Kills">Kills on the scoreboard.</param>
/// <param name="Deaths">Deaths on the scoreboard.</param>
/// <param name="SkillShots">The skill shots landed.</param>
/// <param name="Levels">The weapon levels gained.</param>
/// <param name="Tiers">The ladder tiers finished.</param>
/// <param name="RankUps">The ranks gained.</param>
/// <param name="Feats">The feats earned.</param>
/// <param name="Best">The best moment, such as "Wallbang with the AK-47", or <see langword="null"/> when nothing stood out.</param>
internal sealed record MapRecap(
	ulong SteamId,
	string PlayerName,
	string Map,
	DateTime StartedAt,
	DateTime EndedAt,
	long Xp,
	int Kills,
	int Deaths,
	int SkillShots,
	int Levels,
	int Tiers,
	int RankUps,
	int Feats,
	string? Best);

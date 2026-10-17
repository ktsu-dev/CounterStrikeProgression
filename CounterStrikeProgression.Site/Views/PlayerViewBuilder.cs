// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Views;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Challenges;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// Turns a player's merged progress into the model the player page shows. Ranks, levels, and tiers come
/// from the plugin's own settings, so the page agrees with the in-game panel.
/// </summary>
internal static class PlayerViewBuilder
{
	/// <summary>
	/// Builds the player page model.
	/// </summary>
	/// <param name="merged">The player's stored and live progress, merged.</param>
	/// <param name="settings">The plugin's settings.</param>
	/// <param name="online">Whether the player is on the server now.</param>
	/// <returns>The page model.</returns>
	public static PlayerView Build(PlayerProgress merged, ProgressionSettings settings, bool online)
	{
		Ensure.NotNull(merged);
		Ensure.NotNull(settings);

		RankLadder ladder = new(settings.FirstRankXp, settings.RankGrowth);
		int ladderRank = ladder.RankFor(merged.RankXp);
		int rank = Math.Clamp(Math.Max(ladderRank, merged.Rank), 0, RankLadder.TopRank);

		bool atTop = rank >= RankLadder.TopRank;
		long xpToNext = atTop ? 0 : Math.Max(0, ladder.ThresholdFor(rank + 1) - merged.RankXp);
		double progress = atTop ? 1 : (rank == ladderRank ? ladder.ProgressToNext(merged.RankXp) : 0);

		WeaponLevelCurve curve = new(settings.WeaponLevelBaseXp, settings.WeaponLevelStepXp, settings.WeaponMaxLevel);
		ChallengeCatalog catalog = new(settings);
		List<WeaponView> weapons =
		[
			.. merged.Weapons.Values
				.OrderByDescending(w => w.Xp)
				.ThenBy(w => w.Weapon.WeakString, StringComparer.Ordinal)
				.Select(w => BuildWeapon(w, curve, catalog)),
		];

		return new(
			merged.SteamId.ToString(CultureInfo.InvariantCulture),
			merged.Name,
			rank,
			RankLadder.Names[rank],
			atTop ? null : RankLadder.Names[rank + 1],
			merged.RankXp,
			xpToNext,
			progress,
			merged.FirstSeen,
			merged.LastSeen,
			online,
			weapons);
	}

	private static WeaponView BuildWeapon(WeaponProgress weapon, WeaponLevelCurve curve, ChallengeCatalog catalog)
	{
		WeaponInfo? info = WeaponCatalog.Find(weapon.Weapon);
		int level = Math.Min(curve.MaxLevel, Math.Max(curve.LevelFor(weapon.Xp), weapon.Level));
		double levelProgress = level >= curve.MaxLevel ? 1 : curve.ProgressToNext(weapon.Xp);

		IReadOnlyList<ChallengeView> challenges = info is null
			? []
			: [.. catalog.For(info).Select(d => BuildChallenge(d, weapon))];

		return new(
			weapon.Weapon.WeakString,
			info?.DisplayName ?? weapon.Weapon.WeakString,
			level,
			curve.MaxLevel,
			levelProgress,
			weapon.Kills,
			Percent(weapon.Headshots, weapon.Kills),
			Percent(weapon.Hits, weapon.Shots),
			challenges);
	}

	private static ChallengeView BuildChallenge(ChallengeDefinition definition, WeaponProgress weapon)
	{
		weapon.Challenges.TryGetValue(definition.Id, out ChallengeProgress? progress);
		long count = progress?.Count ?? 0;
		int tier = Math.Min(definition.Tiers.Count, Math.Max(definition.TierFor(count), progress?.TierReached ?? 0));
		return new(definition.Id, definition.Name, count, tier, definition.Tiers.Count, definition.NextTarget(tier));
	}

	private static double Percent(long part, long whole) =>
		whole <= 0 ? 0 : Math.Round(part * 100.0 / whole, 1, MidpointRounding.AwayFromZero);
}

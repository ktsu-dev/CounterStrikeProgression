// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Views;

using System.Globalization;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Challenges;
using ktsu.CounterStrikeProgression.Core.Contracts;
using ktsu.CounterStrikeProgression.Core.Feats;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Recap;
using ktsu.CounterStrikeProgression.Core.Rotation;
using ktsu.CounterStrikeProgression.Core.SkillShots;
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
	/// <param name="feed">The player's feed, newest first. Omitted, the page shows none.</param>
	/// <param name="lastSession">The player's last map recap. Omitted, the page shows none.</param>
	/// <param name="now">The moment that decides which contracts are running, in UTC, or the current time when omitted.</param>
	/// <returns>The page model.</returns>
	public static PlayerView Build(PlayerProgress merged, ProgressionSettings settings, bool online, IReadOnlyList<FeedEntry>? feed = null, MapRecap? lastSession = null, DateTime? now = null)
	{
		Ensure.NotNull(merged);
		Ensure.NotNull(settings);

		RankLadder ladder = new(settings.FirstRankXp, settings.RankGrowth);
		int ladderRank = ladder.RankFor(merged.RankXp);
		int rank = Math.Clamp(Math.Max(ladderRank, merged.Rank), 0, RankLadder.TopRank);

		bool atTop = rank >= RankLadder.TopRank;
		long xpToNext = atTop ? 0 : Math.Max(0, ladder.ThresholdFor(rank + 1) - merged.RankXp);
		double progress = atTop ? 1 : (rank == ladderRank ? Fraction(ladder.ProgressToNext(merged.RankXp)) : 0);

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
			weapons,
			ChallengeSuggester.Suggest(merged, settings, catalog),
			[.. new FeatCatalog(settings).Measure(merged).Select(m => new FeatView(m.Feat.Id, m.Feat.Name, m.Feat.Description, m.Progress.IsEarned, Math.Min(m.Progress.Current, m.Progress.Target), m.Progress.Target))],
			FeedCaptions.For(feed ?? [], settings),
			lastSession is null ? null : BuildSession(lastSession),
			Contracts(merged, settings, now ?? DateTime.UtcNow));
	}

	/// <summary>
	/// Gets the contracts running at a moment, with the player's progress on each.
	/// </summary>
	/// <param name="merged">The player's progress.</param>
	/// <param name="settings">The plugin's settings, which decide how many contracts run and what they pay.</param>
	/// <param name="now">The moment, in UTC.</param>
	/// <returns>The contracts, today's first.</returns>
	internal static IReadOnlyList<ContractView> Contracts(PlayerProgress merged, ProgressionSettings settings, DateTime now) =>
	[
		.. ContractCatalog.Active(DailyRotation.Day(now), settings).Select(c =>
		{
			ContractProgress? progress = merged.Contracts.GetValueOrDefault((c.PeriodKey, c.Definition.Id));
			bool completed = progress?.Completed ?? false;
			long count = completed ? c.Definition.Target : Math.Min(progress?.Count ?? 0, c.Definition.Target);
			return new ContractView(c.Definition.Id, c.Definition.Description, c.Definition.Period, count, c.Definition.Target, completed, c.Xp, c.EndsAt);
		}),
	];

	private static SessionView BuildSession(MapRecap recap) =>
		new(recap.Map, recap.StartedAt, recap.EndedAt, recap.Xp, recap.Kills, recap.Deaths, recap.SkillShots, recap.Levels, recap.Tiers, recap.RankUps, recap.Feats, recap.Best);

	private static WeaponView BuildWeapon(WeaponProgress weapon, WeaponLevelCurve curve, ChallengeCatalog catalog)
	{
		WeaponInfo? info = WeaponCatalog.Find(weapon.Weapon);
		int curveLevel = curve.LevelFor(weapon.Xp);
		int level = Math.Min(curve.MaxLevel, Math.Max(curveLevel, weapon.Level));
		double levelProgress = level >= curve.MaxLevel ? 1 : (level == curveLevel ? Fraction(curve.ProgressToNext(weapon.Xp)) : 0);

		IReadOnlyList<ChallengeView> challenges = info is null
			? []
			: [.. catalog.For(info).Select(d => BuildChallenge(d, weapon))];

		return new(
			weapon.Weapon.WeakString,
			info?.DisplayName ?? weapon.Weapon.WeakString,
			level,
			curve.MaxLevel,
			levelProgress,
			curve.StarsFor(weapon.Xp),
			Fraction(curve.ProgressToNextStar(weapon.Xp)),
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
		bool isSkill = definition.Kind == ChallengeKind.Skill;
		return new(
			definition.Id,
			definition.Name,
			count,
			tier,
			definition.Tiers.Count,
			definition.NextTarget(tier),
			definition.Kind,
			isSkill ? definition.Rarity : null,
			Describe(definition),
			TierProgress(definition, tier, count));
	}

	/// <summary>
	/// Gets what a skill challenge's skill shot asks for.
	/// </summary>
	/// <param name="definition">The challenge.</param>
	/// <returns>The skill shot's description, or <see langword="null"/> for a volume challenge.</returns>
	internal static string? Describe(ChallengeDefinition definition) =>
		definition.Kind == ChallengeKind.Skill
			? SkillShotCatalog.All.FirstOrDefault(s => string.Equals(s.Id.WeakString, definition.Id, StringComparison.Ordinal))?.Description
			: null;

	/// <summary>
	/// Gets how far a count is from the last finished tier to the next one.
	/// </summary>
	/// <param name="definition">The challenge.</param>
	/// <param name="tier">The number of tiers finished.</param>
	/// <param name="count">The challenge's count.</param>
	/// <returns>A fraction from 0 to 1, or 1 when every tier is done.</returns>
	internal static double TierProgress(ChallengeDefinition definition, int tier, long count)
	{
		if (definition.NextTarget(tier) is not { } target)
		{
			return 1;
		}

		int previous = tier > 0 ? definition.Tiers[tier - 1] : 0;
		return target > previous ? Math.Clamp((count - previous) / (double)(target - previous), 0, 1) : 0;
	}

	private static double Percent(long part, long whole) =>
		whole <= 0 ? 0 : Math.Clamp(Math.Round(part * 100.0 / whole, 1, MidpointRounding.AwayFromZero), 0, 100);

	private static double Fraction(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}

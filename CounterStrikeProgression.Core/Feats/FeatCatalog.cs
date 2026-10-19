// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Feats;

using ktsu.CounterStrikeProgression.Core.Challenges;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.SkillShots;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>How far a player is toward a feat.</summary>
/// <param name="Current">The player's best value so far.</param>
/// <param name="Target">The value that earns the feat.</param>
internal readonly record struct FeatProgress(long Current, long Target)
{
	/// <summary>Gets a value indicating whether the feat is earned.</summary>
	public bool IsEarned => (Target > 0) && (Current >= Target);
}

/// <summary>One really difficult feat.</summary>
/// <param name="Id">The stable id.</param>
/// <param name="Name">The name players see.</param>
/// <param name="Description">What the player has to do.</param>
internal sealed record FeatDefinition(string Id, string Name, string Description);

/// <summary>
/// The feats, and how far a player is toward each. Feats are worked out from the stored progress alone, so
/// nothing extra is saved and the plugin and the site always agree.
/// </summary>
/// <param name="settings">The settings that set the ladders, curves, and tiers.</param>
internal sealed class FeatCatalog(ProgressionSettings settings)
{
	/// <summary>The XP that earns Millionaire.</summary>
	public const long MillionaireXp = 1_000_000;

	/// <summary>The level every gun needs for Arsenal.</summary>
	public const int ArsenalLevel = 10;

	/// <summary>The weapons that need a skill shot tier for Polymath.</summary>
	public const int PolymathWeapons = 20;

	/// <summary>The play streak, in days, that earns Regular.</summary>
	public const int RegularDays = 7;

	/// <summary>The play streak, in days, that earns Devoted.</summary>
	public const int DevotedDays = 30;

	/// <summary>Gets every feat, in the order the player page lists them.</summary>
	public static IReadOnlyList<FeatDefinition> All { get; } =
	[
		new("global_elite", "Global Elite", "Reach Global Elite · V"),
		new("millionaire", "Millionaire", "Earn 1,000,000 XP"),
		new("weapon_master", "Weapon master", "Take a weapon to its top level"),
		new("epic_ladder", "Epic ladder", "Finish all five tiers of an epic skill shot"),
		new("full_ladder", "Full ladder", "Finish every challenge on one weapon"),
		new("collector", "Collector", "Earn every skill shot at least once"),
		new("chicken_overlord", "Chicken overlord", "Finish all five Chickens tiers with one weapon"),
		new("chicken_connoisseur", "Chicken connoisseur", "Earn every chicken skill shot"),
		new("arsenal", "Arsenal", "Take every gun to level 10"),
		new("polymath", "Polymath", "Finish a skill shot tier on 20 weapons"),
		new("regular", "Regular", "Earn XP on 7 days in a row"),
		new("devoted", "Devoted", "Earn XP on 30 days in a row"),
	];

	private static readonly WeaponKey[] Guns = [.. WeaponCatalog.All.Where(w => w.IsGun).Select(w => w.Key)];

	private static readonly string[] ChickenSkillShotIds =
		[.. SkillShotCatalog.For(SkillTrigger.Chicken).Select(s => s.Id.WeakString)];

	private readonly RankLadder ranks = new(settings.FirstRankXp, settings.RankGrowth);
	private readonly WeaponLevelCurve levels = new(settings.WeaponLevelBaseXp, settings.WeaponLevelStepXp, settings.WeaponMaxLevel);
	private readonly ChallengeCatalog challenges = new(settings);

	/// <summary>
	/// Measures a player against every feat.
	/// </summary>
	/// <param name="player">The player's progress.</param>
	/// <returns>Each feat with the player's progress toward it, in <see cref="All"/> order.</returns>
	public IReadOnlyList<(FeatDefinition Feat, FeatProgress Progress)> Measure(PlayerProgress player)
	{
		Ensure.NotNull(player);

		List<(WeaponInfo Info, WeaponProgress Progress)> weapons = [];
		foreach (WeaponProgress weapon in player.Weapons.Values)
		{
			if (WeaponCatalog.Find(weapon.Weapon) is { } info)
			{
				weapons.Add((info, weapon));
			}
		}

		HashSet<string> skillShotsEarned = new(StringComparer.Ordinal);
		int bestEpicTier = 0;
		int bestChickenTier = 0;
		(long Done, long Total) bestLadder = (0, 0);
		int weaponsWithSkillTier = 0;
		foreach ((WeaponInfo info, WeaponProgress progress) in weapons)
		{
			IReadOnlyList<ChallengeDefinition> ladder = challenges.For(info);
			int finished = 0;
			bool skillTier = false;
			foreach (ChallengeDefinition definition in ladder)
			{
				progress.Challenges.TryGetValue(definition.Id, out ChallengeProgress? challenge);
				long count = challenge?.Count ?? 0;
				int tier = Math.Max(definition.TierFor(count), challenge?.TierReached ?? 0);
				if (tier >= definition.Tiers.Count)
				{
					finished++;
				}

				if (definition.Kind == ChallengeKind.Skill)
				{
					if (count > 0)
					{
						skillShotsEarned.Add(definition.Id);
					}

					skillTier |= tier > 0;

					if (definition.Rarity == Rarity.Epic)
					{
						bestEpicTier = Math.Max(bestEpicTier, tier);
					}
				}
				else if (string.Equals(definition.Id, ChallengeDefinition.ChickensId, StringComparison.Ordinal))
				{
					bestChickenTier = Math.Max(bestChickenTier, tier);
				}
			}

			if (skillTier)
			{
				weaponsWithSkillTier++;
			}

			if ((ladder.Count > 0) && ((bestLadder.Total == 0) || ((double)finished / ladder.Count > (double)bestLadder.Done / bestLadder.Total)))
			{
				bestLadder = (finished, ladder.Count);
			}
		}

		if (bestLadder.Total == 0)
		{
			bestLadder = (0, challenges.For(WeaponCatalog.All[0]).Count);
		}

		int rank = Math.Max(player.Rank, ranks.RankFor(player.RankXp));
		int bestLevel = weapons.Count == 0 ? 0 : weapons.Max(w => Math.Max(w.Progress.Level, levels.LevelFor(w.Progress.Xp)));
		int chickenShots = ChickenSkillShotIds.Count(skillShotsEarned.Contains);
		int gunsAtArsenalLevel = Guns.Count(key => player.Weapons.TryGetValue(key, out WeaponProgress? weapon)
			&& (Math.Max(weapon.Level, levels.LevelFor(weapon.Xp)) >= ArsenalLevel));

		FeatProgress[] measured =
		[
			new(rank, RankLadder.TopRank),
			new(player.RankXp, MillionaireXp),
			new(bestLevel, settings.WeaponMaxLevel),
			new(bestEpicTier, settings.EpicSkillTiers.Count),
			new(bestLadder.Done, bestLadder.Total),
			new(skillShotsEarned.Count, SkillShotCatalog.All.Count),
			new(bestChickenTier, settings.ChickenTiers.Count),
			new(chickenShots, ChickenSkillShotIds.Length),
			new(gunsAtArsenalLevel, Guns.Length),
			new(weaponsWithSkillTier, PolymathWeapons),
			new(player.Streak.Best, RegularDays),
			new(player.Streak.Best, DevotedDays),
		];

		return [.. All.Zip(measured)];
	}

	/// <summary>
	/// Gets the feats a player has earned.
	/// </summary>
	/// <param name="player">The player's progress.</param>
	/// <returns>The earned feats, in <see cref="All"/> order.</returns>
	public IEnumerable<FeatDefinition> Earned(PlayerProgress player) =>
		Measure(player).Where(m => m.Progress.IsEarned).Select(m => m.Feat);

	/// <summary>
	/// Finds a feat by name, for feed lines, which carry only the name.
	/// </summary>
	/// <param name="name">The feat name.</param>
	/// <returns>The feat, or <see langword="null"/> when no feat has that name.</returns>
	public static FeatDefinition? FindByName(string name) =>
		All.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal));
}

// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Progression;

using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// The XP a weapon needs for each level. Reaching the next level costs a base amount plus a step for
/// every level already earned, up to a maximum level. Past the maximum level, weapon XP keeps counting toward
/// mastery stars, one for each maximum level's worth of XP.
/// </summary>
/// <param name="baseXp">XP needed to go from level 1 to level 2.</param>
/// <param name="stepXp">Extra XP each later level costs.</param>
/// <param name="maxLevel">The highest level a weapon can reach.</param>
internal sealed class WeaponLevelCurve(long baseXp, long stepXp, int maxLevel)
{
	/// <summary>
	/// Gets the highest level a weapon can reach.
	/// </summary>
	public int MaxLevel { get; } = Math.Max(1, maxLevel);

	/// <summary>
	/// Gets the XP needed to go from a level to the next.
	/// </summary>
	/// <param name="level">The current level, starting at 1.</param>
	/// <returns>The XP for that one level.</returns>
	public long CostOf(int level) => baseXp + (stepXp * (Math.Max(1, level) - 1));

	/// <summary>
	/// Gets the level a total weapon XP earns.
	/// </summary>
	/// <param name="xp">Total XP earned with the weapon.</param>
	/// <returns>The level, from 1 to <see cref="MaxLevel"/>.</returns>
	public int LevelFor(long xp)
	{
		int level = 1;
		long remaining = xp;
		while (level < MaxLevel && remaining >= CostOf(level))
		{
			remaining -= CostOf(level);
			level++;
		}

		return level;
	}

	/// <summary>
	/// Gets the total XP needed to reach a level.
	/// </summary>
	/// <param name="level">The level, starting at 1.</param>
	/// <returns>The cumulative XP for that level.</returns>
	public long ThresholdFor(int level)
	{
		long total = 0;
		for (int l = 1; l < Math.Min(level, MaxLevel); l++)
		{
			total += CostOf(l);
		}

		return total;
	}

	/// <summary>
	/// Gets how far a weapon is from its current level to the next, from 0 to 1.
	/// </summary>
	/// <param name="xp">Total XP earned with the weapon.</param>
	/// <returns>The fraction of the way to the next level, or 1 at the maximum level.</returns>
	public double ProgressToNext(long xp)
	{
		int level = LevelFor(xp);
		return level >= MaxLevel ? 1 : (double)(xp - ThresholdFor(level)) / CostOf(level);
	}

	/// <summary>
	/// Gets the total XP needed to reach the maximum level, where mastery stars start counting.
	/// </summary>
	public long MaxLevelXp => ThresholdFor(MaxLevel);

	/// <summary>
	/// Gets the XP each mastery star costs: as much as taking a weapon from level 1 to the maximum level.
	/// </summary>
	public long StarXp => Math.Max(1, MaxLevelXp > 0 ? MaxLevelXp : CostOf(1));

	/// <summary>
	/// Gets the mastery stars a total weapon XP earns.
	/// </summary>
	/// <param name="xp">Total XP earned with the weapon.</param>
	/// <returns>The number of stars, 0 until a full star's worth of XP is earned past the maximum level.</returns>
	public int StarsFor(long xp) => xp <= MaxLevelXp ? 0 : (int)Math.Min(int.MaxValue, (xp - MaxLevelXp) / StarXp);

	/// <summary>
	/// Gets the XP earned toward the next mastery star.
	/// </summary>
	/// <param name="xp">Total XP earned with the weapon.</param>
	/// <returns>The XP past the last star, or 0 below the maximum level.</returns>
	public long XpTowardStar(long xp) => xp <= MaxLevelXp ? 0 : (xp - MaxLevelXp) % StarXp;

	/// <summary>
	/// Gets how far a weapon is toward its next mastery star, from 0 to 1.
	/// </summary>
	/// <param name="xp">Total XP earned with the weapon.</param>
	/// <returns>The fraction of the way to the next star, or 0 below the maximum level.</returns>
	public double ProgressToNextStar(long xp) => (double)XpTowardStar(xp) / StarXp;

	/// <summary>
	/// Describes a weapon's mastery stars in words, such as <c>1 star</c> or <c>3 stars</c>.
	/// </summary>
	/// <param name="stars">The number of stars.</param>
	/// <returns>The text.</returns>
	public static string StarsText(int stars) => stars == 1 ? "1 star" : $"{stars} stars";

	/// <summary>
	/// Gets the title of a new mastery star, such as <c>AK-47 mastery star 2</c>.
	/// </summary>
	/// <param name="weapon">The weapon, if known.</param>
	/// <param name="star">The star reached.</param>
	/// <returns>The title.</returns>
	public static string StarTitle(WeaponInfo? weapon, int star) => $"{weapon?.DisplayName} mastery star {star}".TrimStart();
}

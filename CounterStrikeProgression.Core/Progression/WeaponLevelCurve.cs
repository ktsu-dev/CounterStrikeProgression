// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Progression;

/// <summary>
/// The XP a weapon needs for each level. Reaching the next level costs a base amount plus a step for
/// every level already earned, up to a maximum level.
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
}

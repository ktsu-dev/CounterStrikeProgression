// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Challenges;

using ktsu.CounterStrikeProgression.Core.SkillShots;

/// <summary>
/// Whether a challenge rewards how much you play a weapon or how well.
/// </summary>
internal enum ChallengeKind
{
	Volume = 0,
	Skill,
}

/// <summary>
/// One rung of a weapon's ladder, such as Kills or Mid-air, with five tiers that get harder.
/// </summary>
/// <param name="Id">The stable id stored in the database. For skill challenges it's the skill shot id.</param>
/// <param name="Name">The name players see.</param>
/// <param name="Kind">Volume or skill.</param>
/// <param name="Rarity">For skill challenges, the skill shot's rarity.</param>
/// <param name="Tiers">The count needed for each tier, lowest first.</param>
internal sealed record ChallengeDefinition(string Id, string Name, ChallengeKind Kind, Rarity Rarity, IReadOnlyList<int> Tiers)
{
	/// <summary>Gets the id of every weapon's Kills challenge.</summary>
	public const string KillsId = "kills";

	/// <summary>Gets the id of every weapon's Headshots challenge.</summary>
	public const string HeadshotsId = "headshots";

	/// <summary>Gets the id of every weapon's Hits challenge.</summary>
	public const string HitsId = "hits";

	/// <summary>
	/// Gets the tier a count has reached, where 0 means none yet.
	/// </summary>
	/// <param name="count">The challenge count.</param>
	/// <returns>The number of tiers finished.</returns>
	public int TierFor(long count)
	{
		int tier = 0;
		while ((tier < Tiers.Count) && (count >= Tiers[tier]))
		{
			tier++;
		}

		return tier;
	}

	/// <summary>
	/// Gets the count needed for the next tier, or <see langword="null"/> when every tier is done.
	/// </summary>
	/// <param name="tierReached">The number of tiers finished.</param>
	/// <returns>The next tier's target.</returns>
	public int? NextTarget(int tierReached) => tierReached < Tiers.Count ? Tiers[tierReached] : null;
}

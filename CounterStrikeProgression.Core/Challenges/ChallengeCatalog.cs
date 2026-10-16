// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Challenges;

using ktsu.CounterStrikeProgression.Core.SkillShots;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// Builds each weapon's ladder: the three volume challenges every weapon has, followed by one skill challenge
/// for every skill shot that weapon can earn.
/// </summary>
/// <param name="settings">The tier thresholds to use.</param>
internal sealed class ChallengeCatalog(ProgressionSettings settings)
{
	private readonly Dictionary<WeaponKey, IReadOnlyList<ChallengeDefinition>> cache = [];

	/// <summary>
	/// Gets a weapon's ladder.
	/// </summary>
	/// <param name="weapon">The weapon.</param>
	/// <returns>The weapon's challenges, volume first.</returns>
	public IReadOnlyList<ChallengeDefinition> For(WeaponInfo weapon)
	{
		Ensure.NotNull(weapon);
		if (!cache.TryGetValue(weapon.Key, out IReadOnlyList<ChallengeDefinition>? ladder))
		{
			ladder = Build(weapon);
			cache[weapon.Key] = ladder;
		}

		return ladder;
	}

	/// <summary>
	/// Finds one challenge on a weapon's ladder.
	/// </summary>
	/// <param name="weapon">The weapon.</param>
	/// <param name="challengeId">The challenge id.</param>
	/// <returns>The challenge, or <see langword="null"/> when the weapon doesn't have it.</returns>
	public ChallengeDefinition? Find(WeaponInfo weapon, string challengeId) =>
		For(weapon).FirstOrDefault(c => string.Equals(c.Id, challengeId, StringComparison.Ordinal));

	private List<ChallengeDefinition> Build(WeaponInfo weapon)
	{
		List<ChallengeDefinition> ladder = [];

		// Only guns get Headshots and Hits. Grenades and the Zeus land too few hits for a ladder to mean
		// anything, and a knife hit is nearly always the kill.
		ladder.Add(new(ChallengeDefinition.KillsId, "Kills", ChallengeKind.Volume, Rarity.Common, settings.KillTiers));
		if (weapon.IsGun)
		{
			ladder.Add(new(ChallengeDefinition.HeadshotsId, "Headshots", ChallengeKind.Volume, Rarity.Common, settings.HeadshotTiers));
			ladder.Add(new(ChallengeDefinition.HitsId, "Hits", ChallengeKind.Volume, Rarity.Common, settings.HitTiers));
		}

		foreach (SkillShotDefinition skillShot in SkillShotCatalog.All.Where(s => s.AppliesTo(weapon)))
		{
			ladder.Add(new(skillShot.Id.WeakString, skillShot.Name, ChallengeKind.Skill, skillShot.Rarity, TiersFor(skillShot.Rarity)));
		}

		return ladder;
	}

	private IReadOnlyList<int> TiersFor(Rarity rarity) => rarity switch
	{
		Rarity.Epic => settings.EpicSkillTiers,
		Rarity.Rare => settings.RareSkillTiers,
		_ => settings.CommonSkillTiers,
	};
}

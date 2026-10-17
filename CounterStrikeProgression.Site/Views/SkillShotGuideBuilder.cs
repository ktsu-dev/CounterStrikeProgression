// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Views;

using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.SkillShots;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>One skill shot in the guide.</summary>
/// <param name="Id">The skill shot id.</param>
/// <param name="Name">The name players see.</param>
/// <param name="Description">What the player has to do.</param>
/// <param name="Rarity">How hard it is.</param>
/// <param name="Group">The kind of skill shot, such as Movement or Scope, which the guide groups by.</param>
/// <param name="When">When the plugin checks for it, in plain words.</param>
/// <param name="Weapons">The weapons that can earn it, in plain words, such as <c>Any gun</c> or <c>AWP, SSG 08</c>.</param>
/// <param name="Xp">The rank XP each one pays.</param>
/// <param name="Tiers">The count that finishes each tier of its challenge.</param>
/// <param name="Players">How many players have earned it.</param>
/// <param name="Total">How many times it has been earned on the server.</param>
internal sealed record SkillShotGuideEntry(
	string Id,
	string Name,
	string Description,
	Rarity Rarity,
	string Group,
	string When,
	string Weapons,
	int Xp,
	IReadOnlyList<int> Tiers,
	int Players,
	long Total);

/// <summary>The skill shot guide.</summary>
/// <param name="Updated">When the earned counts were last worked out for every player, or <see langword="null"/> while the first pass runs.</param>
/// <param name="SkillShots">Every skill shot, in catalog order.</param>
internal sealed record SkillShotGuideView(DateTimeOffset? Updated, IReadOnlyList<SkillShotGuideEntry> SkillShots);

/// <summary>
/// Builds the skill shot guide from <see cref="SkillShotCatalog"/>, with the XP and tiers from the plugin's
/// settings and how many players have earned each one.
/// </summary>
internal static class SkillShotGuideBuilder
{
	// The catalog lists skill shots in groups. Each group starts at the first id named here.
	private static readonly (string FirstId, string Group)[] Groups =
	[
		("moving_target", "Movement"),
		("long_shot", "Distance"),
		("wallbang_hit", "Walls and smoke"),
		("no_scope", "Scope"),
		("grenade_kill", "Utility and melee"),
		("quick_switch", "Timing and aim"),
		("double_kill", "Multi-kills and moments"),
		("chicken_nugget", "Chickens"),
	];

	private static readonly (WeaponClass Class, string Plural)[] ClassNames =
	[
		(WeaponClass.Pistol, "Pistols"),
		(WeaponClass.Smg, "SMGs"),
		(WeaponClass.Rifle, "Rifles"),
		(WeaponClass.Sniper, "Snipers"),
		(WeaponClass.Shotgun, "Shotguns"),
		(WeaponClass.MachineGun, "Machine guns"),
		(WeaponClass.Knife, "Knife"),
		(WeaponClass.Taser, "Zeus"),
		(WeaponClass.Grenade, "Grenades"),
	];

	/// <summary>
	/// Builds the guide.
	/// </summary>
	/// <param name="tallies">Every player's tally.</param>
	/// <param name="settings">The plugin's settings.</param>
	/// <param name="updated">When the tallies were last rebuilt, or <see langword="null"/> before the first rebuild.</param>
	/// <returns>The guide.</returns>
	public static SkillShotGuideView Build(IReadOnlyList<PlayerTally> tallies, ProgressionSettings settings, DateTimeOffset? updated)
	{
		Ensure.NotNull(tallies);
		Ensure.NotNull(settings);

		IReadOnlyDictionary<string, (int Players, long Total)> earned = LeaderboardViewBuilder.Earned(tallies);
		List<SkillShotGuideEntry> entries = [];
		string group = Groups[0].Group;
		foreach (SkillShotDefinition skillShot in SkillShotCatalog.All)
		{
			string id = skillShot.Id.WeakString;
			foreach ((string firstId, string name) in Groups)
			{
				if (string.Equals(firstId, id, StringComparison.Ordinal))
				{
					group = name;
				}
			}

			(int players, long total) = earned.GetValueOrDefault(id);
			entries.Add(new(
				id,
				skillShot.Name,
				skillShot.Description,
				skillShot.Rarity,
				group,
				When(skillShot.Trigger),
				Weapons(skillShot),
				Xp(skillShot.Rarity, settings),
				Tiers(skillShot.Rarity, settings),
				players,
				total));
		}

		return new(updated, entries);
	}

	/// <summary>
	/// Names the weapons that can earn a skill shot. A whole family is named once, such as <c>Pistols</c>, and every
	/// gun as <c>Any gun</c>.
	/// </summary>
	/// <param name="skillShot">The skill shot.</param>
	/// <returns>The weapons in plain words.</returns>
	public static string Weapons(SkillShotDefinition skillShot)
	{
		Ensure.NotNull(skillShot);

		List<WeaponInfo> applies = [.. WeaponCatalog.All.Where(skillShot.AppliesTo)];
		if (applies.Count == WeaponCatalog.All.Count)
		{
			return "Any weapon";
		}

		List<string> parts = [];
		List<WeaponInfo> guns = [.. WeaponCatalog.All.Where(w => w.IsGun)];
		if (guns.All(applies.Contains))
		{
			parts.Add("Any gun");
			applies.RemoveAll(w => w.IsGun);
		}

		foreach ((WeaponClass weaponClass, string plural) in ClassNames)
		{
			List<WeaponInfo> family = [.. WeaponCatalog.All.Where(w => w.Class == weaponClass)];
			List<WeaponInfo> chosen = [.. applies.Where(w => w.Class == weaponClass)];
			if (chosen.Count == 0)
			{
				continue;
			}

			if ((chosen.Count == family.Count) && (family.Count > 1))
			{
				parts.Add(plural);
			}
			else
			{
				parts.AddRange(chosen.Select(w => w.DisplayName));
			}
		}

		return string.Join(", ", parts);
	}

	private static string When(SkillTrigger trigger) => trigger switch
	{
		SkillTrigger.Hit => "On a hit",
		SkillTrigger.Kill => "On a kill",
		SkillTrigger.Reload => "When you reload",
		SkillTrigger.LifeEnd => "When a life ends",
		SkillTrigger.Assist => "On an assist",
		SkillTrigger.Flash => "When you blind an enemy",
		SkillTrigger.Clutch => "When the round ends",
		SkillTrigger.Chicken => "When you kill a chicken",
		_ => string.Empty,
	};

	private static int Xp(Rarity rarity, ProgressionSettings settings) => rarity switch
	{
		Rarity.Epic => settings.EpicSkillShotXp,
		Rarity.Rare => settings.RareSkillShotXp,
		_ => settings.CommonSkillShotXp,
	};

	private static IReadOnlyList<int> Tiers(Rarity rarity, ProgressionSettings settings) => rarity switch
	{
		Rarity.Epic => settings.EpicSkillTiers,
		Rarity.Rare => settings.RareSkillTiers,
		_ => settings.CommonSkillTiers,
	};
}

// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Contracts;

using System.Collections.Frozen;
using ktsu.CounterStrikeProgression.Core.Rotation;
using ktsu.CounterStrikeProgression.Core.SkillShots;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// Every contract, and which ones run on a given day. The picks come from a hash of the date, so the plugin and
/// the site agree on them with nothing stored. Contract ids are stored with progress, so never rename one.
/// </summary>
internal static class ContractCatalog
{
	/// <summary>
	/// Gets the daily pool, as groups of variants. A day draws whole groups, so it never gets two contracts of the
	/// same kind, and then one variant from each group drawn.
	/// </summary>
	public static IReadOnlyList<IReadOnlyList<ContractDefinition>> DailyGroups { get; } =
	[
		[
			ClassKills(WeaponClass.Pistol, "kills_pistol", 15),
			ClassKills(WeaponClass.Smg, "kills_smg", 20),
			ClassKills(WeaponClass.Rifle, "kills_rifle", 25),
			ClassKills(WeaponClass.Sniper, "kills_sniper", 10),
			ClassKills(WeaponClass.Shotgun, "kills_shotgun", 12),
			ClassKills(WeaponClass.MachineGun, "kills_machine_gun", 12),
		],
		[Daily("headshots", "Get 15 headshot kills", 15, s => (s.Kind == SignalKind.Kill) && s.Headshot)],
		[Daily("skill_shots", "Earn 10 skill shots", 10, s => s.Kind == SignalKind.SkillShot)],
		[Daily("rare_skill_shots", "Earn 3 rare or epic skill shots", 3, s => s.SkillShot is { Rarity: not Rarity.Common })],
		[Daily("wallbangs", "Get 2 kills through walls", 2, SkillShots("wallbang"))],
		[Daily("movement", "Earn 5 movement skill shots, such as Jumpshot or Moving target", 5, SkillShots("moving_target", "airborne_target", "jumpshot", "air_to_air", "run_and_gun", "landing_shot"))],
		[Daily("multi_kills", "Earn Double kill or better 3 times", 3, SkillShots("double_kill", "triple_kill", "rampage"))],
		[Daily("utility_kills", "Get 2 kills with the knife, a grenade, or the Zeus", 2, s => (s.Kind == SignalKind.Kill) && s.Weapon is { Class: WeaponClass.Knife or WeaponClass.Grenade or WeaponClass.Taser })],
		[Daily("chickens", "Kill 5 chickens", 5, s => s.Kind == SignalKind.Chicken)],
		[Daily("assists", "Get 5 assists", 5, s => s.Kind == SignalKind.Assist)],
		[Daily("scope_tricks", "Earn 2 No-scopes, Quickscopes, or Scoped on the moves", 2, SkillShots("no_scope", "quickscope", "scoped_on_the_move"))],
		[Daily("precision", "Earn One tap or Flick 3 times", 3, SkillShots("one_tap", "flick"))],
	];

	/// <summary>Gets the weekly pool. A week draws one.</summary>
	public static IReadOnlyList<ContractDefinition> Weekly { get; } =
	[
		Week("weekly_kills", "Get 250 kills", 250, s => s.Kind == SignalKind.Kill),
		Week("weekly_headshots", "Get 100 headshot kills", 100, s => (s.Kind == SignalKind.Kill) && s.Headshot),
		Week("weekly_skill_shots", "Earn 60 skill shots", 60, s => s.Kind == SignalKind.SkillShot),
		Week("weekly_epic", "Earn 3 epic skill shots", 3, s => s.SkillShot is { Rarity: Rarity.Epic }),
		Week("weekly_tiers", "Finish 5 challenge tiers", 5, s => s.Kind == SignalKind.Tier),
		Week("weekly_levels", "Gain 10 weapon levels", 10, s => s.Kind == SignalKind.WeaponLevel),
	];

	private static FrozenDictionary<string, ContractDefinition> ById { get; } =
		DailyGroups.SelectMany(g => g).Concat(Weekly).ToFrozenDictionary(c => c.Id, StringComparer.Ordinal);

	/// <summary>
	/// Looks up a contract by id.
	/// </summary>
	/// <param name="id">The contract id.</param>
	/// <returns>The contract, or <see langword="null"/> when there's no such id.</returns>
	public static ContractDefinition? Find(string id) => ById.GetValueOrDefault(id);

	/// <summary>
	/// Gets the contracts running on a day: its daily contracts first, then the week's.
	/// </summary>
	/// <param name="day">The UTC day.</param>
	/// <param name="settings">How many daily contracts there are and what each pays.</param>
	/// <returns>The contracts, or none when contracts are off.</returns>
	public static IReadOnlyList<ActiveContract> Active(DateOnly day, ProgressionSettings settings)
	{
		Ensure.NotNull(settings);
		if (!settings.ContractsEnabled)
		{
			return [];
		}

		string dayKey = DailyRotation.DayKey(day);
		DateTime dayEnds = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
		DateTime weekEnds = DailyRotation.WeekStart(day).AddDays(7).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

		int count = Math.Clamp(settings.DailyContracts, 0, DailyGroups.Count);
		List<ActiveContract> active =
		[
			.. Enumerable.Range(0, DailyGroups.Count)
				.OrderBy(i => DailyRotation.Hash(dayKey, $"daily:{i}"))
				.Take(count)
				.Order()
				.Select(i => Variant(DailyGroups[i], dayKey, i))
				.Select(c => new ActiveContract(c, dayKey, dayEnds, settings.DailyContractXp)),
		];

		string weekKey = DailyRotation.WeekKey(day);
		ContractDefinition weekly = Weekly[(int)(DailyRotation.Hash(weekKey, "weekly") % (ulong)Weekly.Count)];
		active.Add(new ActiveContract(weekly, weekKey, weekEnds, settings.WeeklyContractXp));
		return active;
	}

	private static ContractDefinition Variant(IReadOnlyList<ContractDefinition> group, string dayKey, int groupIndex) =>
		group.Count == 1 ? group[0] : group[(int)(DailyRotation.Hash(dayKey, $"variant:{groupIndex}") % (ulong)group.Count)];

	private static ContractDefinition ClassKills(WeaponClass weaponClass, string id, int target) =>
		Daily(id, $"Get {target} kills with {DailyRotation.ClassName(weaponClass)}", target, s => (s.Kind == SignalKind.Kill) && (s.Weapon?.Class == weaponClass));

	private static Func<ContractSignal, bool> SkillShots(params string[] ids)
	{
		HashSet<string> set = new(ids, StringComparer.Ordinal);
		return s => (s.SkillShot is not null) && set.Contains(s.SkillShot.Id.WeakString);
	}

	private static ContractDefinition Daily(string id, string description, int target, Func<ContractSignal, bool> counts) =>
		new(id, description, ContractPeriod.Daily, target, counts);

	private static ContractDefinition Week(string id, string description, int target, Func<ContractSignal, bool> counts) =>
		new(id, description, ContractPeriod.Weekly, target, counts);
}

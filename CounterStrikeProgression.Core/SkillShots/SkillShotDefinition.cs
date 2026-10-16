// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.SkillShots;

using ktsu.CounterStrikeProgression.Core.Events;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// How hard a skill shot is, which sets its XP and how many it takes to finish each tier of its challenge.
/// </summary>
internal enum Rarity
{
	Common = 0,
	Rare,
	Epic,
}

/// <summary>
/// The moment a skill shot is checked.
/// </summary>
internal enum SkillTrigger
{
	/// <summary>Every damaging bullet.</summary>
	Hit = 0,

	/// <summary>Every kill.</summary>
	Kill,

	/// <summary>The start of a reload.</summary>
	Reload,

	/// <summary>The end of a life, once per weapon used.</summary>
	LifeEnd,

	/// <summary>An assist.</summary>
	Assist,

	/// <summary>An enemy blinded by a flashbang.</summary>
	Flash,

	/// <summary>A round won as the team's last player alive.</summary>
	Clutch,
}

/// <summary>
/// One skill shot: what it's called, how rare it is, which weapons can earn it, and how it's detected.
/// </summary>
/// <param name="Id">The stable id, which is also the id of its challenge.</param>
/// <param name="Name">The name players see.</param>
/// <param name="Description">What the player has to do.</param>
/// <param name="Rarity">How hard it is.</param>
/// <param name="Trigger">When it's checked.</param>
/// <param name="AppliesTo">Whether a weapon can earn it, which also decides whether the weapon's ladder has its challenge.</param>
/// <param name="Detect">For hit and kill skill shots, whether a combat event earns it. Other triggers are decided by the engine.</param>
internal sealed record SkillShotDefinition(
	SkillShotId Id,
	string Name,
	string Description,
	Rarity Rarity,
	SkillTrigger Trigger,
	Func<WeaponInfo, bool> AppliesTo,
	Func<CombatEvent, ProgressionSettings, bool>? Detect = null);

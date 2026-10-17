// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Contracts;

using ktsu.CounterStrikeProgression.Core.SkillShots;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// How long a contract runs.
/// </summary>
internal enum ContractPeriod
{
	/// <summary>One UTC day.</summary>
	Daily = 0,

	/// <summary>One ISO week, Monday to Sunday in UTC.</summary>
	Weekly,
}

/// <summary>
/// The kinds of thing a contract can count.
/// </summary>
internal enum SignalKind
{
	/// <summary>A kill of a player or bot.</summary>
	Kill = 0,

	/// <summary>A skill shot earned.</summary>
	SkillShot,

	/// <summary>A chicken killed.</summary>
	Chicken,

	/// <summary>An assist.</summary>
	Assist,

	/// <summary>A challenge tier finished.</summary>
	Tier,

	/// <summary>A weapon level gained.</summary>
	WeaponLevel,
}

/// <summary>
/// One thing a player did that a contract might count.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="Weapon">The weapon involved, if any.</param>
/// <param name="Headshot">For kills, whether it was a headshot.</param>
/// <param name="SkillShot">For skill shots, which one.</param>
internal readonly record struct ContractSignal(SignalKind Kind, WeaponInfo? Weapon = null, bool Headshot = false, SkillShotDefinition? SkillShot = null);

/// <summary>
/// One contract: a short goal that pays a bonus once in its day or week.
/// </summary>
/// <param name="Id">The stable id, stored with the period's key. Never rename one.</param>
/// <param name="Description">What the player has to do, such as "Get 20 kills with SMGs".</param>
/// <param name="Period">How long the contract runs.</param>
/// <param name="Target">How many matching signals finish it.</param>
/// <param name="Counts">Whether a signal counts toward it.</param>
internal sealed record ContractDefinition(string Id, string Description, ContractPeriod Period, int Target, Func<ContractSignal, bool> Counts);

/// <summary>
/// A contract running now.
/// </summary>
/// <param name="Definition">The contract.</param>
/// <param name="PeriodKey">The key of the day or week it runs in, such as <c>2026-10-17</c> or <c>2026-W42</c>.</param>
/// <param name="EndsAt">When it ends, in UTC.</param>
/// <param name="Xp">The rank XP it pays.</param>
internal sealed record ActiveContract(ContractDefinition Definition, string PeriodKey, DateTime EndsAt, int Xp);

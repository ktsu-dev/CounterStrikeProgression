// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Progression;

using ktsu.CounterStrikeProgression.Core.Contracts;
using ktsu.CounterStrikeProgression.Core.Feats;
using ktsu.CounterStrikeProgression.Core.SkillShots;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// The kinds of moment worth telling a player about.
/// </summary>
internal enum NoticeKind
{
	SkillShot = 0,
	TierCompleted,
	WeaponLevelUp,
	RankUp,
	FeatEarned,
	ContractCompleted,
	BountyCollected,
}

/// <summary>
/// Something a player just earned. The plugin turns these into chat lines.
/// </summary>
internal sealed record ProgressionNotice
{
	/// <summary>Gets what happened.</summary>
	public required NoticeKind Kind { get; init; }

	/// <summary>Gets the weapon involved, if any.</summary>
	public WeaponInfo? Weapon { get; init; }

	/// <summary>Gets the skill shot earned, for skill shot notices.</summary>
	public SkillShotDefinition? SkillShot { get; init; }

	/// <summary>Gets the feat earned, for feat notices.</summary>
	public FeatDefinition? Feat { get; init; }

	/// <summary>Gets the contract finished, for contract notices.</summary>
	public ActiveContract? Contract { get; init; }

	/// <summary>Gets the challenge name, for tier notices.</summary>
	public string? ChallengeName { get; init; }

	/// <summary>Gets the player whose bounty was collected, for bounty notices.</summary>
	public string? Target { get; init; }

	/// <summary>Gets the tier reached, the weapon level reached, the rank reached, or the streak a bounty ended.</summary>
	public int Value { get; init; }

	/// <summary>Gets the rank XP the moment paid.</summary>
	public long Xp { get; init; }

	/// <summary>Gets a value indicating whether the whole server should hear about it.</summary>
	public bool Broadcast { get; init; }
}

/// <summary>
/// What one event earned a player.
/// </summary>
internal sealed class ProgressionResult
{
	/// <summary>Gets the whole rank XP points credited.</summary>
	public long XpGained { get; internal set; }

	/// <summary>
	/// Gets the rank XP earned, including fractions too small to credit yet, such as a quarter point for
	/// hitting a bot. It's what the XP floaties show.
	/// </summary>
	public double XpEarned { get; internal set; }

	/// <summary>Gets the moments worth telling the player about, in the order they happened.</summary>
	public IList<ProgressionNotice> Notices { get; } = [];

	/// <summary>Gets what the event did that contracts might count. The engine empties it once they're counted.</summary>
	internal IList<ContractSignal> Signals { get; } = [];
}

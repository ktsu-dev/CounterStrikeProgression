// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Progression;

using ktsu.CounterStrikeProgression.Core.Weapons;
using ktsu.Extensions;

/// <summary>
/// One challenge's count and the tier it has reached.
/// </summary>
internal sealed class ChallengeProgress
{
	/// <summary>Gets or sets how many times the challenge has been done.</summary>
	public long Count { get; set; }

	/// <summary>Gets or sets the number of tiers finished.</summary>
	public int TierReached { get; set; }

	/// <summary>Gets or sets a value indicating whether the row changed since it was last saved.</summary>
	public bool IsDirty { get; set; }
}

/// <summary>
/// One contract's count in one day or week.
/// </summary>
internal sealed class ContractProgress
{
	/// <summary>Gets or sets how many matching things the player has done.</summary>
	public long Count { get; set; }

	/// <summary>Gets or sets a value indicating whether the contract is finished and paid.</summary>
	public bool Completed { get; set; }

	/// <summary>Gets or sets a value indicating whether the row changed since it was last saved.</summary>
	public bool IsDirty { get; set; }
}

/// <summary>
/// How many times a player has killed one other human.
/// </summary>
internal sealed class RivalProgress
{
	/// <summary>Gets or sets the kills.</summary>
	public long Kills { get; set; }

	/// <summary>Gets or sets a value indicating whether the row changed since it was last saved.</summary>
	public bool IsDirty { get; set; }
}

/// <summary>
/// A player's progress with one weapon.
/// </summary>
/// <param name="weapon">The weapon.</param>
internal sealed class WeaponProgress(WeaponKey weapon)
{
	/// <summary>Gets the weapon.</summary>
	public WeaponKey Weapon { get; } = weapon;

	/// <summary>Gets or sets the XP earned with the weapon, which decides its level.</summary>
	public long Xp { get; set; }

	/// <summary>Gets or sets the weapon's level.</summary>
	public int Level { get; set; } = 1;

	/// <summary>Gets or sets kills with the weapon.</summary>
	public long Kills { get; set; }

	/// <summary>Gets or sets headshot kills with the weapon.</summary>
	public long Headshots { get; set; }

	/// <summary>Gets or sets damaging hits with the weapon.</summary>
	public long Hits { get; set; }

	/// <summary>Gets or sets shots fired with the weapon.</summary>
	public long Shots { get; set; }

	/// <summary>Gets the weapon's challenges, keyed by challenge id.</summary>
	public Dictionary<string, ChallengeProgress> Challenges { get; } = new(StringComparer.Ordinal);

	/// <summary>Gets or sets a value indicating whether the weapon row changed since it was last saved.</summary>
	public bool IsDirty { get; set; }

	/// <summary>
	/// Gets a challenge's progress, creating it when the player hasn't started it.
	/// </summary>
	/// <param name="challengeId">The challenge id.</param>
	/// <returns>The challenge's progress.</returns>
	public ChallengeProgress Challenge(string challengeId) => Challenges.GetOrCreate(challengeId);
}

/// <summary>
/// Everything one player has earned. Changes happen on the game thread, and the store saves rows flagged as dirty.
/// </summary>
/// <param name="steamId">The player's 64-bit Steam ID.</param>
internal sealed class PlayerProgress(ulong steamId)
{
	/// <summary>Gets the player's 64-bit Steam ID.</summary>
	public ulong SteamId { get; } = steamId;

	/// <summary>Gets or sets the player's last known name.</summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>Gets or sets the player's rank XP.</summary>
	public long RankXp { get; set; }

	/// <summary>Gets or sets the player's rank index.</summary>
	public int Rank { get; set; }

	/// <summary>
	/// Gets or sets the fraction of an XP point not yet credited, which carries small amounts such as one
	/// hit on a bot until they add up to a whole point. Not saved.
	/// </summary>
	public double PendingXp { get; set; }

	/// <summary>Gets or sets when the player was first seen.</summary>
	public DateTime FirstSeen { get; set; } = DateTime.UtcNow;

	/// <summary>Gets or sets when the player was last seen.</summary>
	public DateTime LastSeen { get; set; } = DateTime.UtcNow;

	/// <summary>
	/// Gets or sets the ids of the feats the player had when this session started counting, plus any earned
	/// since. Not saved, because feats are worked out from the rest of the progress.
	/// </summary>
	public HashSet<string>? EarnedFeats { get; set; }

	/// <summary>Gets the player's weapons, keyed by weapon.</summary>
	public Dictionary<WeaponKey, WeaponProgress> Weapons { get; } = [];

	/// <summary>Gets the player's contracts, keyed by the period's key and the contract id. Only current periods are loaded.</summary>
	public Dictionary<(string Period, string Contract), ContractProgress> Contracts { get; } = [];

	/// <summary>Gets the humans the player has killed, keyed by their Steam ID. Saved to <c>csp_rivals</c>.</summary>
	public Dictionary<ulong, RivalProgress> Victims { get; } = [];

	/// <summary>
	/// Gets how many times each human has killed the player, keyed by their Steam ID. Not saved, because each count is
	/// the other player's own row.
	/// </summary>
	public Dictionary<ulong, long> KilledBy { get; } = [];

	/// <summary>Gets the last known names of the players in <see cref="Victims"/> and <see cref="KilledBy"/>. Not saved.</summary>
	public Dictionary<ulong, string> RivalNames { get; } = [];

	/// <summary>
	/// Gets or sets the first day of the season <see cref="SeasonXp"/> was earned in, or <see langword="null"/> before
	/// the player has earned any. Only the latest season is loaded, so it can be a season that has already ended.
	/// </summary>
	public DateOnly? SeasonStart { get; set; }

	/// <summary>Gets or sets the rank XP earned in the season that starts on <see cref="SeasonStart"/>.</summary>
	public long SeasonXp { get; set; }

	/// <summary>Gets or sets a value indicating whether the season row changed since it was last saved.</summary>
	public bool IsSeasonDirty { get; set; }

	/// <summary>
	/// Gets or sets the final XP of a season that ended before its last change was saved, so the next save
	/// still writes it. <see langword="null"/> when there's nothing waiting.
	/// </summary>
	public (DateOnly Start, long Xp)? EndedSeason { get; set; }

	/// <summary>Gets or sets a value indicating whether the player row changed since it was last saved.</summary>
	public bool IsDirty { get; set; }

	/// <summary>
	/// Gets a contract's progress in a period, creating it when the player hasn't started it.
	/// </summary>
	/// <param name="period">The period's key, such as <c>2026-10-17</c> or <c>2026-W42</c>.</param>
	/// <param name="contract">The contract id.</param>
	/// <returns>The contract's progress.</returns>
	public ContractProgress Contract(string period, string contract) => Contracts.GetOrCreate((period, contract));

	/// <summary>
	/// Gets the player's kills of one other human, creating the row when they haven't killed them.
	/// </summary>
	/// <param name="victim">The other player's 64-bit Steam ID.</param>
	/// <returns>The kills.</returns>
	public RivalProgress Victim(ulong victim) => Victims.GetOrCreate(victim);

	/// <summary>
	/// Gets a weapon's progress, creating it when the player hasn't used the weapon.
	/// </summary>
	/// <param name="weapon">The weapon.</param>
	/// <returns>The weapon's progress.</returns>
	public WeaponProgress Weapon(WeaponKey weapon)
	{
		if (!Weapons.TryGetValue(weapon, out WeaponProgress? progress))
		{
			progress = new(weapon);
			Weapons[weapon] = progress;
		}

		return progress;
	}
}

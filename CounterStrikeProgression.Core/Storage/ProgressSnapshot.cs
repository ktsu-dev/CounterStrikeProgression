// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Storage;

using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>A player row as stored.</summary>
/// <param name="SteamId">The 64-bit Steam ID.</param>
/// <param name="Name">The last known name.</param>
/// <param name="RankXp">The rank XP.</param>
/// <param name="Rank">The rank index.</param>
/// <param name="FirstSeen">When the player was first seen, in UTC.</param>
/// <param name="LastSeen">When the player was last seen, in UTC.</param>
internal sealed record PlayerRow(ulong SteamId, string Name, long RankXp, int Rank, DateTime FirstSeen, DateTime LastSeen);

/// <summary>A weapon row as stored.</summary>
/// <param name="SteamId">The 64-bit Steam ID.</param>
/// <param name="Weapon">The weapon key.</param>
/// <param name="Xp">The weapon XP.</param>
/// <param name="Level">The weapon level.</param>
/// <param name="Kills">Kills with the weapon.</param>
/// <param name="Headshots">Headshot kills with the weapon.</param>
/// <param name="Hits">Hits with the weapon.</param>
/// <param name="Shots">Shots fired with the weapon.</param>
internal sealed record WeaponRow(ulong SteamId, string Weapon, long Xp, int Level, long Kills, long Headshots, long Hits, long Shots);

/// <summary>A challenge row as stored.</summary>
/// <param name="SteamId">The 64-bit Steam ID.</param>
/// <param name="Weapon">The weapon key.</param>
/// <param name="Challenge">The challenge id.</param>
/// <param name="Count">The challenge count.</param>
/// <param name="Tier">The number of tiers finished.</param>
internal sealed record ChallengeRow(ulong SteamId, string Weapon, string Challenge, long Count, int Tier);

/// <summary>
/// The rows of one player that changed since the last save, copied on the game thread so the database
/// write can happen elsewhere. Every row holds full values, never increments, so saving the same snapshot
/// twice is harmless.
/// </summary>
/// <param name="Player">The player row, or <see langword="null"/> when it didn't change.</param>
/// <param name="Weapons">The weapon rows that changed.</param>
/// <param name="Challenges">The challenge rows that changed.</param>
internal sealed record ProgressSnapshot(PlayerRow? Player, IReadOnlyList<WeaponRow> Weapons, IReadOnlyList<ChallengeRow> Challenges)
{
	/// <summary>Gets a value indicating whether there's nothing to save.</summary>
	public bool IsEmpty => (Player is null) && (Weapons.Count == 0) && (Challenges.Count == 0);

	/// <summary>
	/// Copies a player's changed rows and clears their dirty flags.
	/// </summary>
	/// <param name="progress">The player's progress.</param>
	/// <returns>The rows to save.</returns>
	public static ProgressSnapshot Capture(PlayerProgress progress)
	{
		Ensure.NotNull(progress);

		PlayerRow? player = null;
		if (progress.IsDirty)
		{
			player = ToRow(progress);
			progress.IsDirty = false;
		}

		List<WeaponRow> weapons = [];
		List<ChallengeRow> challenges = [];
		foreach (WeaponProgress weapon in progress.Weapons.Values)
		{
			if (weapon.IsDirty)
			{
				weapons.Add(new(progress.SteamId, weapon.Weapon.WeakString, weapon.Xp, weapon.Level, weapon.Kills, weapon.Headshots, weapon.Hits, weapon.Shots));
				weapon.IsDirty = false;
			}

			foreach ((string id, ChallengeProgress challenge) in weapon.Challenges)
			{
				if (challenge.IsDirty)
				{
					challenges.Add(new(progress.SteamId, weapon.Weapon.WeakString, id, challenge.Count, challenge.TierReached));
					challenge.IsDirty = false;
				}
			}
		}

		return new(player, weapons, challenges);
	}

	/// <summary>
	/// Makes a player row from progress, whether or not it changed.
	/// </summary>
	/// <param name="progress">The player's progress.</param>
	/// <returns>The row.</returns>
	public static PlayerRow ToRow(PlayerProgress progress)
	{
		Ensure.NotNull(progress);
		return new(progress.SteamId, progress.Name, progress.RankXp, progress.Rank, progress.FirstSeen, progress.LastSeen);
	}

	/// <summary>
	/// Flags this snapshot's rows as dirty again after a failed save, so the next save retries them.
	/// </summary>
	/// <param name="progress">The same player's progress.</param>
	public void Restore(PlayerProgress progress)
	{
		Ensure.NotNull(progress);

		if (Player is not null)
		{
			progress.IsDirty = true;
		}

		foreach (WeaponRow row in Weapons)
		{
			progress.Weapon(WeaponKey.Create(row.Weapon)).IsDirty = true;
		}

		foreach (ChallengeRow row in Challenges)
		{
			progress.Weapon(WeaponKey.Create(row.Weapon)).Challenge(row.Challenge).IsDirty = true;
		}
	}

	/// <summary>
	/// Rebuilds a player's progress from stored rows.
	/// </summary>
	/// <param name="player">The player row.</param>
	/// <param name="weapons">The player's weapon rows.</param>
	/// <param name="challenges">The player's challenge rows.</param>
	/// <returns>The progress, with nothing flagged dirty.</returns>
	public static PlayerProgress Rebuild(PlayerRow player, IEnumerable<WeaponRow> weapons, IEnumerable<ChallengeRow> challenges)
	{
		Ensure.NotNull(player);
		Ensure.NotNull(weapons);
		Ensure.NotNull(challenges);

		PlayerProgress progress = new(player.SteamId)
		{
			Name = player.Name,
			RankXp = player.RankXp,
			Rank = player.Rank,
			FirstSeen = player.FirstSeen,
			LastSeen = player.LastSeen,
		};

		foreach (WeaponRow row in weapons)
		{
			if (!WeaponKey.TryCreate(row.Weapon, out WeaponKey? key) || key is null)
			{
				continue;
			}

			WeaponProgress weapon = progress.Weapon(key);
			weapon.Xp = row.Xp;
			weapon.Level = row.Level;
			weapon.Kills = row.Kills;
			weapon.Headshots = row.Headshots;
			weapon.Hits = row.Hits;
			weapon.Shots = row.Shots;
		}

		foreach (ChallengeRow row in challenges)
		{
			if (!WeaponKey.TryCreate(row.Weapon, out WeaponKey? key) || key is null)
			{
				continue;
			}

			ChallengeProgress challenge = progress.Weapon(key).Challenge(row.Challenge);
			challenge.Count = row.Count;
			challenge.TierReached = row.Tier;
		}

		return progress;
	}
}

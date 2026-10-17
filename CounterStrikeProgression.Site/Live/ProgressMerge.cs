// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Live;

using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// Combines stored progress with the live rows the plugin sent. Progress only goes up, so the larger value
/// is the newer one, whichever side holds it.
/// </summary>
internal static class ProgressMerge
{
	/// <summary>
	/// Merges stored progress with live rows, taking the larger value of every counter.
	/// </summary>
	/// <param name="stored">The progress read from the database, if the player has any.</param>
	/// <param name="live">The live rows, if any have arrived.</param>
	/// <returns>The merged progress.</returns>
	public static PlayerProgress Merge(PlayerProgress? stored, ProgressSnapshot? live)
	{
		ulong steamId = stored?.SteamId
			?? SteamIdOf(live)
			?? throw new ArgumentException("There is no progress to merge.", nameof(stored));

		PlayerProgress result = new(steamId);
		if (stored is not null)
		{
			CopyStored(stored, result);
		}

		if (live is not null)
		{
			ApplyLive(result, live, stored is null);
		}

		return result;
	}

	/// <summary>
	/// Combines two snapshots of the same player into one. Progress only goes up, so every counter takes the larger value, and a late batch can't pull the overlay back down.
	/// </summary>
	/// <param name="older">The snapshot received earlier.</param>
	/// <param name="newer">The snapshot received later.</param>
	/// <returns>The combined snapshot.</returns>
	public static ProgressSnapshot Combine(ProgressSnapshot older, ProgressSnapshot newer)
	{
		Ensure.NotNull(older);
		Ensure.NotNull(newer);

		Dictionary<string, WeaponRow> weapons = older.Weapons.ToDictionary(w => w.Weapon, StringComparer.Ordinal);
		foreach (WeaponRow row in newer.Weapons)
		{
			weapons[row.Weapon] = weapons.TryGetValue(row.Weapon, out WeaponRow? existing) ? LargerWeapon(existing, row) : row;
		}

		Dictionary<(string Weapon, string Challenge), ChallengeRow> challenges = older.Challenges.ToDictionary(c => (c.Weapon, c.Challenge));
		foreach (ChallengeRow row in newer.Challenges)
		{
			challenges[(row.Weapon, row.Challenge)] = challenges.TryGetValue((row.Weapon, row.Challenge), out ChallengeRow? existing)
				? existing with { Count = Math.Max(existing.Count, row.Count), Tier = Math.Max(existing.Tier, row.Tier) }
				: row;
		}

		PlayerRow? player = (older.Player, newer.Player) switch
		{
			(null, var n) => n,
			(var o, null) => o,
			(var o, var n) => LargerPlayer(o!, n!),
		};

		return new(player, [.. weapons.Values], [.. challenges.Values]);
	}

	/// <summary>
	/// Finds the player a snapshot belongs to.
	/// </summary>
	/// <param name="snapshot">The snapshot.</param>
	/// <returns>The Steam ID, or <see langword="null"/> when the snapshot has no rows.</returns>
	public static ulong? SteamIdOf(ProgressSnapshot? snapshot)
	{
		if (snapshot is null)
		{
			return null;
		}

		if (snapshot.Player is { } player)
		{
			return player.SteamId;
		}

		if (snapshot.Weapons.Count > 0)
		{
			return snapshot.Weapons[0].SteamId;
		}

		return snapshot.Challenges.Count > 0 ? snapshot.Challenges[0].SteamId : null;
	}

	private static void CopyStored(PlayerProgress stored, PlayerProgress result)
	{
		result.Name = stored.Name;
		result.RankXp = stored.RankXp;
		result.Rank = stored.Rank;
		result.FirstSeen = stored.FirstSeen;
		result.LastSeen = stored.LastSeen;
		foreach (WeaponProgress weapon in stored.Weapons.Values)
		{
			WeaponProgress copy = result.Weapon(weapon.Weapon);
			copy.Xp = weapon.Xp;
			copy.Level = weapon.Level;
			copy.Kills = weapon.Kills;
			copy.Headshots = weapon.Headshots;
			copy.Hits = weapon.Hits;
			copy.Shots = weapon.Shots;
			foreach ((string id, ChallengeProgress challenge) in weapon.Challenges)
			{
				ChallengeProgress challengeCopy = copy.Challenge(id);
				challengeCopy.Count = challenge.Count;
				challengeCopy.TierReached = challenge.TierReached;
			}
		}
	}

	private static void ApplyLive(PlayerProgress result, ProgressSnapshot live, bool noStored)
	{
		if (live.Player is { } player)
		{
			result.Name = player.Name;
			result.RankXp = Math.Max(result.RankXp, player.RankXp);
			result.Rank = Math.Max(result.Rank, player.Rank);
			result.FirstSeen = noStored ? player.FirstSeen : Earlier(result.FirstSeen, player.FirstSeen);
			result.LastSeen = noStored ? player.LastSeen : Later(result.LastSeen, player.LastSeen);
		}

		foreach (WeaponRow row in live.Weapons)
		{
			if (!WeaponKey.TryCreate(row.Weapon, out WeaponKey? key) || key is null)
			{
				continue;
			}

			WeaponProgress weapon = result.Weapon(key);
			weapon.Xp = Math.Max(weapon.Xp, row.Xp);
			weapon.Level = Math.Max(weapon.Level, row.Level);
			weapon.Kills = Math.Max(weapon.Kills, row.Kills);
			weapon.Headshots = Math.Max(weapon.Headshots, row.Headshots);
			weapon.Hits = Math.Max(weapon.Hits, row.Hits);
			weapon.Shots = Math.Max(weapon.Shots, row.Shots);
		}

		foreach (ChallengeRow row in live.Challenges)
		{
			if (!WeaponKey.TryCreate(row.Weapon, out WeaponKey? key) || key is null)
			{
				continue;
			}

			ChallengeProgress challenge = result.Weapon(key).Challenge(row.Challenge);
			challenge.Count = Math.Max(challenge.Count, row.Count);
			challenge.TierReached = Math.Max(challenge.TierReached, row.Tier);
		}
	}

	private static WeaponRow LargerWeapon(WeaponRow a, WeaponRow b) => a with
	{
		Xp = Math.Max(a.Xp, b.Xp),
		Level = Math.Max(a.Level, b.Level),
		Kills = Math.Max(a.Kills, b.Kills),
		Headshots = Math.Max(a.Headshots, b.Headshots),
		Hits = Math.Max(a.Hits, b.Hits),
		Shots = Math.Max(a.Shots, b.Shots),
	};

	private static PlayerRow LargerPlayer(PlayerRow older, PlayerRow newer) => older with
	{
		Name = older.LastSeen > newer.LastSeen ? older.Name : newer.Name,
		RankXp = Math.Max(older.RankXp, newer.RankXp),
		Rank = Math.Max(older.Rank, newer.Rank),
		FirstSeen = Earlier(older.FirstSeen, newer.FirstSeen),
		LastSeen = Later(older.LastSeen, newer.LastSeen),
	};

	private static DateTime Earlier(DateTime a, DateTime b) => a <= b ? a : b;

	private static DateTime Later(DateTime a, DateTime b) => a >= b ? a : b;
}

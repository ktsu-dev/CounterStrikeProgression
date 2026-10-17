// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Live;

using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;

/// <summary>
/// Remembers the rows last sent to the site, so each batch carries only what changed since.
/// </summary>
internal sealed class ProgressChangeTracker
{
	private readonly Dictionary<ulong, Sent> sent = [];

	/// <summary>
	/// Finds the rows that differ from what was last sent for the player, and remembers the new values.
	/// </summary>
	/// <param name="progress">The player's progress.</param>
	/// <returns>The changed rows, or <see langword="null"/> when nothing changed.</returns>
	public ProgressSnapshot? Changes(PlayerProgress progress)
	{
		Ensure.NotNull(progress);

		ProgressSnapshot all = ProgressSnapshot.CaptureAll(progress);
		if (!sent.TryGetValue(progress.SteamId, out Sent? last))
		{
			last = new Sent();
			sent[progress.SteamId] = last;
		}

		PlayerRow? player = null;
		if (all.Player is not null)
		{
			// LastSeen moves while a player stands still, so it doesn't count as a change.
			PlayerRow comparable = all.Player with { LastSeen = default };
			if (last.Player != comparable)
			{
				player = all.Player;
				last.Player = comparable;
			}
		}

		List<WeaponRow> weapons = [];
		foreach (WeaponRow row in all.Weapons)
		{
			if (!last.Weapons.TryGetValue(row.Weapon, out WeaponRow? previous) || (previous != row))
			{
				weapons.Add(row);
				last.Weapons[row.Weapon] = row;
			}
		}

		List<ChallengeRow> challenges = [];
		foreach (ChallengeRow row in all.Challenges)
		{
			(string, string) key = (row.Weapon, row.Challenge);
			if (!last.Challenges.TryGetValue(key, out ChallengeRow? previous) || (previous != row))
			{
				challenges.Add(row);
				last.Challenges[key] = row;
			}
		}

		List<ContractRow> contracts = [];
		foreach (ContractRow row in all.Contracts)
		{
			(string, string) key = (row.Period, row.Contract);
			if (!last.Contracts.TryGetValue(key, out ContractRow? previous) || (previous != row))
			{
				contracts.Add(row);
				last.Contracts[key] = row;
			}
		}

		List<SeasonRow> seasons = [];
		foreach (SeasonRow row in all.Seasons)
		{
			if (last.Season != row)
			{
				seasons.Add(row);
				last.Season = row;
			}
		}

		ProgressSnapshot changes = new(player, weapons, challenges) { Contracts = contracts, Seasons = seasons };
		return changes.IsEmpty ? null : changes;
	}

	/// <summary>
	/// Forgets what was sent for a player, so the next call returns all of their rows.
	/// </summary>
	/// <param name="steamId">The player's 64-bit Steam ID.</param>
	public void Forget(ulong steamId) => sent.Remove(steamId);

	/// <summary>
	/// Forgets everything sent, so the next call for each player returns all of their rows.
	/// </summary>
	public void Reset() => sent.Clear();

	private sealed class Sent
	{
		public PlayerRow? Player { get; set; }

		public SeasonRow? Season { get; set; }

		public Dictionary<string, WeaponRow> Weapons { get; } = new(StringComparer.Ordinal);

		public Dictionary<(string, string), ChallengeRow> Challenges { get; } = [];

		public Dictionary<(string, string), ContractRow> Contracts { get; } = [];
	}
}

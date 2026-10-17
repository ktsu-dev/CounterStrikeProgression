// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Storage;

using System.Collections.Concurrent;
using ktsu.CounterStrikeProgression.Core.Progression;

/// <summary>
/// Keeps progress in memory. Used by the tests, and by the plugin when no database is configured, in which
/// case progress lasts only until the server restarts.
/// </summary>
internal sealed class InMemoryProgressionStore : IProgressionStore
{
	private readonly ConcurrentDictionary<ulong, PlayerRow> players = new();
	private readonly ConcurrentDictionary<(ulong, string), WeaponRow> weapons = new();
	private readonly ConcurrentDictionary<(ulong, string, string), ChallengeRow> challenges = new();

	/// <summary>Gets the number of successful saves, for tests.</summary>
	public int SaveCount { get; private set; }

	/// <inheritdoc/>
	public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	/// <inheritdoc/>
	public Task<PlayerProgress?> LoadAsync(ulong steamId, CancellationToken cancellationToken)
	{
		if (!players.TryGetValue(steamId, out PlayerRow? player))
		{
			return Task.FromResult<PlayerProgress?>(null);
		}

		PlayerProgress progress = ProgressSnapshot.Rebuild(
			player,
			weapons.Values.Where(w => w.SteamId == steamId),
			challenges.Values.Where(c => c.SteamId == steamId));
		return Task.FromResult<PlayerProgress?>(progress);
	}

	/// <inheritdoc/>
	public Task SaveAsync(ProgressSnapshot snapshot, CancellationToken cancellationToken)
	{
		Ensure.NotNull(snapshot);

		if (snapshot.Player is not null)
		{
			players[snapshot.Player.SteamId] = snapshot.Player;
		}

		foreach (WeaponRow row in snapshot.Weapons)
		{
			weapons[(row.SteamId, row.Weapon)] = row;
		}

		foreach (ChallengeRow row in snapshot.Challenges)
		{
			challenges[(row.SteamId, row.Weapon, row.Challenge)] = row;
		}

		SaveCount++;
		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	public Task ResetAsync(PlayerRow player, CancellationToken cancellationToken)
	{
		Ensure.NotNull(player);

		foreach ((ulong, string) key in weapons.Keys.Where(k => k.Item1 == player.SteamId).ToList())
		{
			weapons.TryRemove(key, out _);
		}

		foreach ((ulong, string, string) key in challenges.Keys.Where(k => k.Item1 == player.SteamId).ToList())
		{
			challenges.TryRemove(key, out _);
		}

		players[player.SteamId] = player;
		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	public Task<IReadOnlyList<LeaderboardEntry>> TopAsync(int count, CancellationToken cancellationToken)
	{
		IReadOnlyList<LeaderboardEntry> top = [.. players.Values.OrderByDescending(p => p.RankXp).Take(count).Select(p => new LeaderboardEntry(p.Name, p.RankXp, p.Rank))];
		return Task.FromResult(top);
	}

	/// <inheritdoc/>
	public Task<IReadOnlyList<RankingEntry>> RankingsAsync(int count, CancellationToken cancellationToken)
	{
		IReadOnlyList<RankingEntry> rankings = [.. players.Values
			.OrderByDescending(p => p.RankXp)
			.ThenBy(p => p.SteamId)
			.Take(count)
			.Select(p => new RankingEntry(p.SteamId, p.Name, p.RankXp, p.Rank, p.LastSeen))];
		return Task.FromResult(rankings);
	}
}

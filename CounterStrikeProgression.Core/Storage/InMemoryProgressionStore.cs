// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Storage;

using System.Collections.Concurrent;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// Keeps progress in memory. Used by the tests, and by the plugin when no database is configured, in which
/// case progress lasts only until the server restarts.
/// </summary>
internal sealed class InMemoryProgressionStore : IProgressionStore
{
	private readonly ConcurrentDictionary<ulong, PlayerRow> players = new();
	private readonly ConcurrentDictionary<(ulong, string), WeaponRow> weapons = new();
	private readonly ConcurrentDictionary<(ulong, string, string), ChallengeRow> challenges = new();
	private readonly ConcurrentDictionary<(ulong, DateTime), SeasonRow> seasons = new();

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
			challenges.Values.Where(c => c.SteamId == steamId),
			seasons.Values.Where(s => s.SteamId == steamId).MaxBy(s => s.Start));
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

		foreach (SeasonRow row in snapshot.Seasons)
		{
			seasons[(row.SteamId, row.Start)] = row;
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

		foreach ((ulong, DateTime) key in seasons.Keys.Where(k => k.Item1 == player.SteamId).ToList())
		{
			seasons.TryRemove(key, out _);
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
	public Task<IReadOnlyList<WeaponLeaderboardEntry>> TopByWeaponAsync(WeaponKey weapon, int count, CancellationToken cancellationToken)
	{
		Ensure.NotNull(weapon);
		IReadOnlyList<WeaponLeaderboardEntry> top = [.. weapons.Values
			.Where(w => string.Equals(w.Weapon, weapon.WeakString, StringComparison.Ordinal) && (w.Xp > 0))
			.OrderByDescending(w => w.Xp)
			.ThenBy(w => w.SteamId)
			.Take(count)
			.Select(w => new WeaponLeaderboardEntry(players.TryGetValue(w.SteamId, out PlayerRow? p) ? p.Name : string.Empty, w.Xp, w.Level))];
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

	/// <inheritdoc/>
	public Task<IReadOnlyList<SeasonStanding>> SeasonStandingsAsync(DateOnly start, int count, CancellationToken cancellationToken)
	{
		DateTime day = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
		IReadOnlyList<SeasonStanding> standings = [.. seasons.Values
			.Where(s => s.Start == day && s.Xp > 0 && players.ContainsKey(s.SteamId))
			.OrderByDescending(s => s.Xp)
			.ThenBy(s => s.SteamId)
			.Take(count)
			.Select(s => new SeasonStanding(s.SteamId, players[s.SteamId].Name, players[s.SteamId].Rank, s.Xp))];
		return Task.FromResult(standings);
	}

	/// <inheritdoc/>
	public Task<IReadOnlyList<SeasonResult>> SeasonResultsAsync(ulong steamId, CancellationToken cancellationToken)
	{
		IReadOnlyList<SeasonResult> results = [.. seasons.Values
			.Where(s => s.SteamId == steamId && s.Xp > 0)
			.OrderByDescending(s => s.Start)
			.Select(s =>
			{
				SeasonRow[] season = [.. seasons.Values.Where(o => o.Start == s.Start && o.Xp > 0)];
				return new SeasonResult(s.Start, s.Xp, season.LongCount(o => o.Xp > s.Xp) + 1, season.LongLength);
			})];
		return Task.FromResult(results);
	}
}

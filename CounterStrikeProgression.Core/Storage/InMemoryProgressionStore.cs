// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Storage;

using System.Collections.Concurrent;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Rivals;
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
	private readonly ConcurrentDictionary<(ulong, string, string), ContractRow> contracts = new();
	private readonly ConcurrentDictionary<(ulong SteamId, DateOnly Day), long> daily = new();
	private readonly ConcurrentDictionary<(ulong, DateTime), SeasonRow> seasons = new();
	private readonly ConcurrentDictionary<DateOnly, DateTime> digests = new();
	private readonly ConcurrentDictionary<(ulong SteamId, ulong VictimId), long> rivals = new();

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
			seasons.Values.Where(s => s.SteamId == steamId).MaxBy(s => s.Start),
			contracts.Values.Where(c => c.SteamId == steamId));
		Rivalry.Load(progress, HeadToHeadOf(steamId));
		return Task.FromResult<PlayerProgress?>(progress);
	}

	/// <inheritdoc/>
	public Task SaveAsync(ProgressSnapshot snapshot, CancellationToken cancellationToken)
	{
		Ensure.NotNull(snapshot);

		if (snapshot.Player is not null)
		{
			players[snapshot.Player.SteamId] = snapshot.Player;
			daily[(snapshot.Player.SteamId, DateOnly.FromDateTime(snapshot.Player.LastSeen))] = snapshot.Player.RankXp;
		}

		foreach (WeaponRow row in snapshot.Weapons)
		{
			weapons[(row.SteamId, row.Weapon)] = row;
		}

		foreach (ChallengeRow row in snapshot.Challenges)
		{
			challenges[(row.SteamId, row.Weapon, row.Challenge)] = row;
		}

		foreach (ContractRow row in snapshot.Contracts)
		{
			contracts[(row.SteamId, row.Period, row.Contract)] = row;
		}

		foreach (SeasonRow row in snapshot.Seasons)
		{
			seasons[(row.SteamId, row.Start)] = row;
		}

		foreach (RivalRow row in snapshot.Rivals)
		{
			rivals[(row.SteamId, row.VictimId)] = row.Kills;
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

		foreach ((ulong, string, string) key in contracts.Keys.Where(k => k.Item1 == player.SteamId).ToList())
		{
			contracts.TryRemove(key, out _);
		}

		foreach ((ulong SteamId, ulong VictimId) key in rivals.Keys.Where(k => k.SteamId == player.SteamId).ToList())
		{
			rivals.TryRemove(key, out _);
		}

		foreach ((ulong, DateTime) key in seasons.Keys.Where(k => k.Item1 == player.SteamId).ToList())
		{
			seasons.TryRemove(key, out _);
		}

		foreach ((ulong SteamId, DateOnly Day) key in daily.Keys.Where(k => k.SteamId == player.SteamId).ToList())
		{
			daily.TryRemove(key, out _);
		}

		// A reset starts the player's history again, from the day before, so the XP they keep isn't counted as earned.
		daily[(player.SteamId, DateOnly.FromDateTime(player.LastSeen).AddDays(-1))] = player.RankXp;
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
	public Task<IReadOnlyList<DailyXp>> HistoryAsync(ulong steamId, DateOnly from, CancellationToken cancellationToken)
	{
		List<DailyXp> rows = RowsOf(steamId);
		DailyXp? before = rows.LastOrDefault(r => r.Day < from);
		IReadOnlyList<DailyXp> history = [.. rows.Where(r => r.Day >= from || r == before || r == rows[0])];
		return Task.FromResult(history);
	}

	/// <inheritdoc/>
	public Task<IReadOnlyList<XpGainEntry>> GainsSinceAsync(DateOnly start, int count, CancellationToken cancellationToken)
	{
		DateTime since = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
		IReadOnlyList<XpGainEntry> gains = [.. players.Values
			.Where(p => p.LastSeen >= since)
			.Select(p => new XpGainEntry(p.SteamId, p.Name, p.RankXp, p.Rank, XpHistory.GainedSince(RowsOf(p.SteamId), start, p.FirstSeen, p.RankXp)))
			.Where(g => g.Gained > 0)
			.OrderByDescending(g => g.Gained)
			.ThenBy(g => g.SteamId)
			.Take(count)];
		return Task.FromResult(gains);
	}

	/// <inheritdoc/>
	public Task<IReadOnlyList<PeriodGain>> GainsBetweenAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken)
	{
		DateTime since = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
		DateTime until = end.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
		List<PeriodGain> gains = [];
		foreach (PlayerRow p in players.Values.Where(p => p.LastSeen >= since && p.FirstSeen < until))
		{
			List<DailyXp> rows = RowsOf(p.SteamId);
			if ((XpHistory.Baseline(rows, start, p.FirstSeen) is long startXp) && (rows.LastOrDefault(r => r.Day < end) is { } last) && (last.RankXp > startXp))
			{
				gains.Add(new(p.SteamId, p.Name, startXp, last.RankXp));
			}
		}

		IReadOnlyList<PeriodGain> ordered = [.. gains.OrderByDescending(g => g.Gained).ThenBy(g => g.SteamId)];
		return Task.FromResult(ordered);
	}

	/// <inheritdoc/>
	public Task<bool> ClaimDigestAsync(DateOnly week, DateTime at, CancellationToken cancellationToken) =>
		Task.FromResult(digests.TryAdd(week, at));

	/// <inheritdoc/>
	public Task<IReadOnlyList<HeadToHead>> HeadToHeadAsync(ulong steamId, int count, CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<HeadToHead>>([.. HeadToHeadOf(steamId).Take(count)]);

	private List<HeadToHead> HeadToHeadOf(ulong steamId) =>
	[
		.. rivals
			.Where(r => (r.Value > 0) && ((r.Key.SteamId == steamId) || (r.Key.VictimId == steamId)))
			.GroupBy(r => r.Key.SteamId == steamId ? r.Key.VictimId : r.Key.SteamId)
			.Select(g => new HeadToHead(
				g.Key,
				players.TryGetValue(g.Key, out PlayerRow? p) ? p.Name : string.Empty,
				g.Where(r => r.Key.SteamId == steamId).Sum(r => r.Value),
				g.Where(r => r.Key.VictimId == steamId).Sum(r => r.Value)))
			.OrderByDescending(h => h.Kills + h.Deaths)
			.ThenBy(h => h.SteamId),
	];

	private List<DailyXp> RowsOf(ulong steamId) =>
		[.. daily.Where(d => d.Key.SteamId == steamId).Select(d => new DailyXp(d.Key.Day, d.Value)).OrderBy(d => d.Day)];

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

// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Storage;

using ktsu.CounterStrikeProgression.Core.Maps;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Rivals;
using ktsu.CounterStrikeProgression.Core.Weapons;

/// <summary>
/// One row of the leaderboard.
/// </summary>
/// <param name="Name">The player's last known name.</param>
/// <param name="RankXp">Their rank XP.</param>
/// <param name="Rank">Their rank index.</param>
internal sealed record LeaderboardEntry(string Name, long RankXp, int Rank);

/// <summary>
/// One row of a weapon's leaderboard.
/// </summary>
/// <param name="Name">The player's last known name.</param>
/// <param name="Xp">Their XP with the weapon.</param>
/// <param name="Level">Their stored level with the weapon.</param>
internal sealed record WeaponLeaderboardEntry(string Name, long Xp, int Level);

/// <summary>
/// One row of the server rankings.
/// </summary>
/// <param name="SteamId">The 64-bit Steam ID.</param>
/// <param name="Name">The player's last known name.</param>
/// <param name="RankXp">Their rank XP.</param>
/// <param name="Rank">Their rank index.</param>
/// <param name="LastSeen">When they last played, in UTC.</param>
internal sealed record RankingEntry(ulong SteamId, string Name, long RankXp, int Rank, DateTime LastSeen);

/// <summary>
/// One row of a season's standings.
/// </summary>
/// <param name="SteamId">The 64-bit Steam ID.</param>
/// <param name="Name">The player's last known name.</param>
/// <param name="Rank">Their lifetime rank index.</param>
/// <param name="Xp">The rank XP they earned in the season.</param>
internal sealed record SeasonStanding(ulong SteamId, string Name, int Rank, long Xp);

/// <summary>
/// Where a player finished in one season.
/// </summary>
/// <param name="Start">The season's first day, at midnight UTC.</param>
/// <param name="Xp">The rank XP they earned in the season.</param>
/// <param name="Place">Their place in the season, from 1. Players on the same XP share a place.</param>
/// <param name="Players">How many players earned XP in the season.</param>
internal sealed record SeasonResult(DateTime Start, long Xp, long Place, long Players);

/// <summary>
/// One player's rank XP at the start and end of a span of days, such as last week for the weekly digest.
/// </summary>
/// <param name="SteamId">The 64-bit Steam ID.</param>
/// <param name="Name">The player's last known name.</param>
/// <param name="StartXp">Their rank XP just before the first day, measured as <see cref="XpHistory.Baseline"/> does.</param>
/// <param name="EndXp">Their rank XP at the end of the last day.</param>
internal sealed record PeriodGain(ulong SteamId, string Name, long StartXp, long EndXp)
{
	/// <summary>Gets the rank XP earned over the span.</summary>
	public long Gained => EndXp - StartXp;
}

/// <summary>
/// Loads and saves player progress. Calls run off the game thread.
/// </summary>
internal interface IProgressionStore
{
	/// <summary>
	/// Creates the tables if they don't exist yet.
	/// </summary>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>A task that completes when the tables exist.</returns>
	public Task InitializeAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Loads a player's progress.
	/// </summary>
	/// <param name="steamId">The 64-bit Steam ID.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The progress, or <see langword="null"/> for a player never seen before.</returns>
	public Task<PlayerProgress?> LoadAsync(ulong steamId, CancellationToken cancellationToken);

	/// <summary>
	/// Saves the rows in a snapshot in one transaction.
	/// </summary>
	/// <param name="snapshot">The changed rows.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>A task that completes when the rows are written.</returns>
	public Task SaveAsync(ProgressSnapshot snapshot, CancellationToken cancellationToken);

	/// <summary>
	/// Deletes a player's weapon and ladder rows and writes their player row, in one transaction. Used by the
	/// admin reset, the only thing that ever removes rows.
	/// </summary>
	/// <param name="player">The player row to keep, with its XP and rank already cleared.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>A task that completes when the rows are gone.</returns>
	public Task ResetAsync(PlayerRow player, CancellationToken cancellationToken);

	/// <summary>
	/// Gets the players with the most rank XP.
	/// </summary>
	/// <param name="count">How many to return.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The leaderboard, highest first.</returns>
	public Task<IReadOnlyList<LeaderboardEntry>> TopAsync(int count, CancellationToken cancellationToken);

	/// <summary>
	/// Gets the players with the most XP with one weapon.
	/// </summary>
	/// <param name="weapon">The weapon.</param>
	/// <param name="count">How many to return.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The leaderboard, highest first.</returns>
	public Task<IReadOnlyList<WeaponLeaderboardEntry>> TopByWeaponAsync(WeaponKey weapon, int count, CancellationToken cancellationToken);

	/// <summary>
	/// Gets every player's standing, ordered by rank XP and then Steam ID.
	/// </summary>
	/// <param name="count">How many to return.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The rankings, highest first.</returns>
	public Task<IReadOnlyList<RankingEntry>> RankingsAsync(int count, CancellationToken cancellationToken);

	/// <summary>
	/// Gets a player's daily rows from a day on, with the latest row before it and their earliest row, so
	/// <see cref="XpHistory"/> can tell what they earned on the first day.
	/// </summary>
	/// <param name="steamId">The 64-bit Steam ID.</param>
	/// <param name="from">The first day wanted.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The rows, earliest first.</returns>
	public Task<IReadOnlyList<DailyXp>> HistoryAsync(ulong steamId, DateOnly from, CancellationToken cancellationToken);

	/// <summary>
	/// Gets the players who earned the most rank XP from a day on, measured as <see cref="XpHistory.GainedSince"/> does.
	/// </summary>
	/// <param name="start">The first day to count.</param>
	/// <param name="count">How many to return.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The players who earned any, most first and then by Steam ID.</returns>
	public Task<IReadOnlyList<XpGainEntry>> GainsSinceAsync(DateOnly start, int count, CancellationToken cancellationToken);

	/// <summary>
	/// Gets the players who earned the most rank XP in one season, ordered by that XP and then Steam ID.
	/// </summary>
	/// <param name="start">The season's first day.</param>
	/// <param name="count">How many to return.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The standings, highest first.</returns>
	public Task<IReadOnlyList<SeasonStanding>> SeasonStandingsAsync(DateOnly start, int count, CancellationToken cancellationToken);

	/// <summary>
	/// Gets a player's record against the humans they've killed or been killed by.
	/// </summary>
	/// <param name="steamId">The 64-bit Steam ID.</param>
	/// <param name="count">How many to return.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>One row per other player, most kills and deaths together first, then by Steam ID.</returns>
	public Task<IReadOnlyList<HeadToHead>> HeadToHeadAsync(ulong steamId, int count, CancellationToken cancellationToken);

	/// <summary>
	/// Gets where a player finished in every season they earned XP in.
	/// </summary>
	/// <param name="steamId">The 64-bit Steam ID.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The results, latest season first.</returns>
	public Task<IReadOnlyList<SeasonResult>> SeasonResultsAsync(ulong steamId, CancellationToken cancellationToken);

	/// <summary>
	/// Gets the players who earned the most rank XP on one map, ordered by that XP and then Steam ID.
	/// </summary>
	/// <param name="map">The map's stored name.</param>
	/// <param name="count">How many to return.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The board, highest first.</returns>
	public Task<IReadOnlyList<MapStanding>> MapStandingsAsync(string map, int count, CancellationToken cancellationToken);

	/// <summary>
	/// Gets every map anyone has earned XP on, with everyone's totals.
	/// </summary>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The maps, most played first and then by name.</returns>
	public Task<IReadOnlyList<MapSummary>> MapSummariesAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Gets every player who earned rank XP from one day up to, but not including, another.
	/// </summary>
	/// <param name="start">The first day to count.</param>
	/// <param name="end">The day after the last day to count.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The players who earned any, most first and then by Steam ID.</returns>
	public Task<IReadOnlyList<PeriodGain>> GainsBetweenAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken);

	/// <summary>
	/// Records that the weekly digest for a week was posted, unless it already was.
	/// </summary>
	/// <param name="week">The Monday the digest is posted for.</param>
	/// <param name="at">When it's posted, in UTC.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>True if this call claimed the week, false if an earlier one already had.</returns>
	public Task<bool> ClaimDigestAsync(DateOnly week, DateTime at, CancellationToken cancellationToken);
}

// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Storage;

using ktsu.CounterStrikeProgression.Core.Progression;
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
}

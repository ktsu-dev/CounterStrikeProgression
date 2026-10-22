// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Heatmaps;

/// <summary>
/// Keeps kill positions for the heatmaps. Calls run off the game thread. It's separate from
/// <see cref="Storage.IProgressionStore"/>, because kills aren't anyone's progress and are written on their own schedule.
/// </summary>
internal interface IKillMapStore
{
	/// <summary>
	/// Creates the table if it doesn't exist yet.
	/// </summary>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>A task that completes when the table exists.</returns>
	public Task InitializeAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Adds kills, then deletes each of their maps' oldest kills past the caps, in one transaction. Kills with a human on
	/// either end and kills between two bots are counted apart, so neither pushes out the other.
	/// </summary>
	/// <param name="points">The kills.</param>
	/// <param name="maxPerMap">How many kills with a human on either end each map keeps.</param>
	/// <param name="maxBotFightsPerMap">How many kills between two bots each map keeps.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>A task that completes when the kills are written.</returns>
	public Task AddAsync(IReadOnlyList<KillPoint> points, int maxPerMap, int maxBotFightsPerMap, CancellationToken cancellationToken);

	/// <summary>
	/// Gets every map with kills kept, and how many.
	/// </summary>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The maps, most kills first and then by name.</returns>
	public Task<IReadOnlyList<MapPointCount>> MapsAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Gets the latest positions on a map, for the server or one player.
	/// </summary>
	/// <param name="map">The map.</param>
	/// <param name="kind">Whether to take where the killers stood or where the victims fell.</param>
	/// <param name="steamId">The player whose kills or deaths to take, or <see langword="null"/> for everyone's.</param>
	/// <param name="limit">The most positions to return.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The positions, newest first.</returns>
	public Task<IReadOnlyList<HeatPoint>> PointsAsync(string map, HeatmapKind kind, ulong? steamId, int limit, CancellationToken cancellationToken);

	/// <summary>
	/// Gets the newest kills on a map, for the server or the fights one player was in.
	/// </summary>
	/// <param name="map">The map.</param>
	/// <param name="steamId">The player who was the killer or the victim, or <see langword="null"/> for every kill.</param>
	/// <param name="since">The earliest time to take, or <see langword="null"/> for no limit.</param>
	/// <param name="limit">The most kills to return.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The kills, newest first.</returns>
	public Task<IReadOnlyList<KillPoint>> FightsAsync(string map, ulong? steamId, DateTime? since, int limit, CancellationToken cancellationToken);

	/// <summary>
	/// Gets the map of a player's newest kill or death.
	/// </summary>
	/// <param name="steamId">The player.</param>
	/// <param name="cancellationToken">Cancels the operation.</param>
	/// <returns>The map, or <see langword="null"/> if the player has no kills kept.</returns>
	public Task<string?> LastMapAsync(ulong steamId, CancellationToken cancellationToken);
}

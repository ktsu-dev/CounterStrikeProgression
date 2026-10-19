// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Data;

using ktsu.CounterStrikeProgression.Core.Maps;

/// <summary>
/// The map board reads, cached and limited like the others.
/// </summary>
internal sealed partial class ProgressReader
{
	private readonly SharedCache<bool, IReadOnlyList<MapSummary>> mapSummaries = new(1);
	private readonly SharedCache<string, IReadOnlyList<MapStanding>> mapStandings = new(MaxCachedPlayers);

	/// <summary>
	/// Gets every map anyone has earned XP on.
	/// </summary>
	/// <param name="cancellationToken">Stops this caller waiting. A query other callers share runs on.</param>
	/// <returns>The maps, most played first.</returns>
	public Task<IReadOnlyList<MapSummary>> MapSummariesAsync(CancellationToken cancellationToken = default) =>
		mapSummaries.GetAsync(true, clock, () => QueryAsync(() => store.MapSummariesAsync(CancellationToken.None)), cancellationToken);

	/// <summary>
	/// Gets the stored board of one map, most XP on it first.
	/// </summary>
	/// <param name="map">The map's stored name.</param>
	/// <param name="cancellationToken">Stops this caller waiting. A query other callers share runs on.</param>
	/// <returns>Up to <see cref="RankingsCount"/> rows.</returns>
	public Task<IReadOnlyList<MapStanding>> MapStandingsAsync(string map, CancellationToken cancellationToken = default) =>
		mapStandings.GetAsync(map, clock, () => QueryAsync(() => store.MapStandingsAsync(map, RankingsCount, CancellationToken.None)), cancellationToken);
}

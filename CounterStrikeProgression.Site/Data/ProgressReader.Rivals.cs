// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Data;

using ktsu.CounterStrikeProgression.Core.Rivals;

/// <summary>
/// The head-to-head reads, cached and limited like the others.
/// </summary>
internal sealed partial class ProgressReader
{
	/// <summary>How many rivals a head-to-head read returns.</summary>
	public const int RivalsCount = 50;

	private readonly SharedCache<ulong, IReadOnlyList<HeadToHead>> headToHeads = new(MaxCachedPlayers);

	/// <summary>
	/// Gets a player's record against the humans and bot personas they've killed or been killed by.
	/// </summary>
	/// <param name="steamId">The 64-bit Steam ID.</param>
	/// <param name="cancellationToken">Stops this caller waiting. A query other callers share runs on.</param>
	/// <returns>Up to <see cref="RivalsCount"/> rows, most kills and deaths together first.</returns>
	public Task<IReadOnlyList<HeadToHead>> HeadToHeadAsync(ulong steamId, CancellationToken cancellationToken = default) =>
		headToHeads.GetAsync(steamId, clock, () => QueryAsync(() => store.HeadToHeadAsync(steamId, RivalsCount, CancellationToken.None)), cancellationToken);
}

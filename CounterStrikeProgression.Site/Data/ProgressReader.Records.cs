// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Data;

using ktsu.CounterStrikeProgression.Core.Records;

/// <summary>
/// The server records read, cached and limited like the others.
/// </summary>
internal sealed partial class ProgressReader
{
	private readonly SharedCache<int, IReadOnlyList<RecordRow>> records = new(1);

	/// <summary>
	/// Gets every stored server record's holder.
	/// </summary>
	/// <param name="cancellationToken">Stops this caller waiting. A query other callers share runs on.</param>
	/// <returns>One row per record anyone holds.</returns>
	public Task<IReadOnlyList<RecordRow>> RecordsAsync(CancellationToken cancellationToken = default) =>
		records.GetAsync(0, clock, () => QueryAsync(() => store.RecordsAsync(CancellationToken.None)), cancellationToken);
}

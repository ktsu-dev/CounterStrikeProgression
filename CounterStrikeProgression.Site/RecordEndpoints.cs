// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site;

using System.Data.Common;
using ktsu.CounterStrikeProgression.Core.Records;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Views;
using Microsoft.AspNetCore.Http;

/// <summary>
/// The server records route handler.
/// </summary>
internal static class RecordEndpoints
{
	/// <summary>
	/// Gets every server record and who holds it.
	/// </summary>
	/// <param name="reader">Reads the stored records.</param>
	/// <param name="cancellationToken">Stops waiting when the request is aborted.</param>
	/// <returns>The records in catalog order, or 503 when the database can't be read.</returns>
	public static async Task<IResult> RecordsAsync(ProgressReader reader, CancellationToken cancellationToken)
	{
		Ensure.NotNull(reader);

		IReadOnlyList<RecordRow> rows;
		try
		{
			rows = await reader.RecordsAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (DbException)
		{
			return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
		}

		return TypedResults.Ok(RecordsViewBuilder.Build(rows));
	}
}

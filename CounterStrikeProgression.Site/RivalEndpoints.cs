// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site;

using System.Data.Common;
using ktsu.CounterStrikeProgression.Core.Rivals;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Live;
using ktsu.CounterStrikeProgression.Site.Views;
using Microsoft.AspNetCore.Http;

/// <summary>
/// The route handler for a player's nemesis, favourite victim, and head-to-head records.
/// </summary>
internal static class RivalEndpoints
{
	/// <summary>
	/// Gets a player's rivals from the stored head-to-head rows.
	/// </summary>
	/// <param name="id">The SteamID64 from the URL.</param>
	/// <param name="reader">Reads the stored rows.</param>
	/// <param name="live">The live state, whose settings decide how many kills make a nemesis.</param>
	/// <param name="cancellationToken">Stops waiting when the request is aborted.</param>
	/// <returns>The rivals, empty for a player with none, 404 for a bad id, or 503 when the database can't be read.</returns>
	public static async Task<IResult> PlayerRivalsAsync(string id, ProgressReader reader, LiveState live, CancellationToken cancellationToken)
	{
		Ensure.NotNull(reader);
		Ensure.NotNull(live);

		if (!ProgressReader.TryParseSteamId(id, out ulong steamId))
		{
			return TypedResults.NotFound();
		}

		IReadOnlyList<HeadToHead> rows;
		try
		{
			rows = await reader.HeadToHeadAsync(steamId, cancellationToken).ConfigureAwait(false);
		}
		catch (DbException)
		{
			return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
		}

		return TypedResults.Ok(RivalsViewBuilder.Build(rows, live.Settings));
	}
}

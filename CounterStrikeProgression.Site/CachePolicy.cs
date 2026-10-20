// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site;

using Microsoft.AspNetCore.Http;

/// <summary>
/// The caching every response gets unless its endpoint sets its own.
/// </summary>
internal static class CachePolicy
{
	/// <summary>
	/// Sends <c>Cache-Control: no-cache</c> unless the endpoint sets its own policy, so browsers check the ETag
	/// before reusing a copy. Without it, browsers guess how long the pages and scripts stay fresh, and a release
	/// can run against a page and script from different versions until that guess runs out.
	/// </summary>
	/// <param name="context">The request.</param>
	/// <param name="next">The rest of the pipeline, which may replace the header.</param>
	/// <returns>A task that completes when the rest of the pipeline has.</returns>
	public static Task RevalidateByDefaultAsync(HttpContext context, RequestDelegate next)
	{
		Ensure.NotNull(context);
		Ensure.NotNull(next);
		context.Response.Headers.CacheControl = "no-cache";
		return next(context);
	}
}

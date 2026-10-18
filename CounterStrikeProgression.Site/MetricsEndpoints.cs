// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site;

using ktsu.CounterStrikeProgression.Site.Live;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The Prometheus metrics: a middleware that counts each ingest answer, and the <c>/metrics</c> route.
/// </summary>
internal static class MetricsEndpoints
{
	/// <summary>
	/// Counts every answer the ingest route gives, and maps the metrics route.
	/// </summary>
	/// <param name="app">The app.</param>
	public static void Map(WebApplication app)
	{
		Ensure.NotNull(app);
		SiteMetrics metrics = app.Services.GetRequiredService<SiteMetrics>();
		app.Use(async (context, next) =>
		{
			await next(context).ConfigureAwait(false);
			if ((context.Connection.LocalPort == Endpoints.IngestPort) && context.Request.Path.StartsWithSegments(Endpoints.IngestRoute, StringComparison.OrdinalIgnoreCase))
			{
				metrics.Record(context.Response.StatusCode);
			}
		});
		app.MapGet(Endpoints.MetricsRoute, Metrics);
	}

	/// <summary>Serves the metrics for Prometheus.</summary>
	/// <param name="metrics">The ingest counters.</param>
	/// <param name="live">The live state.</param>
	/// <returns>The metrics in the Prometheus text format.</returns>
	public static IResult Metrics(SiteMetrics metrics, LiveState live)
	{
		Ensure.NotNull(metrics);
		return TypedResults.Text(metrics.Render(live), SiteMetrics.ContentType);
	}
}

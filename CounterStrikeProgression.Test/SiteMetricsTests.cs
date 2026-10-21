// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using System.Text;
using System.Text.Json;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Heatmaps;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Site;
using ktsu.CounterStrikeProgression.Site.Live;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

[TestClass]
public sealed class SiteMetricsTests
{
	private static readonly DateTime Start = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

	private string directory = string.Empty;

	[TestInitialize]
	public void Initialize()
	{
		directory = Path.Combine(Path.GetTempPath(), "csp-metrics-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
	}

	[TestCleanup]
	public void Cleanup() => Directory.Delete(directory, recursive: true);

	private LiveState NewState(TimeProvider? time = null) =>
		new(new SiteOptions(), new FeedStore(Path.Combine(directory, "feed.json")), time ?? new ManualTime(), NullLogger<LiveState>.Instance);

	private static LiveBatch Batch() =>
		new(new ServerSnapshot(Start, "de_inferno", "Deathmatch", 16, [new LivePlayer(1, TestHelpers.SteamId, "Tester", false, 2, 3, 1, 40)]), [], [], new ProgressionSettings());

	private static DefaultHttpContext IngestContext(string? authorization, string body)
	{
		DefaultHttpContext context = new();
		context.Request.Method = "POST";
		context.Request.Path = "/api/ingest";
		if (authorization is not null)
		{
			context.Request.Headers.Authorization = authorization;
		}

		byte[] bytes = Encoding.UTF8.GetBytes(body);
		context.Request.Body = new MemoryStream(bytes);
		context.Request.ContentLength = bytes.Length;
		return context;
	}

	private static int StatusOf(IResult result) => ((IStatusCodeHttpResult)result).StatusCode ?? 0;

	[TestMethod]
	public void Metrics_BeforeAnyBatch_AgeCountsFromStartAndPluginCountersAreAbsent()
	{
		ManualTime time = new();
		SiteMetrics metrics = new(time);
		time.Advance(TimeSpan.FromSeconds(90));

		string text = metrics.Render(NewState(time));

		Assert.Contains("csp_site_server_online 0\n", text);
		Assert.Contains("csp_site_last_batch_age_seconds 90\n", text);
		Assert.Contains("csp_site_ingest_batches_total 0\n", text);
		Assert.Contains("csp_site_ingest_failures_total{reason=\"unauthorized\"} 0\n", text);
		Assert.DoesNotContain("csp_plugin_", text);
	}

	[TestMethod]
	public async Task Metrics_CountBatchesFailuresPlayersAndPluginStats()
	{
		ManualTime time = new();
		LiveState live = NewState(time);
		SiteMetrics metrics = new(time);
		SiteOptions options = new() { Token = "dev" };
		MatchFights fights = new(new InMemoryKillMapStore(), NullLogger<MatchFights>.Instance);
		LiveBatch batch = Batch() with
		{
			Snapshot = Batch().Snapshot with { Players = [new LivePlayer(1, TestHelpers.SteamId, "Tester", false, 2, 3, 1, 40), new LivePlayer(2, 0, "Bot", true, 0, 0, 0, 0)] },
			Stats = new PluginStats(SendFailures: 3, BatchesDropped: 1, SaveFailures: 2),
		};

		metrics.Record(StatusOf(await IngestEndpoints.IngestAsync(IngestContext("Bearer dev", JsonSerializer.Serialize(batch, LiveJson.Options)), options, live, new Broadcaster(), new FeatIndex(), fights).ConfigureAwait(false)));
		metrics.Record(StatusOf(await IngestEndpoints.IngestAsync(IngestContext(null, "{}"), options, live, new Broadcaster(), new FeatIndex(), fights).ConfigureAwait(false)));
		metrics.Record(StatusOf(await IngestEndpoints.IngestAsync(IngestContext("Bearer dev", "not json"), options, live, new Broadcaster(), new FeatIndex(), fights).ConfigureAwait(false)));
		metrics.Record(StatusCodes.Status499ClientClosedRequest);
		time.Advance(TimeSpan.FromSeconds(5));

		string text = metrics.Render(live);

		Assert.Contains("csp_site_server_online 1\n", text);
		Assert.Contains("csp_site_players_online{kind=\"human\"} 1\n", text);
		Assert.Contains("csp_site_players_online{kind=\"bot\"} 1\n", text);
		Assert.Contains("csp_site_last_batch_age_seconds 5\n", text);
		Assert.Contains("csp_site_ingest_batches_total 1\n", text);
		Assert.Contains("csp_site_ingest_failures_total{reason=\"unauthorized\"} 1\n", text);
		Assert.Contains("csp_site_ingest_failures_total{reason=\"bad_request\"} 1\n", text);
		Assert.Contains("csp_site_ingest_failures_total{reason=\"too_large\"} 0\n", text);
		Assert.Contains("csp_plugin_send_failures_total 3\n", text);
		Assert.Contains("csp_plugin_batches_dropped_total 1\n", text);
		Assert.Contains("csp_plugin_save_failures_total 2\n", text);
	}

	[TestMethod]
	[DataRow(8081, "/api/ingest", StatusCodes.Status204NoContent, "csp_site_ingest_batches_total 1\n")]
	[DataRow(8081, "/api/ingest", StatusCodes.Status401Unauthorized, "csp_site_ingest_failures_total{reason=\"unauthorized\"} 1\n")]
	[DataRow(8081, "/api/ingest", StatusCodes.Status413PayloadTooLarge, "csp_site_ingest_failures_total{reason=\"too_large\"} 1\n")]
	[DataRow(8081, "/api/ingest", StatusCodes.Status400BadRequest, "csp_site_ingest_failures_total{reason=\"bad_request\"} 1\n")]
	[DataRow(8080, "/api/ingest", StatusCodes.Status204NoContent, "csp_site_ingest_batches_total 0\n")]
	[DataRow(8081, "/metrics", StatusCodes.Status204NoContent, "csp_site_ingest_batches_total 0\n")]
	public async Task CountIngest_CountsOnlyIngestAnswersOnTheIngestPort(int port, string path, int status, string expected)
	{
		SiteMetrics metrics = new(new ManualTime());
		DefaultHttpContext context = new();
		context.Connection.LocalPort = port;
		context.Request.Path = path;
		bool ran = false;

		await MetricsEndpoints.CountIngestAsync(
			context,
			c =>
			{
				ran = true;
				c.Response.StatusCode = status;
				return Task.CompletedTask;
			},
			metrics).ConfigureAwait(false);

		Assert.IsTrue(ran);
		Assert.Contains(expected, metrics.Render(NewState()));
	}

	[TestMethod]
	public void MetricsEndpoint_ServesTheTextFormat()
	{
		IResult result = MetricsEndpoints.Metrics(new SiteMetrics(new ManualTime()), NewState());

		ContentHttpResult text = (ContentHttpResult)result;
		Assert.AreEqual(SiteMetrics.ContentType, text.ContentType);
		Assert.Contains("# TYPE csp_site_server_online gauge\n", text.ResponseContent ?? string.Empty);
	}

	[TestMethod]
	public void Metrics_KeepPluginStatsFromABatchWithout()
	{
		LiveState live = NewState();
		live.Apply(Batch() with { Stats = new PluginStats(4, 0, 0) });
		live.Apply(Batch());

		Assert.AreEqual(4, live.PluginStats?.SendFailures);
	}
}

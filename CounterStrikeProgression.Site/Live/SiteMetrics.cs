// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Live;

using System.Globalization;
using System.Text;
using ktsu.CounterStrikeProgression.Core.Live;
using Microsoft.AspNetCore.Http;

/// <summary>Why the ingest endpoint turned a batch away.</summary>
internal enum IngestFailure
{
	Unauthorized = 0,
	TooLarge,
	BadRequest,
}

/// <summary>
/// Counts what the ingest endpoint takes and turns away, and renders those counts with the live state in the
/// Prometheus text format for <c>/metrics</c>.
/// </summary>
/// <param name="time">The clock that ages the last batch.</param>
internal sealed class SiteMetrics(TimeProvider time)
{
	/// <summary>The content type Prometheus expects for the text format.</summary>
	public const string ContentType = "text/plain; version=0.0.4; charset=utf-8";

	private static readonly IngestFailure[] FailureReasons = Enum.GetValues<IngestFailure>();

	private readonly DateTimeOffset startedAt = time.GetUtcNow();
	private readonly long[] failures = new long[FailureReasons.Length];
	private long batches;

	/// <summary>
	/// Counts one answer from the ingest endpoint: 204 is an applied batch, and 401, 413, and 400 are batches
	/// turned away. Anything else, such as a client that hung up, isn't counted.
	/// </summary>
	/// <param name="statusCode">The status code the endpoint answered with.</param>
	public void Record(int statusCode)
	{
		if (statusCode == StatusCodes.Status204NoContent)
		{
			Interlocked.Increment(ref batches);
		}
		else if (Reason(statusCode) is { } reason)
		{
			Interlocked.Increment(ref failures[(int)reason]);
		}
	}

	/// <summary>
	/// Renders every metric.
	/// </summary>
	/// <param name="live">The live state, for the server and the plugin's counters.</param>
	/// <returns>The metrics in the Prometheus text format.</returns>
	public string Render(LiveState live)
	{
		Ensure.NotNull(live);

		ServerSnapshot? snapshot = live.Snapshot;
		DateTimeOffset since = live.LastBatchAt ?? startedAt;
		double age = Math.Max(0, (time.GetUtcNow() - since).TotalSeconds);
		int humans = snapshot?.Players.Count(p => !p.IsBot) ?? 0;
		int bots = snapshot?.Players.Count(p => p.IsBot) ?? 0;

		StringBuilder text = new();
		Gauge(text, "csp_site_server_online", "Whether the plugin sent a batch within the offline window.", live.IsOnline ? 1 : 0);
		Header(text, "csp_site_players_online", "gauge", "Players on the server in the last batch.");
		Sample(text, "csp_site_players_online{kind=\"human\"}", humans);
		Sample(text, "csp_site_players_online{kind=\"bot\"}", bots);
		Gauge(text, "csp_site_last_batch_age_seconds", "Seconds since the last batch, or since the site started when none has arrived.", age);
		Counter(text, "csp_site_ingest_batches_total", "Batches the ingest endpoint applied.", Interlocked.Read(ref batches));
		Header(text, "csp_site_ingest_failures_total", "counter", "Batches the ingest endpoint turned away, by reason.");
		foreach (IngestFailure reason in FailureReasons)
		{
			Sample(text, $"csp_site_ingest_failures_total{{reason=\"{Label(reason)}\"}}", Interlocked.Read(ref failures[(int)reason]));
		}

		if (live.PluginStats is { } stats)
		{
			Counter(text, "csp_plugin_send_failures_total", "Batches the plugin failed to send, as of its last batch.", stats.SendFailures);
			Counter(text, "csp_plugin_batches_dropped_total", "Batches the plugin dropped because its queue was full, as of its last batch.", stats.BatchesDropped);
			Counter(text, "csp_plugin_save_failures_total", "Saves that failed every retry in the plugin, as of its last batch.", stats.SaveFailures);
		}

		return text.ToString();
	}

	private static IngestFailure? Reason(int statusCode) => statusCode switch
	{
		StatusCodes.Status401Unauthorized => IngestFailure.Unauthorized,
		StatusCodes.Status413PayloadTooLarge => IngestFailure.TooLarge,
		StatusCodes.Status400BadRequest => IngestFailure.BadRequest,
		_ => null,
	};

	private static string Label(IngestFailure reason) => reason switch
	{
		IngestFailure.Unauthorized => "unauthorized",
		IngestFailure.TooLarge => "too_large",
		IngestFailure.BadRequest => "bad_request",
		_ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
	};

	private static void Gauge(StringBuilder text, string name, string help, double value)
	{
		Header(text, name, "gauge", help);
		Sample(text, name, value);
	}

	private static void Counter(StringBuilder text, string name, string help, long value)
	{
		Header(text, name, "counter", help);
		Sample(text, name, value);
	}

	private static void Header(StringBuilder text, string name, string type, string help) =>
		text.Append(CultureInfo.InvariantCulture, $"# HELP {name} {help}\n# TYPE {name} {type}\n");

	private static void Sample(StringBuilder text, string series, double value) =>
		text.Append(CultureInfo.InvariantCulture, $"{series} {value.ToString("R", CultureInfo.InvariantCulture)}\n");
}

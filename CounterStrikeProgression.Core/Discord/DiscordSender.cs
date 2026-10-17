// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Discord;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

/// <summary>
/// Posts lines to a Discord webhook from the thread pool, without ever blocking the caller.
/// </summary>
/// <remarks>
/// Lines that pile up are joined into one post of up to Discord's 2,000 characters, a post Discord rate limits is
/// retried after the wait it asks for, and the queue drops its oldest line when full, so a slow or dead webhook
/// costs the game nothing.
/// </remarks>
internal sealed class DiscordSender : IDisposable
{
	/// <summary>The most characters Discord takes in one message.</summary>
	public const int MaxContentLength = 2000;

	private const int MaxAttempts = 3;

	private static readonly TimeSpan FailureReportInterval = TimeSpan.FromMinutes(1);
	private static readonly TimeSpan LongestRetryWait = TimeSpan.FromSeconds(30);
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	private readonly Uri webhook;
	private readonly string? username;
	private readonly TimeSpan postGap;
	private readonly Action<Exception> onFailure;
	private readonly TimeProvider time;
	private readonly HttpClient client;
	private readonly Channel<string> queue;
	private readonly CancellationTokenSource shutdown = new();
	private readonly CancellationToken stopping;
	private DateTimeOffset? lastFailureReport;

	/// <summary>
	/// Creates a sender and starts its reader loop.
	/// </summary>
	/// <param name="webhookUrl">The webhook URL.</param>
	/// <param name="name">The name posts appear under, or empty for the webhook's own.</param>
	/// <param name="queueLimit">How many lines wait before the oldest is dropped.</param>
	/// <param name="timeout">How long one request may take.</param>
	/// <param name="gap">How long to wait after each post, which keeps a burst under Discord's rate limit.</param>
	/// <param name="failureCallback">Called with a failure, at most once a minute.</param>
	/// <param name="handler">The HTTP handler, or null for the default.</param>
	/// <param name="clock">The clock for waits and the failure rate limit, or null for the system clock.</param>
	public DiscordSender(Uri webhookUrl, string name, int queueLimit, TimeSpan timeout, TimeSpan gap, Action<Exception> failureCallback, HttpMessageHandler? handler = null, TimeProvider? clock = null)
	{
		Ensure.NotNull(webhookUrl);
		Ensure.NotNull(failureCallback);

		webhook = webhookUrl;
		username = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
		postGap = gap;
		onFailure = failureCallback;
		time = clock ?? TimeProvider.System;
		stopping = shutdown.Token;

		client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
		client.Timeout = timeout;

		queue = Channel.CreateBounded<string>(new BoundedChannelOptions(Math.Max(1, queueLimit))
		{
			FullMode = BoundedChannelFullMode.DropOldest,
			SingleReader = true,
		});
		_ = Task.Run(ReadLoopAsync);
	}

	/// <summary>
	/// Parses a webhook URL from the config.
	/// </summary>
	/// <param name="url">The configured URL.</param>
	/// <returns>The URL, or <see langword="null"/> when it isn't an absolute https URL.</returns>
	public static Uri? ParseUrl(string? url) =>
		Uri.TryCreate(url?.Trim(), UriKind.Absolute, out Uri? parsed) && (parsed.Scheme == Uri.UriSchemeHttps) ? parsed : null;

	/// <summary>
	/// Queues a line without waiting. A line longer than one message is cut short.
	/// </summary>
	/// <param name="line">The line to post.</param>
	/// <returns>True if the line was queued, false if the sender has stopped.</returns>
	public bool TryEnqueue(string line)
	{
		Ensure.NotNull(line);
		return queue.Writer.TryWrite(line.Length > MaxContentLength ? line[..MaxContentLength] : line);
	}

	/// <summary>
	/// Stops the sender and releases its resources. Lines still queued are dropped.
	/// </summary>
	/// <remarks>
	/// The source is cancelled before it is disposed, so the reader only ever sees a cancelled token, which it can
	/// still read after disposal.
	/// </remarks>
	public void Dispose()
	{
		queue.Writer.TryComplete();
		shutdown.Cancel();
		shutdown.Dispose();
		client.Dispose();
	}

	[SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The reader runs unobserved on the thread pool, so a post failure must be reported here or it vanishes, and nothing may escape onto the pool.")]
	private async Task ReadLoopAsync()
	{
		string? carried = null;
		try
		{
			while (!stopping.IsCancellationRequested)
			{
				string first = carried ?? await queue.Reader.ReadAsync(stopping).ConfigureAwait(false);
				carried = null;

				// Join whatever else is already waiting, up to one message.
				StringBuilder content = new(first);
				while (queue.Reader.TryRead(out string? next))
				{
					if (content.Length + 1 + next.Length > MaxContentLength)
					{
						carried = next;
						break;
					}

					content.Append('\n').Append(next);
				}

				try
				{
					await PostAsync(content.ToString()).ConfigureAwait(false);
				}
				catch (Exception) when (stopping.IsCancellationRequested)
				{
					return;
				}
				catch (Exception exception)
				{
					Report(exception);
				}

				if (postGap > TimeSpan.Zero)
				{
					await Task.Delay(postGap, time, stopping).ConfigureAwait(false);
				}
			}
		}
		catch (ChannelClosedException)
		{
			// The sender was disposed and the queue is empty.
		}
		catch (Exception) when (stopping.IsCancellationRequested)
		{
			// Shutdown ended a wait, or a disposed member threw during it.
		}
	}

	private async Task PostAsync(string content)
	{
		WebhookMessage message = new(content, username, new AllowedMentions([]));
		int attempt = 1;
		while (true)
		{
			using HttpResponseMessage response = await client.PostAsJsonAsync(webhook, message, JsonOptions, stopping).ConfigureAwait(false);
			if ((response.StatusCode != HttpStatusCode.TooManyRequests) || (attempt >= MaxAttempts))
			{
				response.EnsureSuccessStatusCode();
				return;
			}

			attempt++;
			await Task.Delay(RetryWait(response), time, stopping).ConfigureAwait(false);
		}
	}

	private static TimeSpan RetryWait(HttpResponseMessage response)
	{
		TimeSpan wait = TimeSpan.FromSeconds(1);
		if (response.Headers.RetryAfter?.Delta is { } delta)
		{
			wait = delta;
		}
		else if (response.Headers.TryGetValues("Retry-After", out IEnumerable<string>? values)
			&& double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
		{
			wait = TimeSpan.FromSeconds(seconds);
		}

		return TimeSpan.FromTicks(Math.Clamp(wait.Ticks, 0, LongestRetryWait.Ticks));
	}

	[SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A faulty failure callback must not stop the reader loop.")]
	private void Report(Exception exception)
	{
		DateTimeOffset now = time.GetUtcNow();
		if (lastFailureReport is { } last && now - last < FailureReportInterval)
		{
			return;
		}

		lastFailureReport = now;
		try
		{
			onFailure(exception);
		}
		catch (Exception)
		{
			// The callback is outside this class's control, and a throw must not end the loop.
		}
	}

	/// <summary>The body Discord's execute-webhook endpoint takes.</summary>
	/// <param name="Content">The message text.</param>
	/// <param name="Username">The name the post appears under, or null for the webhook's own.</param>
	/// <param name="AllowedMentions">Who the post may ping.</param>
	internal sealed record WebhookMessage(string Content, string? Username, AllowedMentions AllowedMentions);

	/// <summary>Which mentions Discord acts on. An empty list pings no one, whatever a player name holds.</summary>
	/// <param name="Parse">The kinds of mention to act on.</param>
	internal sealed record AllowedMentions(IReadOnlyList<string> Parse);
}

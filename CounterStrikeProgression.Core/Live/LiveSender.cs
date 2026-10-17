// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Live;

using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Channels;

/// <summary>
/// Posts live batches to the site from the thread pool, without ever blocking the caller.
/// </summary>
/// <remarks>
/// The queue is bounded and drops its oldest batch when full, so a slow or dead site costs the game nothing but old data.
/// </remarks>
internal sealed class LiveSender : IDisposable
{
	private static readonly TimeSpan FailureReportInterval = TimeSpan.FromMinutes(1);

	private readonly Uri endpoint;
	private readonly Action<Exception> onFailure;
	private readonly TimeProvider time;
	private readonly HttpClient client;
	private readonly Channel<LiveBatch> queue;
	[SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The source has no timer, and the reader and StopAsync may still touch it after Dispose, where a disposed source would throw.")]
	private readonly CancellationTokenSource shutdown = new();
	private readonly CancellationToken stopping;
	private readonly Task reader;
	private DateTimeOffset? lastFailureReport;

	/// <summary>
	/// Creates a sender and starts its reader loop.
	/// </summary>
	/// <param name="ingestUrl">The site's ingest URL.</param>
	/// <param name="token">The bearer token the site expects.</param>
	/// <param name="queueLimit">How many batches wait before the oldest is dropped.</param>
	/// <param name="timeout">How long one request may take.</param>
	/// <param name="failureCallback">Called with a failure, at most once a minute.</param>
	/// <param name="handler">The HTTP handler, or null for the default.</param>
	/// <param name="clock">The clock for the failure rate limit, or null for the system clock.</param>
	public LiveSender(Uri ingestUrl, string token, int queueLimit, TimeSpan timeout, Action<Exception> failureCallback, HttpMessageHandler? handler = null, TimeProvider? clock = null)
	{
		Ensure.NotNull(ingestUrl);
		Ensure.NotNull(token);
		Ensure.NotNull(failureCallback);

		endpoint = ingestUrl;
		onFailure = failureCallback;
		time = clock ?? TimeProvider.System;
		stopping = shutdown.Token;

		client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
		client.Timeout = timeout;
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

		queue = Channel.CreateBounded<LiveBatch>(new BoundedChannelOptions(queueLimit)
		{
			FullMode = BoundedChannelFullMode.DropOldest,
			SingleReader = true,
		});
		reader = Task.Run(ReadLoopAsync);
	}

	/// <summary>
	/// Queues a batch without waiting.
	/// </summary>
	/// <param name="batch">The batch to send.</param>
	/// <returns>True if the batch was queued, false if the sender has stopped.</returns>
	public bool TryEnqueue(LiveBatch batch)
	{
		Ensure.NotNull(batch);
		return queue.Writer.TryWrite(batch);
	}

	/// <summary>
	/// Stops accepting batches and waits for the queued ones to send.
	/// </summary>
	/// <param name="wait">The longest to wait. Batches still queued after that are abandoned.</param>
	/// <returns>A task that completes when the queue drains or the wait ends.</returns>
	public async Task StopAsync(TimeSpan wait)
	{
		queue.Writer.TryComplete();
		using CancellationTokenSource limit = new();
		Task expired = Task.Delay(wait, time, limit.Token);
		if (await Task.WhenAny(reader, expired).ConfigureAwait(false) == reader)
		{
			await limit.CancelAsync().ConfigureAwait(false);
			return;
		}

		await shutdown.CancelAsync().ConfigureAwait(false);
	}

	/// <summary>
	/// Stops the sender and releases its resources.
	/// The cancellation source is left undisposed, because the reader may still read its token and the source holds no timer.
	/// </summary>
	public void Dispose()
	{
		queue.Writer.TryComplete();
		shutdown.Cancel();
		client.Dispose();
	}

	[SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The reader runs unobserved on the thread pool, so a send failure must be reported here or it vanishes, and nothing may escape onto the pool.")]
	private async Task ReadLoopAsync()
	{
		try
		{
			await foreach (LiveBatch batch in queue.Reader.ReadAllAsync(stopping).ConfigureAwait(false))
			{
				if (stopping.IsCancellationRequested)
				{
					return;
				}

				try
				{
					using HttpResponseMessage response = await client.PostAsJsonAsync(endpoint, batch, LiveJson.Options, stopping).ConfigureAwait(false);
					response.EnsureSuccessStatusCode();
				}
				catch (Exception) when (stopping.IsCancellationRequested)
				{
					return;
				}
				catch (Exception exception)
				{
					Report(exception);
				}
			}
		}
		catch (Exception) when (stopping.IsCancellationRequested)
		{
			// Shutdown ended the wait for the next batch, or a disposed member threw during it.
		}
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
}

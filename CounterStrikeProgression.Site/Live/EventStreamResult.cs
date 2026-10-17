// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Live;

using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

/// <summary>
/// Streams live events to one browser as server-sent events until the browser disconnects or the site stops.
/// </summary>
/// <param name="live">The live state, for the frames sent on connect.</param>
/// <param name="broadcaster">Where the events come from.</param>
/// <param name="stopping">Ends the stream when the site shuts down.</param>
internal sealed class EventStreamResult(LiveState live, Broadcaster broadcaster, CancellationToken stopping) : IResult
{
	/// <summary>How often a comment goes out on a quiet stream, so proxies keep the connection open.</summary>
	public static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(15);

	/// <inheritdoc/>
	public async Task ExecuteAsync(HttpContext httpContext)
	{
		Ensure.NotNull(httpContext);

		using CancellationTokenSource connection = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted, stopping);
		CancellationToken cancellationToken = connection.Token;
		HttpResponse response = httpContext.Response;
		response.ContentType = "text/event-stream";
		response.Headers.CacheControl = "no-cache";
		response.Headers["X-Accel-Buffering"] = "no";
		httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

		ChannelReader<string> reader = broadcaster.Subscribe(cancellationToken);
		try
		{
			await response.WriteAsync("retry: 3000\n\n", cancellationToken).ConfigureAwait(false);
			await response.WriteAsync(Broadcaster.Frame("online", live.IsOnline), cancellationToken).ConfigureAwait(false);
			if (live.Snapshot is { } snapshot)
			{
				await response.WriteAsync(Broadcaster.Frame("snapshot", snapshot), cancellationToken).ConfigureAwait(false);
			}

			await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

			while (await WaitAsync(reader, cancellationToken).ConfigureAwait(false) is { } ready)
			{
				if (!ready)
				{
					await response.WriteAsync(": ping\n\n", cancellationToken).ConfigureAwait(false);
				}

				while (reader.TryRead(out string? frame))
				{
					await response.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
				}

				await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (IOException) when (httpContext.RequestAborted.IsCancellationRequested)
		{
		}
		finally
		{
			await connection.CancelAsync().ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Waits for messages, giving up after <see cref="PingEvery"/>.
	/// </summary>
	/// <returns><see langword="true"/> when messages are ready, <see langword="false"/> when it's time for a ping, or <see langword="null"/> when the stream is over.</returns>
	private static async Task<bool?> WaitAsync(ChannelReader<string> reader, CancellationToken cancellationToken)
	{
		using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		wait.CancelAfter(PingEvery);
		try
		{
			return await reader.WaitToReadAsync(wait.Token).ConfigureAwait(false) ? true : null;
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return false;
		}
	}
}

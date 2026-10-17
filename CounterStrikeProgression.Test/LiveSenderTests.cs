// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Live;

[TestClass]
public sealed class LiveSenderTests
{
	private static readonly Uri Endpoint = new("http://site.test:8081/ingest");
	private static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);
	private static readonly string[] Expected = ["A", "C", "D"];

	private sealed class StubHandler(Func<HttpRequestMessage, string, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
	{
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			return await respond(request, body, cancellationToken).ConfigureAwait(false);
		}
	}

	private sealed class ManualTime : TimeProvider
	{
		private DateTimeOffset now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

		public override DateTimeOffset GetUtcNow() => now;

		public void Advance(TimeSpan by) => now += by;
	}

	private static LiveBatch Batch(string map) => new(
		new ServerSnapshot(new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc), map, "Casual", 10, []),
		[],
		[],
		new ProgressionSettings());

	private static HttpResponseMessage Ok() => new(HttpStatusCode.OK);

	[TestMethod]
	public async Task Send_PostsJsonWithBearerToken()
	{
		TaskCompletionSource<(HttpMethod Method, Uri? Uri, string? Auth, string Body)> seen = new(TaskCreationOptions.RunContinuationsAsynchronously);
		using StubHandler handler = new((request, body, _) =>
		{
			seen.TrySetResult((request.Method, request.RequestUri, request.Headers.Authorization?.ToString(), body));
			return Task.FromResult(Ok());
		});
		using LiveSender sender = new(Endpoint, "secret", 4, TimeSpan.FromSeconds(5), _ => { }, handler);

		Assert.IsTrue(sender.TryEnqueue(Batch("de_dust2")));

		(HttpMethod method, Uri? uri, string? auth, string body) = await seen.Task.WaitAsync(Generous).ConfigureAwait(false);
		Assert.AreEqual(HttpMethod.Post, method);
		Assert.AreEqual(Endpoint, uri);
		Assert.AreEqual("Bearer secret", auth);
		LiveBatch? back = JsonSerializer.Deserialize<LiveBatch>(body, LiveJson.Options);
		Assert.IsNotNull(back);
		Assert.AreEqual("de_dust2", back.Snapshot.Map);
	}

	[TestMethod]
	public void TryEnqueue_ReturnsImmediately_WhenTheSiteHangs()
	{
		using StubHandler handler = new(async (_, _, token) =>
		{
			await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
			return Ok();
		});
		using LiveSender sender = new(Endpoint, "secret", 4, TimeSpan.FromSeconds(30), _ => { }, handler);

		Stopwatch watch = Stopwatch.StartNew();
		for (int i = 0; i < 20; i++)
		{
			sender.TryEnqueue(Batch($"map{i}"));
		}

		watch.Stop();
		Assert.IsLessThan(100, watch.ElapsedMilliseconds);
	}

	[TestMethod]
	public async Task FullQueue_DropsOldest()
	{
		TaskCompletionSource firstArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		List<string> received = [];
		using StubHandler handler = new(async (_, body, _) =>
		{
			LiveBatch? batch = JsonSerializer.Deserialize<LiveBatch>(body, LiveJson.Options);
			lock (received)
			{
				received.Add(batch!.Snapshot.Map);
			}

			firstArrived.TrySetResult();
			await release.Task.ConfigureAwait(false);
			return Ok();
		});
		using LiveSender sender = new(Endpoint, "secret", 2, TimeSpan.FromSeconds(30), _ => { }, handler);

		sender.TryEnqueue(Batch("A"));
		await firstArrived.Task.WaitAsync(Generous).ConfigureAwait(false);
		sender.TryEnqueue(Batch("B"));
		sender.TryEnqueue(Batch("C"));
		sender.TryEnqueue(Batch("D"));
		release.SetResult();
		await sender.StopAsync(Generous).ConfigureAwait(false);

		lock (received)
		{
			CollectionAssert.AreEqual(Expected, received);
		}
	}

	[TestMethod]
	public async Task HungRequest_TimesOutAndReportsFailure()
	{
		TaskCompletionSource<Exception> failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
		using StubHandler handler = new(async (_, _, token) =>
		{
			await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
			return Ok();
		});
		using LiveSender sender = new(Endpoint, "secret", 4, TimeSpan.FromMilliseconds(200), exception => failure.TrySetResult(exception), handler);

		sender.TryEnqueue(Batch("A"));

		Exception reported = await failure.Task.WaitAsync(Generous).ConfigureAwait(false);
		Assert.IsInstanceOfType<OperationCanceledException>(reported);
	}

	[TestMethod]
	public async Task Failures_AreReportedAtMostOncePerMinute()
	{
		int reports = 0;
		ManualTime time = new();
		using StubHandler handler = new((_, _, _) => throw new HttpRequestException("down"));
		using LiveSender sender = new(Endpoint, "secret", 16, TimeSpan.FromSeconds(5), _ => Interlocked.Increment(ref reports), handler, time);

		for (int i = 0; i < 5; i++)
		{
			sender.TryEnqueue(Batch($"map{i}"));
		}

		await sender.StopAsync(Generous).ConfigureAwait(false);

		Assert.AreEqual(1, Volatile.Read(ref reports));
	}

	[TestMethod]
	public async Task Failures_ReportAgainAfterAMinute()
	{
		int reports = 0;
		TaskCompletionSource<bool> second = new(TaskCreationOptions.RunContinuationsAsynchronously);
		ManualTime time = new();
		using StubHandler handler = new((_, _, _) => throw new HttpRequestException("down"));
		using LiveSender sender = new(Endpoint, "secret", 16, TimeSpan.FromSeconds(5), _ =>
		{
			if (Interlocked.Increment(ref reports) == 2)
			{
				second.TrySetResult(true);
			}
		}, handler, time);

		sender.TryEnqueue(Batch("A"));
		while (Volatile.Read(ref reports) < 1)
		{
			await Task.Delay(5).ConfigureAwait(false);
		}

		time.Advance(TimeSpan.FromSeconds(61));
		sender.TryEnqueue(Batch("B"));

		Assert.IsTrue(await second.Task.WaitAsync(Generous).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task TryEnqueue_ReturnsFalse_AfterStop()
	{
		using StubHandler handler = new((_, _, _) => Task.FromResult(Ok()));
		using LiveSender sender = new(Endpoint, "secret", 4, TimeSpan.FromSeconds(5), _ => { }, handler);

		await sender.StopAsync(Generous).ConfigureAwait(false);

		Assert.IsFalse(sender.TryEnqueue(Batch("A")));
	}

	[TestMethod]
	public async Task Dispose_WithQueuedBatchesAndASendInFlight_NeverReportsFailure()
	{
		int reports = 0;
		TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
		using StubHandler handler = new(async (_, _, token) =>
		{
			arrived.TrySetResult();
			try
			{
				await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
			}
			finally
			{
				cancelled.TrySetResult();
			}

			return Ok();
		});
		LiveSender sender = new(Endpoint, "secret", 8, TimeSpan.FromSeconds(30), _ => Interlocked.Increment(ref reports), handler);

		sender.TryEnqueue(Batch("A"));
		await arrived.Task.WaitAsync(Generous).ConfigureAwait(false);
		sender.TryEnqueue(Batch("B"));
		sender.TryEnqueue(Batch("C"));
		sender.Dispose();
		await cancelled.Task.WaitAsync(Generous).ConfigureAwait(false);
		await sender.StopAsync(Generous).ConfigureAwait(false);
		sender.Dispose();

		Assert.AreEqual(0, Volatile.Read(ref reports));
	}

	[TestMethod]
	public async Task Dispose_Immediately_NeverReportsFailure()
	{
		int reports = 0;
		using StubHandler handler = new((_, _, _) => Task.FromResult(Ok()));
		LiveSender sender = new(Endpoint, "secret", 8, TimeSpan.FromSeconds(30), _ => Interlocked.Increment(ref reports), handler);

		sender.TryEnqueue(Batch("A"));
		sender.Dispose();
		await sender.StopAsync(Generous).ConfigureAwait(false);

		Assert.AreEqual(0, Volatile.Read(ref reports));
	}
}

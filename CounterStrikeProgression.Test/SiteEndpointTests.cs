// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Core.Weapons;
using ktsu.CounterStrikeProgression.Site;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Live;
using ktsu.CounterStrikeProgression.Site.Views;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

[TestClass]
public sealed class SiteEndpointTests
{
	private static readonly DateTime Start = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

	private string directory = string.Empty;

	[TestInitialize]
	public void Initialize()
	{
		directory = Path.Combine(Path.GetTempPath(), "csp-endpoints-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
	}

	[TestCleanup]
	public void Cleanup() => Directory.Delete(directory, recursive: true);

	private LiveState NewState(TimeProvider? time = null) =>
		new(new SiteOptions(), new FeedStore(Path.Combine(directory, "feed.json")), time ?? new ManualTime(), NullLogger<LiveState>.Instance);

	private static LiveBatch Batch(params FeedEntry[] feed) =>
		new(new ServerSnapshot(Start, "de_inferno", "Deathmatch", 16, [new LivePlayer(1, TestHelpers.SteamId, "Tester", false, 2, 3, 1, 40)]), feed, [], new ProgressionSettings());

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
	public void IngestAuth_RejectsMissingWrongAndEmptyToken()
	{
		Assert.IsFalse(IngestAuth.IsAuthorized(null, "secret"));
		Assert.IsFalse(IngestAuth.IsAuthorized(string.Empty, "secret"));
		Assert.IsFalse(IngestAuth.IsAuthorized("Bearer wrong", "secret"));
		Assert.IsFalse(IngestAuth.IsAuthorized("secret", "secret"));
		Assert.IsFalse(IngestAuth.IsAuthorized("Bearer secret2", "secret"));
		Assert.IsFalse(IngestAuth.IsAuthorized("Bearer ", string.Empty));
		Assert.IsFalse(IngestAuth.IsAuthorized(null, string.Empty));
	}

	[TestMethod]
	public void IngestAuth_AcceptsExactBearerToken() =>
		Assert.IsTrue(IngestAuth.IsAuthorized("Bearer secret", "secret"));

	[TestMethod]
	public void Broadcaster_DeliversToEverySubscriber()
	{
		Broadcaster broadcaster = new();
		using CancellationTokenSource cancel = new();
		ChannelReader<string> first = broadcaster.Subscribe(cancel.Token);
		ChannelReader<string> second = broadcaster.Subscribe(cancel.Token);

		broadcaster.Publish("online", true);

		Assert.IsTrue(first.TryRead(out string? a));
		Assert.IsTrue(second.TryRead(out string? b));
		Assert.AreEqual("event: online\ndata: true\n\n", a);
		Assert.AreEqual(a, b);
	}

	[TestMethod]
	public void Broadcaster_SlowSubscriberDropsOldest()
	{
		Broadcaster broadcaster = new();
		using CancellationTokenSource cancel = new();
		ChannelReader<string> slow = broadcaster.Subscribe(cancel.Token);
		ChannelReader<string> fast = broadcaster.Subscribe(cancel.Token);

		List<string> received = [];
		for (int i = 0; i < 150; i++)
		{
			broadcaster.Publish("progress", i);
			while (fast.TryRead(out string? message))
			{
				received.Add(message);
			}
		}

		Assert.HasCount(150, received);
		Assert.AreEqual(100, slow.Count);
		Assert.IsTrue(slow.TryRead(out string? oldest));
		Assert.AreEqual("event: progress\ndata: 50\n\n", oldest);
	}

	[TestMethod]
	public void Broadcaster_CancelledSubscriberIsRemovedAndCompleted()
	{
		Broadcaster broadcaster = new();
		using CancellationTokenSource cancel = new();
		ChannelReader<string> reader = broadcaster.Subscribe(cancel.Token);
		Assert.AreEqual(1, broadcaster.SubscriberCount);

		cancel.Cancel();

		Assert.AreEqual(0, broadcaster.SubscriberCount);
		Assert.IsTrue(reader.Completion.IsCompleted);
	}

	[TestMethod]
	public void Broadcaster_SubscribeWithCancelledToken_AddsNothing()
	{
		Broadcaster broadcaster = new();
		using CancellationTokenSource cancel = new();
		cancel.Cancel();

		ChannelReader<string> reader = broadcaster.Subscribe(cancel.Token);

		Assert.AreEqual(0, broadcaster.SubscriberCount);
		Assert.IsTrue(reader.Completion.IsCompleted);
	}

	[TestMethod]
	public void Broadcaster_WritesSteamIdsAsStrings()
	{
		Broadcaster broadcaster = new();
		using CancellationTokenSource cancel = new();
		ChannelReader<string> reader = broadcaster.Subscribe(cancel.Token);

		broadcaster.Publish("feed", new FeedEntry(Start, TestHelpers.SteamId, "Tester", FeedKind.RankUp, "Rank up", null, 5));

		Assert.IsTrue(reader.TryRead(out string? message));
		Assert.Contains("\"steamId\":\"76561198000000001\"", message);
		Assert.Contains("\"kind\":\"RankUp\"", message);
	}

	[TestMethod]
	public void TryParseSteamId_AcceptsSteamId64()
	{
		Assert.IsTrue(ProgressReader.TryParseSteamId("76561197993183173", out ulong steamId));
		Assert.AreEqual(76561197993183173UL, steamId);
		Assert.IsTrue(ProgressReader.TryParseSteamId("76561197960265729", out _));
		Assert.IsTrue(ProgressReader.TryParseSteamId("76561202255233023", out _));
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[DataRow("abc")]
	[DataRow("1")]
	[DataRow("76561197993183173x")]
	[DataRow(" 76561197993183173")]
	[DataRow("+76561197993183173")]
	[DataRow("76561197960265728")]
	[DataRow("76561202255233024")]
	[DataRow("12345678901234567890")]
	[DataRow("７６５６１１９７９９３１８３１７３")]
	public void TryParseSteamId_RejectsOthers(string? text)
	{
		Assert.IsFalse(ProgressReader.TryParseSteamId(text, out ulong steamId));
		Assert.AreEqual(0UL, steamId);
	}

	[TestMethod]
	public async Task PlayerEndpoint_UnknownId_Returns404()
	{
		using ProgressReader reader = new(new InMemoryProgressionStore());

		IResult result = await Endpoints.PlayerAsync("76561197993183173", reader, NewState(), new FeatIndex(), CancellationToken.None).ConfigureAwait(false);

		Assert.IsInstanceOfType<NotFound>(result);
	}

	[TestMethod]
	public async Task PlayerEndpoint_BadId_Returns404()
	{
		using ProgressReader reader = new(new InMemoryProgressionStore());

		IResult result = await Endpoints.PlayerAsync("not-a-steam-id", reader, NewState(), new FeatIndex(), CancellationToken.None).ConfigureAwait(false);

		Assert.IsInstanceOfType<NotFound>(result);
	}

	[TestMethod]
	public async Task PlayerEndpoint_StoredPlayer_ReturnsView()
	{
		InMemoryProgressionStore store = new();
		await store.SaveAsync(ProgressSnapshot.CaptureAll(TestHelpers.Player()), CancellationToken.None).ConfigureAwait(false);
		using ProgressReader reader = new(store);

		IResult result = await Endpoints.PlayerAsync(TestHelpers.SteamId.ToString(System.Globalization.CultureInfo.InvariantCulture), reader, NewState(), new FeatIndex(), CancellationToken.None).ConfigureAwait(false);

		Ok<PlayerView> ok = Assert.IsInstanceOfType<Ok<PlayerView>>(result);
		Assert.AreEqual("Tester", ok.Value?.Name);
	}

	[TestMethod]
	public async Task RanksEndpoint_IncludesOverlayOnlyPlayers()
	{
		LiveState live = NewState();
		ProgressSnapshot rows = new(new PlayerRow(TestHelpers.SteamId, "Fresh", 50, 0, Start, Start), [], []);
		live.Apply(Batch() with { Progress = [rows] });

		using ProgressReader reader = new(new InMemoryProgressionStore());

		IResult result = await Endpoints.RanksAsync(reader, live, new FeatIndex(), CancellationToken.None).ConfigureAwait(false);

		Ok<IReadOnlyList<RankRow>> ok = Assert.IsInstanceOfType<Ok<IReadOnlyList<RankRow>>>(result);
		Assert.HasCount(1, ok.Value!);
		Assert.AreEqual("Fresh", ok.Value![0].Name);
	}

	[TestMethod]
	public async Task ProgressReader_CachesAnswersForTenSeconds()
	{
		InMemoryProgressionStore store = new();
		ManualTime time = new();
		using ProgressReader reader = new(store, time);

		Assert.IsEmpty(await reader.RankingsAsync().ConfigureAwait(false));
		Assert.IsNull(await reader.PlayerAsync(TestHelpers.SteamId).ConfigureAwait(false));
		await store.SaveAsync(ProgressSnapshot.CaptureAll(TestHelpers.Player()), CancellationToken.None).ConfigureAwait(false);

		time.Advance(TimeSpan.FromSeconds(9));
		Assert.IsEmpty(await reader.RankingsAsync().ConfigureAwait(false));
		Assert.IsNull(await reader.PlayerAsync(TestHelpers.SteamId).ConfigureAwait(false));

		time.Advance(TimeSpan.FromSeconds(2));
		Assert.HasCount(1, await reader.RankingsAsync().ConfigureAwait(false));
		Assert.IsNotNull(await reader.PlayerAsync(TestHelpers.SteamId).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task ProgressReader_RunsAtMostFourQueriesAtOnce()
	{
		using BlockingStore store = new();
		using ProgressReader reader = new(store);

		Task<PlayerProgress?>[] loads = [.. Enumerable.Range(0, 10).Select(i => reader.PlayerAsync(ProgressReader.MinSteamId + (ulong)i))];
		for (int i = 0; i < ProgressReader.MaxConcurrentQueries; i++)
		{
			Assert.IsTrue(await store.Entered.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
		}

		Assert.IsFalse(await store.Entered.WaitAsync(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false));
		Assert.AreEqual(ProgressReader.MaxConcurrentQueries, store.Running);

		store.Release.SetResult();
		await Task.WhenAll(loads).ConfigureAwait(false);

		Assert.AreEqual(10, store.Calls);
		Assert.AreEqual(ProgressReader.MaxConcurrentQueries, store.MaxRunning);
	}

	[TestMethod]
	public async Task ProgressReader_ConcurrentMissesForOneId_QueryOnce()
	{
		using BlockingStore store = new();
		using ProgressReader reader = new(store);

		Task<PlayerProgress?> first = reader.PlayerAsync(TestHelpers.SteamId);
		Task<PlayerProgress?> second = reader.PlayerAsync(TestHelpers.SteamId);
		Assert.IsTrue(await store.Entered.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));

		store.Release.SetResult();
		await Task.WhenAll(first, second).ConfigureAwait(false);

		Assert.AreEqual(1, store.Calls);
	}

	[TestMethod]
	public async Task ProgressReader_CancelledCallerStopsWaiting()
	{
		using BlockingStore store = new();
		using ProgressReader reader = new(store);
		using CancellationTokenSource cancel = new();

		Task<PlayerProgress?> load = reader.PlayerAsync(TestHelpers.SteamId, cancel.Token);
		Assert.IsTrue(await store.Entered.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
		await cancel.CancelAsync().ConfigureAwait(false);

		await Assert.ThrowsAsync<OperationCanceledException>(() => load).ConfigureAwait(false);
		store.Release.SetResult();
	}

	[TestMethod]
	public void LiveEndpoint_ReturnsOnlineSnapshotAndFeed()
	{
		LiveState live = NewState();
		live.Apply(Batch(new FeedEntry(Start, TestHelpers.SteamId, "Tester", FeedKind.SkillShot, "Wallbang", "AK-47", 25)));

		IResult result = Endpoints.Live(live, new FeatIndex(), new ManualTime());

		Ok<LiveView> ok = Assert.IsInstanceOfType<Ok<LiveView>>(result);
		Assert.IsTrue(ok.Value!.Online);
		Assert.AreEqual("de_inferno", ok.Value.Snapshot?.Map);
		Assert.HasCount(1, ok.Value.Feed);
		Assert.AreEqual("2026-10-17", ok.Value.Featured?.Day);
	}

	[TestMethod]
	public async Task Ingest_WithoutToken_Returns401()
	{
		LiveState live = NewState();
		DefaultHttpContext context = IngestContext(null, JsonSerializer.Serialize(Batch(), LiveJson.Options));

		IResult result = await Endpoints.IngestAsync(context, new SiteOptions { Token = "dev" }, live, new Broadcaster(), new FeatIndex()).ConfigureAwait(false);

		Assert.AreEqual(StatusCodes.Status401Unauthorized, StatusOf(result));
		Assert.IsNull(live.Snapshot);
	}

	[TestMethod]
	public async Task Ingest_WhenNoTokenConfigured_Returns401()
	{
		DefaultHttpContext context = IngestContext("Bearer ", JsonSerializer.Serialize(Batch(), LiveJson.Options));

		IResult result = await Endpoints.IngestAsync(context, new SiteOptions(), NewState(), new Broadcaster(), new FeatIndex()).ConfigureAwait(false);

		Assert.AreEqual(StatusCodes.Status401Unauthorized, StatusOf(result));
	}

	[TestMethod]
	[DataRow("not json")]
	[DataRow("null")]
	[DataRow("{}")]
	[DataRow("{\"snapshot\":{\"sentAt\":\"2026-10-17T12:00:00Z\",\"map\":\"x\",\"gameMode\":\"y\",\"maxPlayers\":1,\"players\":null},\"feed\":[],\"progress\":[],\"settings\":{}}")]
	[DataRow("{\"snapshot\":{\"sentAt\":\"2026-10-17T12:00:00Z\",\"map\":\"x\",\"gameMode\":\"y\",\"maxPlayers\":1,\"players\":[null]},\"feed\":[],\"progress\":[],\"settings\":{}}")]
	[DataRow("{\"snapshot\":{\"sentAt\":\"2026-10-17T12:00:00Z\",\"map\":\"x\",\"gameMode\":\"y\",\"maxPlayers\":1,\"players\":[]},\"feed\":[],\"progress\":[],\"settings\":{},\"recaps\":[{\"steamId\":1,\"playerName\":\"a\"}]}")]
	public async Task Ingest_BadBody_Returns400(string body)
	{
		LiveState live = NewState();
		DefaultHttpContext context = IngestContext("Bearer dev", body);

		IResult result = await Endpoints.IngestAsync(context, new SiteOptions { Token = "dev" }, live, new Broadcaster(), new FeatIndex()).ConfigureAwait(false);

		Assert.AreEqual(StatusCodes.Status400BadRequest, StatusOf(result));
		Assert.IsNull(live.Snapshot);
	}

	[TestMethod]
	public async Task Ingest_OversizedBody_Returns413()
	{
		DefaultHttpContext context = IngestContext("Bearer dev", "{}");
		context.Request.ContentLength = Endpoints.IngestBodyLimit + 1;

		IResult result = await Endpoints.IngestAsync(context, new SiteOptions { Token = "dev" }, NewState(), new Broadcaster(), new FeatIndex()).ConfigureAwait(false);

		Assert.AreEqual(StatusCodes.Status413PayloadTooLarge, StatusOf(result));
	}

	[TestMethod]
	public async Task Ingest_ThreeMebibyteBody_IsAccepted()
	{
		LiveState live = NewState();
		string body = JsonSerializer.Serialize(Batch(), LiveJson.Options) + new string(' ', 3 * 1024 * 1024);
		DefaultHttpContext context = IngestContext("Bearer dev", body);

		IResult result = await Endpoints.IngestAsync(context, new SiteOptions { Token = "dev" }, live, new Broadcaster(), new FeatIndex()).ConfigureAwait(false);

		Assert.AreEqual(StatusCodes.Status204NoContent, StatusOf(result));
		Assert.AreEqual("de_inferno", live.Snapshot?.Map);
	}

	[TestMethod]
	public async Task Ingest_ValidBatch_AppliesAndPublishes()
	{
		LiveState live = NewState();
		Broadcaster broadcaster = new();
		using CancellationTokenSource cancel = new();
		ChannelReader<string> reader = broadcaster.Subscribe(cancel.Token);
		ProgressSnapshot rows = new(new PlayerRow(TestHelpers.SteamId, "Tester", 50, 0, Start, Start), [], []);
		FeedEntry second = new(Start.AddSeconds(2), TestHelpers.SteamId, "Tester", FeedKind.SkillShot, "Second", null, 5);
		FeedEntry first = new(Start.AddSeconds(1), TestHelpers.SteamId, "Tester", FeedKind.SkillShot, "First", null, 5);
		LiveBatch batch = Batch(second, first) with { Progress = [rows] };
		DefaultHttpContext context = IngestContext("Bearer dev", JsonSerializer.Serialize(batch, LiveJson.Options));

		IResult result = await Endpoints.IngestAsync(context, new SiteOptions { Token = "dev" }, live, broadcaster, new FeatIndex()).ConfigureAwait(false);

		Assert.AreEqual(StatusCodes.Status204NoContent, StatusOf(result));
		Assert.AreEqual("de_inferno", live.Snapshot?.Map);
		List<string> messages = [];
		while (reader.TryRead(out string? message))
		{
			messages.Add(message);
		}

		Assert.HasCount(4, messages);
		Assert.StartsWith("event: snapshot\n", messages[0]);
		Assert.Contains("\"title\":\"First\"", messages[1]);
		Assert.Contains("\"title\":\"Second\"", messages[2]);
		Assert.AreEqual("event: progress\ndata: [\"76561198000000001\"]\n\n", messages[3]);
	}

	[TestMethod]
	[DataRow(8081, "/api/ingest", true)]
	[DataRow(8081, "/API/Ingest", true)]
	[DataRow(8080, "/api/ingest/", false)]
	[DataRow(8080, "/API/INGEST", false)]
	[DataRow(8081, "/api/live", false)]
	[DataRow(8081, "/healthz", false)]
	[DataRow(8080, "/api/ingest", false)]
	[DataRow(8080, "/api/live", true)]
	[DataRow(8080, "/", true)]
	public void PortGate_KeepsIngestOnItsOwnPort(int port, string path, bool allowed) =>
		Assert.AreEqual(allowed, Endpoints.IsAllowedOnPort(port, new PathString(path)));

	[TestMethod]
	public void OnlineWatcher_PublishesOnlyWhenOnlineFlips()
	{
		ManualTime time = new();
		LiveState live = NewState(time);
		Broadcaster broadcaster = new();
		using CancellationTokenSource cancel = new();
		ChannelReader<string> reader = broadcaster.Subscribe(cancel.Token);
		using OnlineWatcher watcher = new(live, broadcaster);

		Assert.IsFalse(watcher.Check());
		live.Apply(Batch());
		Assert.IsTrue(watcher.Check());
		Assert.IsFalse(watcher.Check());
		time.Advance(TimeSpan.FromSeconds(60));
		Assert.IsTrue(watcher.Check());

		Assert.IsTrue(reader.TryRead(out string? online));
		Assert.AreEqual("event: online\ndata: true\n\n", online);
		Assert.IsTrue(reader.TryRead(out string? offline));
		Assert.AreEqual("event: online\ndata: false\n\n", offline);
		Assert.IsFalse(reader.TryRead(out _));
	}

	/// <summary>A store whose loads wait until the test releases them, and that counts how many run at once.</summary>
	private sealed class BlockingStore : IProgressionStore, IDisposable
	{
		private int running;
		private int maxRunning;
		private int calls;

		public SemaphoreSlim Entered { get; } = new(0);

		public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public int Running => Volatile.Read(ref running);

		public int MaxRunning => Volatile.Read(ref maxRunning);

		public int Calls => Volatile.Read(ref calls);

		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public async Task<PlayerProgress?> LoadAsync(ulong steamId, CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref calls);
			int now = Interlocked.Increment(ref running);
			int seen;
			do
			{
				seen = Volatile.Read(ref maxRunning);
			}
			while (now > seen && Interlocked.CompareExchange(ref maxRunning, now, seen) != seen);

			Entered.Release();
			try
			{
				await Release.Task.ConfigureAwait(false);
				return null;
			}
			finally
			{
				Interlocked.Decrement(ref running);
			}
		}

		public Task SaveAsync(ProgressSnapshot snapshot, CancellationToken cancellationToken) => Task.CompletedTask;

		public Task ResetAsync(PlayerRow player, CancellationToken cancellationToken) => Task.CompletedTask;

		public Task<IReadOnlyList<LeaderboardEntry>> TopAsync(int count, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LeaderboardEntry>>([]);

		public Task<IReadOnlyList<WeaponLeaderboardEntry>> TopByWeaponAsync(WeaponKey weapon, int count, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WeaponLeaderboardEntry>>([]);

		public Task<IReadOnlyList<RankingEntry>> RankingsAsync(int count, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RankingEntry>>([]);

		public void Dispose() => Entered.Dispose();
	}
}

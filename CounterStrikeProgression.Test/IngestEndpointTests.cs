// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Heatmaps;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Site;
using ktsu.CounterStrikeProgression.Site.Live;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

[TestClass]
public sealed class IngestEndpointTests
{
	private static readonly DateTime Start = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

	private string directory = string.Empty;

	[TestInitialize]
	public void Initialize()
	{
		directory = Path.Combine(Path.GetTempPath(), "csp-ingest-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
	}

	[TestCleanup]
	public void Cleanup() => Directory.Delete(directory, recursive: true);

	private LiveState NewState() =>
		new(new SiteOptions(), new FeedStore(Path.Combine(directory, "feed.json")), new ManualTime(), NullLogger<LiveState>.Instance);

	private static MatchFights NewMatchFights() => new(new InMemoryKillMapStore(), NullLogger<MatchFights>.Instance);

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
	public async Task Ingest_WithoutToken_Returns401()
	{
		LiveState live = NewState();
		DefaultHttpContext context = IngestContext(null, JsonSerializer.Serialize(Batch(), LiveJson.Options));

		IResult result = await IngestEndpoints.IngestAsync(context, new SiteOptions { Token = "dev" }, live, new Broadcaster(), new FeatIndex(), NewMatchFights()).ConfigureAwait(false);

		Assert.AreEqual(StatusCodes.Status401Unauthorized, StatusOf(result));
		Assert.IsNull(live.Snapshot);
	}

	[TestMethod]
	public async Task Ingest_WhenNoTokenConfigured_Returns401()
	{
		DefaultHttpContext context = IngestContext("Bearer ", JsonSerializer.Serialize(Batch(), LiveJson.Options));

		IResult result = await IngestEndpoints.IngestAsync(context, new SiteOptions(), NewState(), new Broadcaster(), new FeatIndex(), NewMatchFights()).ConfigureAwait(false);

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

		IResult result = await IngestEndpoints.IngestAsync(context, new SiteOptions { Token = "dev" }, live, new Broadcaster(), new FeatIndex(), NewMatchFights()).ConfigureAwait(false);

		Assert.AreEqual(StatusCodes.Status400BadRequest, StatusOf(result));
		Assert.IsNull(live.Snapshot);
	}

	[TestMethod]
	public async Task Ingest_ContractRows_AreAcceptedAndANullRowIsRejected()
	{
		ProgressSnapshot rows = new(null, [], []) { Contracts = [new ContractRow(TestHelpers.SteamId, "2026-10-17", "headshots", 3, false)] };
		LiveState live = NewState();
		DefaultHttpContext good = IngestContext("Bearer dev", JsonSerializer.Serialize(Batch() with { Progress = [rows] }, LiveJson.Options));
		IResult accepted = await IngestEndpoints.IngestAsync(good, new SiteOptions { Token = "dev" }, live, new Broadcaster(), new FeatIndex(), NewMatchFights()).ConfigureAwait(false);
		Assert.AreEqual(StatusCodes.Status204NoContent, StatusOf(accepted));

		string json = JsonSerializer.Serialize(Batch() with { Progress = [rows] }, LiveJson.Options);
		foreach ((string from, string to) in new[] { ("\"contracts\":[{", "\"contracts\":[null,{"), ("\"period\":\"2026-10-17\"", "\"period\":null"), ("\"contract\":\"headshots\"", "\"contract\":null") })
		{
			string body = json.Replace(from, to, StringComparison.Ordinal);
			Assert.AreNotEqual(json, body);
			DefaultHttpContext bad = IngestContext("Bearer dev", body);
			IResult rejected = await IngestEndpoints.IngestAsync(bad, new SiteOptions { Token = "dev" }, NewState(), new Broadcaster(), new FeatIndex(), NewMatchFights()).ConfigureAwait(false);
			Assert.AreEqual(StatusCodes.Status400BadRequest, StatusOf(rejected), to);
		}
	}

	[TestMethod]
	public async Task Ingest_OversizedBody_Returns413()
	{
		DefaultHttpContext context = IngestContext("Bearer dev", "{}");
		context.Request.ContentLength = IngestEndpoints.IngestBodyLimit + 1;

		IResult result = await IngestEndpoints.IngestAsync(context, new SiteOptions { Token = "dev" }, NewState(), new Broadcaster(), new FeatIndex(), NewMatchFights()).ConfigureAwait(false);

		Assert.AreEqual(StatusCodes.Status413PayloadTooLarge, StatusOf(result));
	}

	[TestMethod]
	public async Task Ingest_ThreeMebibyteBody_IsAccepted()
	{
		LiveState live = NewState();
		string body = JsonSerializer.Serialize(Batch(), LiveJson.Options) + new string(' ', 3 * 1024 * 1024);
		DefaultHttpContext context = IngestContext("Bearer dev", body);

		IResult result = await IngestEndpoints.IngestAsync(context, new SiteOptions { Token = "dev" }, live, new Broadcaster(), new FeatIndex(), NewMatchFights()).ConfigureAwait(false);

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

		IResult result = await IngestEndpoints.IngestAsync(context, new SiteOptions { Token = "dev" }, live, broadcaster, new FeatIndex(), NewMatchFights()).ConfigureAwait(false);

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
	public async Task Ingest_NullKill_Returns400()
	{
		string json = JsonSerializer.Serialize(Batch() with { Kills = [] }, LiveJson.Options);
		string body = json.Replace("\"kills\":[]", "\"kills\":[null]", StringComparison.Ordinal);
		Assert.AreNotEqual(json, body);

		IResult result = await IngestEndpoints.IngestAsync(IngestContext("Bearer dev", body), new SiteOptions { Token = "dev" }, NewState(), new Broadcaster(), new FeatIndex(), NewMatchFights()).ConfigureAwait(false);

		Assert.AreEqual(StatusCodes.Status400BadRequest, StatusOf(result));
	}

	[TestMethod]
	public async Task Ingest_Kills_KeepTheMatchAndPublishFights()
	{
		Broadcaster broadcaster = new();
		using CancellationTokenSource cancel = new();
		ChannelReader<string> reader = broadcaster.Subscribe(cancel.Token);
		MatchFights fights = NewMatchFights();
		KillPoint kill = new("de_inferno", TestHelpers.SteamId, 0, "ak47", true, 0, 0, 0, 100, 100, 0, Start);
		DefaultHttpContext context = IngestContext("Bearer dev", JsonSerializer.Serialize(Batch() with { Kills = [kill] }, LiveJson.Options));

		IResult result = await IngestEndpoints.IngestAsync(context, new SiteOptions { Token = "dev" }, NewState(), broadcaster, new FeatIndex(), fights).ConfigureAwait(false);

		Assert.AreEqual(StatusCodes.Status204NoContent, StatusOf(result));
		Assert.AreEqual(kill, Assert.ContainsSingle(await fights.CurrentAsync(CancellationToken.None).ConfigureAwait(false)));
		List<string> messages = [];
		while (reader.TryRead(out string? message))
		{
			messages.Add(message);
		}

		Assert.StartsWith("event: fights\n", messages[^1]);
		Assert.Contains("\"steamId\":\"76561198000000001\"", messages[^1]);
	}
}

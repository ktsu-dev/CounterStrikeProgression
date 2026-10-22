// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using System.Text.Json;
using System.Threading.Channels;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Heatmaps;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Core.Players;
using ktsu.CounterStrikeProgression.Site;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Live;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

[TestClass]
public sealed class LivePositionTests
{
	private const ulong Human = 76561198000000007;
	private const string Dust = "de_dust2";

	private static readonly DateTime At = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

	private string directory = string.Empty;

	[TestInitialize]
	public void Initialize()
	{
		directory = Path.Combine(Path.GetTempPath(), "csp-positions-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
	}

	[TestCleanup]
	public void Cleanup() => Directory.Delete(directory, recursive: true);

	private static LivePlayer Player(int slot, double? x, double? y, double? yaw = 0, bool isBot = false, bool? alive = true) =>
		new(slot, isBot ? 0 : Human, isBot ? "Bot Ringo" : "Tester", isBot, 0, 0, 0, 0, x, y, yaw, alive);

	private static ServerSnapshot Server(string map, params LivePlayer[] players) => new(At, map, "Deathmatch", 16, players);

	[TestMethod]
	public void From_ConvertsWorldToRadarPixels()
	{
		PositionsView view = LivePositions.From(Server(Dust, Player(3, 100, -200)), "/radar/de_dust2.png");

		(double expectedX, double expectedY) = MapOverview.For(Dust)!.ToPixel(new HeatPoint(100, -200));
		PlayerPosition player = Assert.ContainsSingle(view.Players);
		Assert.AreEqual(Dust, view.Map);
		Assert.IsTrue(view.Calibrated);
		Assert.AreEqual("/radar/de_dust2.png", view.Radar);
		Assert.AreEqual(MapOverview.Size, view.Size);
		Assert.AreEqual(3, player.Slot);
		Assert.AreEqual(expectedX, player.X, 0.001);
		Assert.AreEqual(expectedY, player.Y, 0.001);
		Assert.AreEqual("76561198000000007", player.SteamId);
		Assert.AreEqual("Tester", player.Name);
		Assert.IsFalse(player.IsBot);
		Assert.IsTrue(player.Alive);
	}

	[TestMethod]
	public void From_FacingIsClockwiseFromUp()
	{
		PositionsView view = LivePositions.From(Server(Dust, Player(1, 0, 0, yaw: 90), Player(2, 0, 0, yaw: 0), Player(4, 0, 0, yaw: 180)), null);

		Assert.AreEqual(0, view.Players.Single(p => p.Slot == 1).Facing, 0.001);
		Assert.AreEqual(90, view.Players.Single(p => p.Slot == 2).Facing, 0.001);
		Assert.AreEqual(270, view.Players.Single(p => p.Slot == 4).Facing, 0.001);
	}

	[TestMethod]
	public void From_OlderPluginPlayerWithoutPositionMembers_IsLeftOutButMapIsNamed()
	{
		const string json = """
			{"sentAt":"2026-10-21T12:00:00Z","map":"de_dust2","gameMode":"Deathmatch","maxPlayers":16,
			"players":[{"slot":3,"steamId":76561198000000007,"name":"Tester","isBot":false,"rank":1,"kills":2,"deaths":3,"mapXp":4.5}]}
			""";

		ServerSnapshot snapshot = JsonSerializer.Deserialize<ServerSnapshot>(json, SiteJson.Options)!;
		PositionsView view = LivePositions.From(snapshot, null);

		Assert.ContainsSingle(snapshot.Players);
		Assert.AreEqual(Dust, view.Map);
		Assert.IsEmpty(view.Players);
	}

	[TestMethod]
	public void From_IncludesBotsWithoutSteamId()
	{
		PositionsView view = LivePositions.From(Server(Dust, Player(5, 0, 0, isBot: true)), null);

		PlayerPosition bot = Assert.ContainsSingle(view.Players);
		Assert.IsTrue(bot.IsBot);
		Assert.IsNull(bot.SteamId);
	}

	[TestMethod]
	public void BotPersonaKeepsItsId()
	{
		LivePlayer persona = new(5, BotIds.For("Muhlik"), "Muhlik", true, 0, 0, 0, 0, 0, 0, 0, true);
		PositionsView view = LivePositions.From(Server(Dust, persona, Player(6, 0, 0, isBot: true)), null);

		Assert.AreEqual("10000000062568863", view.Players.Single(p => p.Slot == 5).SteamId);
		Assert.IsNull(view.Players.Single(p => p.Slot == 6).SteamId);
	}

	[TestMethod]
	public void From_LeavesOutPlayersWithoutPositions()
	{
		PositionsView view = LivePositions.From(Server(Dust, Player(1, null, 0)), null);

		Assert.AreEqual(Dust, view.Map);
		Assert.IsEmpty(view.Players);
	}

	[TestMethod]
	public void From_LeavesOutPositionsOffTheRadar()
	{
		PositionsView view = LivePositions.From(Server(Dust, Player(1, -10000, 0), Player(2, 0, 10000)), null);

		Assert.IsEmpty(view.Players);
	}

	[TestMethod]
	public void From_UncalibratedMapHasNoPlayers()
	{
		PositionsView view = LivePositions.From(Server("de_madeup", Player(1, 0, 0)), null);

		Assert.AreEqual("de_madeup", view.Map);
		Assert.IsFalse(view.Calibrated);
		Assert.IsEmpty(view.Players);
	}

	[TestMethod]
	public void From_NoSnapshotHasNoMap()
	{
		PositionsView view = LivePositions.From(null, null);

		Assert.IsNull(view.Map);
		Assert.IsFalse(view.Calibrated);
		Assert.IsEmpty(view.Players);
	}

	[TestMethod]
	public void Ingest_PublishesPositions()
	{
		Broadcaster broadcaster = new();
		using CancellationTokenSource cancel = new();
		ChannelReader<string> reader = broadcaster.Subscribe(cancel.Token);
		LiveBatch batch = new(Server(Dust, Player(3, 100, -200)), [], [], new ProgressionSettings());

		LivePublisher.Publish(broadcaster, batch, new LiveChanges(SnapshotChanged: true, [], []), []);

		const string Prefix = "event: positions\ndata: ";
		List<string> messages = [];
		while (reader.TryRead(out string? message))
		{
			messages.Add(message);
		}

		string positions = Assert.ContainsSingle(messages.Where(m => m.StartsWith(Prefix, StringComparison.Ordinal)));
		PositionsView? view = JsonSerializer.Deserialize<PositionsView>(positions[Prefix.Length..].Trim(), SiteJson.Options);
		Assert.IsNotNull(view);
		Assert.AreEqual(Dust, view.Map);
		Assert.IsNull(view.Radar);
		Assert.AreEqual(LivePositions.From(batch.Snapshot, null).Players.Single(), Assert.ContainsSingle(view.Players));
	}

	[TestMethod]
	public void Positions_ServesTheLatestSnapshotWithItsRadar()
	{
		Directory.CreateDirectory(Path.Combine(directory, "radar"));
		File.WriteAllBytes(Path.Combine(directory, "radar", Dust + ".png"), [1]);
		LiveState live = new(new SiteOptions(), new FeedStore(Path.Combine(directory, "feed.json")), TimeProvider.System, NullLogger<LiveState>.Instance);
		live.Apply(new LiveBatch(Server(Dust, Player(3, 100, -200)), [], [], new ProgressionSettings()));
		RadarImages radars = new(new SiteOptions { DataPath = directory });

		PositionsView view = ((Ok<PositionsView>)PositionEndpoints.Positions(live, radars)).Value!;

		Assert.AreEqual(Dust, view.Map);
		Assert.AreEqual("/radar/de_dust2.png", view.Radar);
		Assert.AreEqual(3, Assert.ContainsSingle(view.Players).Slot);
	}

	[TestMethod]
	public void Positions_WithoutASnapshotIsEmpty()
	{
		LiveState live = new(new SiteOptions(), new FeedStore(Path.Combine(directory, "feed.json")), TimeProvider.System, NullLogger<LiveState>.Instance);
		RadarImages radars = new(new SiteOptions { DataPath = directory });

		PositionsView view = ((Ok<PositionsView>)PositionEndpoints.Positions(live, radars)).Value!;

		Assert.IsNull(view.Map);
		Assert.IsNull(view.Radar);
		Assert.IsEmpty(view.Players);
	}
}

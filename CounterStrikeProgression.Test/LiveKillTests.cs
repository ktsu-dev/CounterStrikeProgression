// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using System.Text.Json;
using System.Threading.Channels;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Heatmaps;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Core.Weapons;
using ktsu.CounterStrikeProgression.Site.Live;

[TestClass]
public sealed class LiveKillTests
{
	private const ulong Killer = 76561198000000007;
	private const ulong Victim = 76561198000000008;
	private const string Dust = "de_dust2";

	private static readonly DateTime At = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

	private static KillPoint Kill(
		string map = Dust,
		ulong killer = Killer,
		ulong victim = Victim,
		string weapon = "ak47",
		bool headshot = false,
		short killerX = 100,
		short killerY = -200,
		short victimX = 300,
		short victimY = 400,
		string? killerName = "Killer",
		string? victimName = "Victim") =>
		new(map, killer, victim, weapon, headshot, killerX, killerY, 0, victimX, victimY, 0, At, killerName, victimName);

	[TestMethod]
	public void From_ConvertsBothEndsToRadarPixels()
	{
		LiveKill kill = Assert.ContainsSingle(LiveKills.From([Kill(headshot: true)]));

		MapOverview overview = MapOverview.For(Dust)!;
		(double killerX, double killerY) = overview.ToPixel(100, -200);
		(double victimX, double victimY) = overview.ToPixel(300, 400);
		Assert.AreEqual(Dust, kill.Map);
		Assert.IsNotNull(kill.KillerX);
		Assert.IsNotNull(kill.KillerY);
		Assert.AreEqual(killerX, kill.KillerX.Value, 0.001);
		Assert.AreEqual(killerY, kill.KillerY.Value, 0.001);
		Assert.AreEqual(victimX, kill.VictimX, 0.001);
		Assert.AreEqual(victimY, kill.VictimY, 0.001);
		Assert.AreEqual("Killer", kill.KillerName);
		Assert.AreEqual("Victim", kill.VictimName);
		Assert.IsFalse(kill.KillerIsBot);
		Assert.IsFalse(kill.VictimIsBot);
		Assert.IsTrue(kill.Headshot);
		Assert.AreEqual(At, kill.At);
	}

	[TestMethod]
	public void From_UsesWeaponDisplayName()
	{
		IReadOnlyList<LiveKill> kills = LiveKills.From([Kill(weapon: "ak47"), Kill(weapon: "madeup_gun")]);

		Assert.HasCount(2, kills);
		Assert.AreEqual(WeaponCatalog.Find(WeaponKey.Create("ak47"))!.DisplayName, kills[0].Weapon);
		Assert.AreNotEqual("ak47", kills[0].Weapon);
		Assert.AreEqual("madeup_gun", kills[1].Weapon);
	}

	[TestMethod]
	public void From_LeavesOutKillsOnUncalibratedMaps()
	{
		IReadOnlyList<LiveKill> kills = LiveKills.From([Kill(map: "de_madeup")]);

		Assert.IsEmpty(kills);
	}

	[TestMethod]
	public void From_LeavesOutAVictimOffTheRadar()
	{
		IReadOnlyList<LiveKill> kills = LiveKills.From([Kill(victimX: -10000), Kill(victimY: 10000)]);

		Assert.IsEmpty(kills);
	}

	[TestMethod]
	public void From_DropsTheLineWhenTheKillerIsOffTheRadar()
	{
		LiveKill kill = Assert.ContainsSingle(LiveKills.From([Kill(killerX: -10000)]));

		Assert.IsNull(kill.KillerX);
		Assert.IsNull(kill.KillerY);
		Assert.AreEqual(MapOverview.For(Dust)!.ToPixel(300, 400).X, kill.VictimX, 0.001);
	}

	[TestMethod]
	public void From_KeepsBotOnBotKills()
	{
		LiveKill kill = Assert.ContainsSingle(LiveKills.From([Kill(killer: 0, victim: 0, killerName: "Bot Ringo", victimName: "Bot Vinny")]));

		Assert.IsTrue(kill.KillerIsBot);
		Assert.IsTrue(kill.VictimIsBot);
		Assert.AreEqual("Bot Ringo", kill.KillerName);
		Assert.AreEqual("Bot Vinny", kill.VictimName);
	}

	[TestMethod]
	public void Ingest_PublishesKills()
	{
		Broadcaster broadcaster = new();
		using CancellationTokenSource cancel = new();
		ChannelReader<string> reader = broadcaster.Subscribe(cancel.Token);
		ServerSnapshot snapshot = new(At, Dust, "Deathmatch", 16, []);
		LiveBatch batch = new(snapshot, [], [], new ProgressionSettings(), Kills: [Kill(killer: 0, victim: 0)]);

		LivePublisher.Publish(broadcaster, batch, new LiveChanges(SnapshotChanged: false, [], []), []);

		const string Prefix = "event: kills\ndata: ";
		List<string> messages = [];
		while (reader.TryRead(out string? message))
		{
			messages.Add(message);
		}

		string kills = Assert.ContainsSingle(messages.Where(m => m.StartsWith(Prefix, StringComparison.Ordinal)));
		LiveKill[]? published = JsonSerializer.Deserialize<LiveKill[]>(kills[Prefix.Length..].Trim(), SiteJson.Options);
		Assert.IsNotNull(published);
		Assert.AreEqual(LiveKills.From(batch.Kills!).Single(), Assert.ContainsSingle(published));
	}

	[TestMethod]
	public void Ingest_PublishesNoKillsForABatchWithoutThem()
	{
		Broadcaster broadcaster = new();
		using CancellationTokenSource cancel = new();
		ChannelReader<string> reader = broadcaster.Subscribe(cancel.Token);
		LiveBatch batch = new(new ServerSnapshot(At, Dust, "Deathmatch", 16, []), [], [], new ProgressionSettings());

		LivePublisher.Publish(broadcaster, batch, new LiveChanges(SnapshotChanged: false, [], []), []);

		Assert.IsFalse(reader.TryRead(out _));
	}
}

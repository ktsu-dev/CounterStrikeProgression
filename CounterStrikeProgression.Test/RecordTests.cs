// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core.Discord;
using ktsu.CounterStrikeProgression.Core.Events;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Recap;
using ktsu.CounterStrikeProgression.Core.Records;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Core.Weapons;
using ktsu.CounterStrikeProgression.Site;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Views;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

[TestClass]
public sealed class RecordTests
{
	private const ulong Other = 76561198000000002;

	public TestContext TestContext { get; set; } = null!;

	private static ProgressionEngine LoadedEngine(Action<Core.ProgressionSettings>? configure = null)
	{
		ProgressionEngine engine = TestHelpers.Engine(configure);
		engine.Records.Load([]);
		return engine;
	}

	private static IReadOnlyList<ProgressionNotice> Records(ProgressionResult result) =>
		[.. result.Notices.Where(n => n.Kind == NoticeKind.RecordBroken)];

	private static CombatEvent RifleKill(double distance) => TestHelpers.Kill("ak47") with { Distance = distance };

	[TestMethod]
	public void NothingIsCheckedUntilTheStoredRecordsLoad()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		ProgressionResult result = engine.ApplyCombat(TestHelpers.Player(), RifleKill(3000));

		Assert.IsEmpty(Records(result));
		Assert.IsFalse(engine.Records.IsDirty);
	}

	[TestMethod]
	public void ALongKillSetsTheClassRecordAndAShortOneDoesNot()
	{
		ProgressionEngine engine = LoadedEngine();
		PlayerProgress player = TestHelpers.Player();

		// Rifles need half their 2,000 unit Long shot distance.
		Assert.IsEmpty(Records(engine.ApplyCombat(player, RifleKill(900))));
		ProgressionNotice notice = Records(engine.ApplyCombat(player, RifleKill(1500))).Single();

		Assert.AreEqual(RecordCatalog.LongestKillId(WeaponClass.Rifle), notice.Record!.Definition.Id);
		Assert.AreEqual(1500, notice.Record.Holder.Value);
		Assert.IsNull(notice.Record.Holder.PreviousValue);
		Assert.IsTrue(notice.Broadcast);
		Assert.AreEqual("38.1 m", notice.Record.Definition.Format(1500));
	}

	[TestMethod]
	public void ARecordMustBeBeatenOutright()
	{
		ProgressionEngine engine = LoadedEngine();
		PlayerProgress first = TestHelpers.Player();
		PlayerProgress second = new(Other) { Name = "Rival" };

		_ = engine.ApplyCombat(first, RifleKill(1500));
		Assert.IsEmpty(Records(engine.ApplyCombat(second, RifleKill(1500))));
		RecordBreak broken = Records(engine.ApplyCombat(second, RifleKill(1600))).Single().Record!;

		Assert.AreEqual(Other, broken.Holder.SteamId);
		Assert.AreEqual(TestHelpers.SteamId, broken.Holder.PreviousSteamId);
		Assert.AreEqual("Tester", broken.Holder.PreviousName);
		Assert.AreEqual(1500, broken.Holder.PreviousValue);
		Assert.IsFalse(broken.BeatOwn);
		Assert.AreEqual("Beat Tester's 38.1 m", FeedBuilder.RecordDetail(broken));
	}

	[TestMethod]
	public void KillsOnBotsDoNotCountUnlessTheSettingsSaySo()
	{
		CombatEvent botKill = RifleKill(1500) with { VictimIsBot = true };

		Assert.IsEmpty(Records(LoadedEngine().ApplyCombat(TestHelpers.Player(), botKill)));
		Assert.HasCount(1, Records(LoadedEngine(s => s.RecordsCountBots = true).ApplyCombat(TestHelpers.Player(), botKill)));
	}

	[TestMethod]
	public void RecordsCanBeTurnedOff() =>
		Assert.IsEmpty(Records(LoadedEngine(s => s.RecordsEnabled = false).ApplyCombat(TestHelpers.Player(), RifleKill(1500))));

	[TestMethod]
	public void ALongNoScopeSetsBothItsRecordAndTheSniperRecord()
	{
		CombatEvent kill = TestHelpers.Kill("awp") with { Distance = 1400, NoScope = true };
		IReadOnlyList<ProgressionNotice> notices = Records(LoadedEngine().ApplyCombat(TestHelpers.Player(), kill));

		CollectionAssert.AreEquivalent(
			new[] { RecordCatalog.NoScopeId, RecordCatalog.LongestKillId(WeaponClass.Sniper) },
			notices.Select(n => n.Record!.Definition.Id).ToArray());
	}

	[TestMethod]
	public void ALifeSetsKillAndAccuracyRecordsOverTheMinimums()
	{
		ProgressionEngine engine = LoadedEngine();
		PlayerProgress player = TestHelpers.Player();

		Assert.IsEmpty(Records(engine.ApplyLifeRecords(player, new LifeTotals(4, 0, 29, 29, 0))));
		IReadOnlyList<ProgressionNotice> notices = Records(engine.ApplyLifeRecords(player, new LifeTotals(7, 1, 40, 30, 2)));

		RecordBreak kills = notices.Single(n => n.Record!.Definition.Id == RecordCatalog.LifeKillsId).Record!;
		RecordBreak accuracy = notices.Single(n => n.Record!.Definition.Id == RecordCatalog.LifeAccuracyId).Record!;
		Assert.AreEqual(6, kills.Holder.Value);
		Assert.AreEqual(0.75, accuracy.Holder.Value);
		Assert.AreEqual("75%", accuracy.Definition.Format(accuracy.Holder.Value));
	}

	[TestMethod]
	public void ALifeSpentMostlyOnBotsSetsNoAccuracyRecord()
	{
		IReadOnlyList<ProgressionNotice> notices = Records(LoadedEngine().ApplyLifeRecords(TestHelpers.Player(), new LifeTotals(0, 0, 40, 30, 20)));
		Assert.IsEmpty(notices);
	}

	[TestMethod]
	public void BeatingYourOwnRecordSaysSo()
	{
		ProgressionEngine engine = LoadedEngine();
		PlayerProgress player = TestHelpers.Player();
		_ = engine.ApplyLifeRecords(player, new LifeTotals(5, 0, 0, 0, 0));
		RecordBreak broken = Records(engine.ApplyLifeRecords(player, new LifeTotals(8, 0, 0, 0, 0))).Single().Record!;

		Assert.IsTrue(broken.BeatOwn);
		Assert.AreEqual("Beat their own 5", FeedBuilder.RecordDetail(broken));
	}

	[TestMethod]
	public void AMapCountsSkillShotsAgainstHumans()
	{
		ProgressionEngine engine = LoadedEngine();
		Assert.IsEmpty(Records(engine.ApplyMapRecords(TestHelpers.Player(), 14, 5)));
		RecordBreak broken = Records(engine.ApplyMapRecords(TestHelpers.Player(), 14, 2)).Single().Record!;

		Assert.AreEqual(RecordCatalog.MapSkillShotsId, broken.Definition.Id);
		Assert.AreEqual(12, broken.Holder.Value);
	}

	[TestMethod]
	public void TheRecapCountsSkillShotsAgainstBots()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		RecapTracker recap = new(DateTime.UtcNow);
		recap.Add(engine.ApplyCombat(TestHelpers.Player(), TestHelpers.Kill("ak47") with { Distance = 2500 }));
		recap.Add(engine.ApplyCombat(TestHelpers.Player(), TestHelpers.Kill("ak47") with { Distance = 2500, VictimIsBot = true }));

		Assert.AreEqual(2, recap.SkillShots);
		Assert.AreEqual(1, recap.BotSkillShots);
	}

	[TestMethod]
	public void TheLifeTrackerTotalsEveryWeaponAndStartsOverAtDeath()
	{
		Core.Tracking.LifeTracker life = new();
		WeaponInfo ak = TestHelpers.Weapon("ak47");
		WeaponInfo deagle = TestHelpers.Weapon("deagle");
		life.Shot(ak);
		life.Shot(ak);
		life.Shot(deagle);
		life.Hit(ak, headshot: false, vsBot: true, tick: 1);
		life.Hit(deagle, headshot: true, vsBot: false, tick: 1);
		life.Kill(vsBot: true);
		life.Kill(vsBot: false);

		Assert.AreEqual(new LifeTotals(2, 1, 3, 2, 1), life.Totals);
		_ = life.EndLife();
		Assert.AreEqual(new LifeTotals(0, 0, 0, 0, 0), life.Totals);
	}

	[TestMethod]
	public void SavedRecordsAreTakenOnceAndRestoredAfterAFailedWrite()
	{
		ProgressionEngine engine = LoadedEngine();
		_ = engine.ApplyCombat(TestHelpers.Player(), RifleKill(1500));

		IReadOnlyList<RecordRow> rows = engine.Records.TakeDirty();
		Assert.HasCount(1, rows);
		Assert.IsFalse(engine.Records.IsDirty);
		engine.Records.Restore(rows);
		Assert.IsTrue(engine.Records.IsDirty);
	}

	[TestMethod]
	public void LoadingKeepsARecordBrokenWhileTheLoadWasOut()
	{
		RecordBook book = new();
		RecordDefinition kills = RecordCatalog.Get(RecordCatalog.LifeKillsId);
		book.Load([new RecordRow(kills.Id, Other, "Rival", 9, DateTime.UtcNow, null, null, null)]);

		Assert.IsNull(book.TryBreak(kills, TestHelpers.SteamId, "Tester", 9, DateTime.UtcNow));
		Assert.IsNotNull(book.TryBreak(kills, TestHelpers.SteamId, "Tester", 10, DateTime.UtcNow));
		book.Load([new RecordRow(kills.Id, Other, "Rival", 9, DateTime.UtcNow, null, null, null)]);
		Assert.AreEqual(10, book.Holder(kills.Id)!.Value);
	}

	[TestMethod]
	public async Task TheStoreKeepsOnlyRowsThatBeatTheStoredOne()
	{
		InMemoryProgressionStore store = new();
		DateTime now = DateTime.UtcNow;
		await store.SaveRecordsAsync([new RecordRow(RecordCatalog.LifeKillsId, Other, "Rival", 9, now, null, null, null)], TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveRecordsAsync([new RecordRow(RecordCatalog.LifeKillsId, TestHelpers.SteamId, "Tester", 6, now, null, null, null)], TestContext.CancellationToken).ConfigureAwait(false);

		RecordRow stored = (await store.RecordsAsync(TestContext.CancellationToken).ConfigureAwait(false)).Single();
		Assert.AreEqual(Other, stored.SteamId);
	}

	[TestMethod]
	public void TheFeedAndDiscordNameTheOldHolder()
	{
		ProgressionEngine engine = LoadedEngine();
		_ = engine.ApplyCombat(new PlayerProgress(Other) { Name = "Rival_1" }, RifleKill(1500));
		ProgressionResult result = engine.ApplyCombat(TestHelpers.Player(), RifleKill(2000));

		FeedEntry entry = FeedBuilder.From(result, TestHelpers.SteamId, "Tester", DateTime.UtcNow).Single(e => e.Kind == FeedKind.RecordBroken);
		Assert.AreEqual("Record: Longest rifle kill, 50.8 m", entry.Title);
		Assert.AreEqual("Beat Rival_1's 38.1 m", entry.Detail);
		Assert.AreEqual("Beat Rival_1's 38.1 m", FeedCaptions.For(entry, engine.Settings).Caption);

		string line = DiscordMessages.From(result, "Tester", new DiscordSettings()).Single();
		Assert.AreEqual("🏆 **Tester** broke **Rival\\_1**'s server record for **Longest rifle kill**: 50.8 m (was 38.1 m)", line);
		Assert.IsEmpty(DiscordMessages.From(result, "Tester", new DiscordSettings { Records = false }));
	}

	[TestMethod]
	public void ARecordIsTheRecapsBestMoment()
	{
		ProgressionEngine engine = LoadedEngine();
		RecapTracker recap = new(DateTime.UtcNow);
		recap.Add(engine.ApplyCombat(TestHelpers.Player(), TestHelpers.Kill("ak47") with { Distance = 2500 }));

		MapRecap finished = recap.Finish(TestHelpers.SteamId, "Tester", "de_dust2", 1, 0, DateTime.UtcNow);
		Assert.AreEqual("the server record for Longest rifle kill", finished.Best);
	}

	[TestMethod]
	public async Task TheSiteListsEveryRecordWithItsHolder()
	{
		InMemoryProgressionStore store = new();
		DateTime now = DateTime.UtcNow;
		await store.SaveRecordsAsync([new RecordRow(RecordCatalog.LifeKillsId, TestHelpers.SteamId, "Tester", 12, now, Other, "Rival", 9)], TestContext.CancellationToken).ConfigureAwait(false);
		using ProgressReader reader = new(store);

		IResult result = await RecordEndpoints.RecordsAsync(reader, TestContext.CancellationToken).ConfigureAwait(false);
		IReadOnlyList<RecordItem> items = Assert.IsInstanceOfType<Ok<IReadOnlyList<RecordItem>>>(result).Value!;

		Assert.HasCount(RecordCatalog.All.Count, items);
		RecordItem kills = items.Single(i => i.Id == RecordCatalog.LifeKillsId);
		Assert.AreEqual("Tester", kills.Holder);
		Assert.AreEqual("12", kills.Value);
		Assert.AreEqual("Rival", kills.PreviousHolder);
		Assert.AreEqual("9", kills.PreviousValue);
		Assert.IsTrue(items.Where(i => i.Id != RecordCatalog.LifeKillsId).All(i => i.Holder is null));
	}
}

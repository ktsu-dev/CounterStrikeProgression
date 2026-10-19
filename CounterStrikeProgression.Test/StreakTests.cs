// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core.Feats;
using ktsu.CounterStrikeProgression.Core.Panel;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Site.Live;
using ktsu.CounterStrikeProgression.Site.Views;

[TestClass]
public sealed class StreakTests
{
	private static readonly DateOnly Day = new(2026, 10, 17);

	public TestContext TestContext { get; set; } = null!;

	[TestMethod]
	public void Record_ExtendsOnTheNextDay_AndRestartsAfterAGap()
	{
		PlayStreak streak = default(PlayStreak).Record(Day).Record(Day).Record(Day.AddDays(1)).Record(Day.AddDays(2));
		Assert.AreEqual(3, streak.Length);
		Assert.AreEqual(3, streak.Best);

		streak = streak.Record(Day.AddDays(4));
		Assert.AreEqual(1, streak.Length);
		Assert.AreEqual(3, streak.Best);
		Assert.AreEqual(Day.AddDays(4), streak.LastDay);
	}

	[TestMethod]
	public void CurrentOn_HoldsThroughTheNextDay_ThenBreaks()
	{
		PlayStreak streak = default(PlayStreak).Record(Day).Record(Day.AddDays(1));
		Assert.AreEqual(2, streak.CurrentOn(Day.AddDays(1)));
		Assert.IsTrue(streak.PlayedOn(Day.AddDays(1)));
		Assert.AreEqual(2, streak.CurrentOn(Day.AddDays(2)));
		Assert.IsFalse(streak.PlayedOn(Day.AddDays(2)));
		Assert.AreEqual(0, streak.CurrentOn(Day.AddDays(3)));
	}

	[TestMethod]
	public void FromHistory_CountsDaysWhoseTotalRose()
	{
		DailyXp[] rows =
		[
			new(Day, 100),
			new(Day.AddDays(1), 200),
			new(Day.AddDays(2), 200),
			new(Day.AddDays(3), 300),
			new(Day.AddDays(4), 400),
		];

		PlayStreak streak = PlayStreak.FromHistory(rows, Day.ToDateTime(new TimeOnly(9, 0)));
		Assert.AreEqual(Day.AddDays(4), streak.LastDay);
		Assert.AreEqual(2, streak.Length);
		Assert.AreEqual(2, streak.Best);
	}

	[TestMethod]
	public void FromHistory_SkipsTheStartingRowOfAnOlderPlayer()
	{
		DailyXp[] rows = [new(Day, 5_000), new(Day.AddDays(1), 5_100)];

		Assert.AreEqual(1, PlayStreak.FromHistory(rows, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Length);
		Assert.AreEqual(2, PlayStreak.FromHistory(rows, Day.ToDateTime(TimeOnly.MinValue)).Length);
		Assert.AreEqual(default, PlayStreak.FromHistory([], DateTime.UtcNow));
	}

	[TestMethod]
	public void EarningRankXp_CountsTheDay_AndMovesLastSeen()
	{
		ManualTime time = new();
		ProgressionEngine engine = TestHelpers.Engine(time: time);
		PlayerProgress player = TestHelpers.Player();
		player.LastSeen = new DateTime(2026, 10, 16, 23, 0, 0, DateTimeKind.Utc);

		engine.ApplyCombat(player, TestHelpers.Kill("ak47"));
		time.Advance(TimeSpan.FromDays(1));
		engine.ApplyCombat(player, TestHelpers.Kill("ak47"));

		Assert.AreEqual(2, player.Streak.Length);
		Assert.AreEqual(new DateOnly(2026, 10, 18), player.Streak.LastDay);
		Assert.AreEqual(time.GetUtcNow().UtcDateTime, player.LastSeen);
	}

	[TestMethod]
	public void ANewStreakDay_IsAnnounced_AndAStreakFeatOnTheSessionsFirstResultToo()
	{
		ProgressionEngine engine = TestHelpers.Engine(time: new ManualTime());
		PlayerProgress player = TestHelpers.Player();
		PlayStreak streak = default;
		for (int i = FeatCatalog.RegularDays - 1; i > 0; i--)
		{
			streak = streak.Record(Day.AddDays(-i));
		}

		player.Streak = streak;
		player.RankXp = FeatCatalog.MillionaireXp;
		ProgressionResult result = engine.ApplyCombat(player, TestHelpers.Kill("ak47"));
		engine.CheckFeats(player, result);

		ProgressionNotice day = Assert.ContainsSingle(n => n.Kind == NoticeKind.StreakDay, result.Notices);
		Assert.AreEqual(FeatCatalog.RegularDays, day.Value);
		Assert.AreEqual("regular", Assert.ContainsSingle(n => n.Kind == NoticeKind.FeatEarned, result.Notices).Feat!.Id);
		Assert.Contains("millionaire", player.EarnedFeats!);

		ProgressionResult later = engine.ApplyCombat(player, TestHelpers.Kill("ak47"));
		Assert.DoesNotContain(n => n.Kind == NoticeKind.StreakDay, later.Notices);
		FeatProgress devoted = engine.Feats.Measure(player).Single(m => m.Feat.Id == "devoted").Progress;
		Assert.AreEqual(FeatCatalog.RegularDays, devoted.Current);
		Assert.AreEqual(FeatCatalog.DevotedDays, devoted.Target);
	}

	[TestMethod]
	public void TheFirstDayOfAStreak_IsNotAnnounced()
	{
		ProgressionResult result = TestHelpers.Engine(time: new ManualTime()).ApplyCombat(TestHelpers.Player(), TestHelpers.Kill("ak47"));
		Assert.DoesNotContain(n => n.Kind == NoticeKind.StreakDay, result.Notices);
	}

	[TestMethod]
	public void Overview_ShowsTheStreak_AndTheBestWhenLonger()
	{
		PanelLine line = PanelBuilder.StreakLine(default(PlayStreak).Record(Day).Record(Day.AddDays(1)), Day.AddDays(1));
		Assert.AreEqual("Play streak", line.Label);
		Assert.AreEqual("2 days", line.Detail);
		Assert.AreEqual(LineState.Done, line.State);

		PlayStreak broken = default(PlayStreak).Record(Day).Record(Day.AddDays(1)).Record(Day.AddDays(5));
		line = PanelBuilder.StreakLine(broken, Day.AddDays(6));
		Assert.AreEqual("1 day · best 2 days", line.Detail);
		Assert.AreEqual(LineState.InProgress, line.State);

		ProgressionEngine engine = TestHelpers.Engine();
		Assert.Contains(l => l.Label == "Play streak", new PanelBuilder(engine).Overview(TestHelpers.Player()).Lines);
	}

	[TestMethod]
	public async Task Load_WorksTheStreakOutFromDailyRows()
	{
		InMemoryProgressionStore store = new();
		PlayerProgress player = TestHelpers.Player();
		player.FirstSeen = Day.ToDateTime(new TimeOnly(10, 0));
		for (int i = 0; i < 3; i++)
		{
			player.RankXp += 100;
			player.LastSeen = Day.AddDays(i).ToDateTime(new TimeOnly(12, 0));
			player.IsDirty = true;
			await store.SaveAsync(ProgressSnapshot.Capture(player), TestContext.CancellationToken).ConfigureAwait(false);
		}

		PlayerProgress? loaded = await store.LoadAsync(TestHelpers.SteamId, TestContext.CancellationToken).ConfigureAwait(false);
		Assert.IsNotNull(loaded);
		Assert.AreEqual(3, loaded.Streak.Length);
		Assert.AreEqual(Day.AddDays(2), loaded.Streak.LastDay);
	}

	[TestMethod]
	public void Merge_CountsTodayWhenLiveXpIsAhead_AndThePageShowsIt()
	{
		PlayerProgress stored = TestHelpers.Player();
		stored.RankXp = 500;
		stored.Streak = default(PlayStreak).Record(Day).Record(Day.AddDays(1));
		DateTime now = Day.AddDays(2).ToDateTime(new TimeOnly(15, 0));
		ProgressSnapshot live = new(new PlayerRow(TestHelpers.SteamId, "Tester", 600, 0, stored.FirstSeen, now), [], []);

		PlayerProgress merged = ProgressMerge.Merge(stored, live);
		Assert.AreEqual(3, merged.Streak.Length);

		StreakView view = PlayerViewBuilder.Streak(merged.Streak, now);
		Assert.AreEqual(new StreakView(3, 3, true, "3-day streak"), view);
		Assert.AreEqual(new StreakView(0, 2, false, "best streak 2 days"), PlayerViewBuilder.Streak(stored.Streak, now.AddDays(2)));
		Assert.AreEqual("2-day streak, play today to keep it", PlayerViewBuilder.Streak(stored.Streak, now).Label);
		Assert.AreEqual("1-day streak, play today to keep it · best streak 2 days", PlayerViewBuilder.Streak(stored.Streak.Record(Day.AddDays(5)), Day.AddDays(6).ToDateTime(TimeOnly.MinValue)).Label);
		Assert.AreEqual("best streak 1 day", PlayerViewBuilder.Streak(default(PlayStreak).Record(Day), now).Label);
		Assert.IsNull(PlayerViewBuilder.Streak(default, now).Label);
	}
}

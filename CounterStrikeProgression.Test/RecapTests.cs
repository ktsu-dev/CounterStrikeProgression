// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Feats;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Core.Panel;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Recap;
using ktsu.CounterStrikeProgression.Core.SkillShots;
using ktsu.CounterStrikeProgression.Site;
using ktsu.CounterStrikeProgression.Site.Live;
using ktsu.CounterStrikeProgression.Site.Views;
using Microsoft.Extensions.Logging.Abstractions;

[TestClass]
public sealed class RecapTests
{
	private static readonly DateTime Start = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

	private string directory = string.Empty;

	[TestInitialize]
	public void Initialize()
	{
		directory = Path.Combine(Path.GetTempPath(), "csp-recap-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
	}

	[TestCleanup]
	public void Cleanup() => Directory.Delete(directory, recursive: true);

	private static ProgressionResult Result(double xp, params ProgressionNotice[] notices)
	{
		ProgressionResult result = new() { XpEarned = xp };
		foreach (ProgressionNotice notice in notices)
		{
			result.Notices.Add(notice);
		}

		return result;
	}

	private static ProgressionNotice Shot(Rarity rarity, string weapon = "ak47", long xp = 40) => new()
	{
		Kind = NoticeKind.SkillShot,
		SkillShot = SkillShotCatalog.All.First(s => s.Rarity == rarity),
		Weapon = TestHelpers.Weapon(weapon),
		Xp = xp,
	};

	private static MapRecap Recap(DateTime endedAt, long xp = 100, ulong steamId = TestHelpers.SteamId) =>
		new(steamId, "Tester", "de_dust2", endedAt.AddMinutes(-30), endedAt, xp, 10, 5, 2, 1, 0, 0, 0, "Wallbang with the AK-47");

	private LiveState NewState() =>
		new(new SiteOptions(), new FeedStore(Path.Combine(directory, "feed.json")), new ManualTime(), NullLogger<LiveState>.Instance);

	private static LiveBatch Batch(params MapRecap[] recaps) =>
		new(new ServerSnapshot(Start, "de_dust2", "Casual", 10, []), [], [], new ProgressionSettings(), recaps);

	[TestMethod]
	public void Tracker_AddsUpXpAndMoments()
	{
		RecapTracker tracker = new(Start);
		tracker.Add(Result(10.5, Shot(Rarity.Common)));
		tracker.Add(Result(20.75,
			Shot(Rarity.Rare),
			new ProgressionNotice { Kind = NoticeKind.WeaponLevelUp, Weapon = TestHelpers.Weapon("ak47"), Value = 4 },
			new ProgressionNotice { Kind = NoticeKind.WeaponLevelUp, Weapon = TestHelpers.Weapon("ak47"), Value = 5 },
			new ProgressionNotice { Kind = NoticeKind.TierCompleted, Weapon = TestHelpers.Weapon("ak47"), ChallengeName = "Kills", Value = 1 }));

		MapRecap recap = tracker.Finish(TestHelpers.SteamId, "Tester", "de_dust2", 12, 7, Start.AddMinutes(20));

		Assert.AreEqual(31, recap.Xp);
		Assert.AreEqual(2, recap.SkillShots);
		Assert.AreEqual(2, recap.Levels);
		Assert.AreEqual(1, recap.Tiers);
		Assert.AreEqual(12, recap.Kills);
		Assert.AreEqual(7, recap.Deaths);
		Assert.AreEqual(Start, recap.StartedAt);
		Assert.AreEqual(Start.AddMinutes(20), recap.EndedAt);
		Assert.AreEqual("AK-47 Kills I", recap.Best);
	}

	[TestMethod]
	public void Tracker_Finish_StartsTheNextSessionEmpty()
	{
		RecapTracker tracker = new(Start);
		tracker.Add(Result(5, Shot(Rarity.Common)));
		Assert.IsFalse(tracker.IsEmpty);

		tracker.Finish(TestHelpers.SteamId, "Tester", "de_dust2", 0, 0, Start.AddMinutes(5));

		Assert.IsTrue(tracker.IsEmpty);
		Assert.AreEqual(Start.AddMinutes(5), tracker.StartedAt);
		MapRecap next = tracker.Finish(TestHelpers.SteamId, "Tester", "de_inferno", 0, 0, Start.AddMinutes(9));
		Assert.AreEqual(0, next.Xp);
		Assert.AreEqual(0, next.SkillShots);
		Assert.IsNull(next.Best);
	}

	[TestMethod]
	public void Tracker_BestMoment_PrefersFeatsThenRankUpsThenEpicShots()
	{
		RecapTracker tracker = new(Start);
		tracker.Add(Result(0, new ProgressionNotice { Kind = NoticeKind.TierCompleted, Weapon = TestHelpers.Weapon("awp"), ChallengeName = "Kills", Value = 3, Xp = 900 }));
		tracker.Add(Result(0, Shot(Rarity.Epic, "awp", 10)));
		Assert.AreEqual($"{Shot(Rarity.Epic).SkillShot!.Name} with the AWP", tracker.Finish(1, "a", "m", 0, 0, Start).Best);

		tracker.Add(Result(0, Shot(Rarity.Epic), new ProgressionNotice { Kind = NoticeKind.RankUp, Value = 3 }));
		Assert.AreEqual($"ranked up to {RankLadder.Names[3]}", tracker.Finish(1, "a", "m", 0, 0, Start).Best);

		FeatDefinition feat = FeatCatalog.All[0];
		tracker.Add(Result(0, new ProgressionNotice { Kind = NoticeKind.RankUp, Value = 4 }, new ProgressionNotice { Kind = NoticeKind.FeatEarned, Feat = feat }));
		Assert.AreEqual($"the feat {feat.Name}", tracker.Finish(1, "a", "m", 0, 0, Start).Best);
	}

	[TestMethod]
	public void Tracker_BestMoment_HigherLevelWinsATie()
	{
		RecapTracker tracker = new(Start);
		tracker.Add(Result(0,
			new ProgressionNotice { Kind = NoticeKind.WeaponLevelUp, Weapon = TestHelpers.Weapon("awp"), Value = 9, Xp = 50 },
			new ProgressionNotice { Kind = NoticeKind.WeaponLevelUp, Weapon = TestHelpers.Weapon("awp"), Value = 10, Xp = 50 }));

		Assert.AreEqual("AWP level 10", tracker.Finish(1, "a", "m", 0, 0, Start).Best);
	}

	[TestMethod]
	public void RecapChat_LeavesOutWhatDidNotHappen()
	{
		MapRecap recap = new(1, "Tester", "de_dust2", Start, Start, 1240, 18, 9, 4, 1, 0, 0, 0, "Wallbang with the AK-47");

		Assert.AreEqual("This map: +1,240 XP · 18/9 K/D · 4 skill shots · 1 level · best: Wallbang with the AK-47", PanelFormatter.ToRecapChat(recap));
	}

	[TestMethod]
	public void RecapChat_NothingButXp()
	{
		MapRecap recap = new(1, "Tester", "de_dust2", Start, Start, 3, 0, 0, 0, 0, 0, 0, 0, null);

		Assert.AreEqual("This map: +3 XP", PanelFormatter.ToRecapChat(recap));
	}

	[TestMethod]
	public void LiveState_KeepsEachPlayersNewestRecap()
	{
		LiveState state = NewState();

		LiveChanges changes = state.Apply(Batch(Recap(Start.AddHours(1), xp: 200), Recap(Start, xp: 100)));
		Assert.Contains(TestHelpers.SteamId, changes.ChangedPlayers);
		Assert.AreEqual(200, state.LastSession(TestHelpers.SteamId)?.Xp);

		// A resent batch with an older recap changes nothing.
		LiveChanges again = state.Apply(Batch(Recap(Start, xp: 100)));
		Assert.IsEmpty(again.ChangedPlayers);
		Assert.AreEqual(200, state.LastSession(TestHelpers.SteamId)?.Xp);
	}

	[TestMethod]
	public void LiveState_RecapSurvivesRestart()
	{
		NewState().Apply(Batch(Recap(Start)));

		Assert.AreEqual(Recap(Start), NewState().LastSession(TestHelpers.SteamId));
	}

	[TestMethod]
	public void LiveState_BatchWithoutRecaps_IsAccepted()
	{
		LiveState state = NewState();

		LiveChanges changes = state.Apply(new LiveBatch(new ServerSnapshot(Start, "de_dust2", "Casual", 10, []), [], [], new ProgressionSettings()));

		Assert.IsEmpty(changes.ChangedPlayers);
		Assert.IsNull(state.LastSession(TestHelpers.SteamId));
	}

	[TestMethod]
	public void PlayerView_CarriesTheLastSession()
	{
		PlayerView view = PlayerViewBuilder.Build(TestHelpers.Player(), new ProgressionSettings(), online: false, lastSession: Recap(Start));

		Assert.IsNotNull(view.LastSession);
		Assert.AreEqual("de_dust2", view.LastSession.Map);
		Assert.AreEqual(100, view.LastSession.Xp);
		Assert.AreEqual("Wallbang with the AK-47", view.LastSession.Best);
		Assert.IsNull(PlayerViewBuilder.Build(TestHelpers.Player(), new ProgressionSettings(), online: false).LastSession);
	}

	[TestMethod]
	public void Tracker_TryFinish_NothingEarned_RestartsWithoutARecap()
	{
		RecapTracker tracker = new(Start);

		Assert.IsNull(tracker.TryFinish(1, "a", "m", 3, 2, Start.AddMinutes(4)));
		Assert.AreEqual(Start.AddMinutes(4), tracker.StartedAt);

		tracker.Add(Result(2));
		Assert.AreEqual(2, tracker.TryFinish(1, "a", "m", 3, 2, Start.AddMinutes(6))?.Xp);
	}

	[TestMethod]
	public void LiveState_KeepsRecapsForTheNewestPlayersOnly()
	{
		LiveState state = new(new SiteOptions { PlayerFeedPlayers = 2 }, new FeedStore(Path.Combine(directory, "feed.json")), new ManualTime(), NullLogger<LiveState>.Instance);

		LiveChanges changes = state.Apply(Batch(Recap(Start, steamId: 1), Recap(Start.AddMinutes(1), steamId: 2), Recap(Start.AddMinutes(2), steamId: 3)));

		Assert.IsNull(state.LastSession(1));
		Assert.IsNotNull(state.LastSession(2));
		Assert.IsNotNull(state.LastSession(3));
		Assert.DoesNotContain(1UL, changes.ChangedPlayers);
	}

	[TestMethod]
	public void LiveState_RecapWriteFails_KeepsItInMemory()
	{
		string blocker = Path.Combine(directory, "blocker");
		File.WriteAllText(blocker, "a file where the data folder should be");
		LiveState state = new(new SiteOptions(), new FeedStore(Path.Combine(blocker, "feed.json")), new ManualTime(), NullLogger<LiveState>.Instance);

		state.Apply(Batch(Recap(Start)));
		state.Apply(Batch(Recap(Start.AddMinutes(1), xp: 7)));

		Assert.AreEqual(7, state.LastSession(TestHelpers.SteamId)?.Xp);
	}
}

// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Discord;
using ktsu.CounterStrikeProgression.Core.Floaties;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Core.Panel;
using ktsu.CounterStrikeProgression.Core.Players;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Recap;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Site.Views;

[TestClass]
public sealed class MasteryStarTests
{
	// Levels cost 10 XP each up to level 5, so the top level is 40 XP and each star costs another 40.
	private static void SmallCurve(ProgressionSettings settings)
	{
		settings.WeaponLevelBaseXp = 10;
		settings.WeaponLevelStepXp = 0;
		settings.WeaponMaxLevel = 5;
		settings.KillTiers = [1_000_000, 2_000_000, 3_000_000, 4_000_000, 5_000_000];
	}

	private static ProgressionSettings SmallSettings()
	{
		ProgressionSettings settings = new();
		SmallCurve(settings);
		return settings;
	}

	private static ProgressionNotice Star(int star) =>
		new() { Kind = NoticeKind.MasteryStar, Weapon = TestHelpers.Weapon("ak47"), Value = star, Broadcast = true };

	[TestMethod]
	public void Curve_StarsCountEachMaxLevelsWorthOfXpPastTheTop()
	{
		WeaponLevelCurve curve = new(300, 100, 40);
		long top = curve.ThresholdFor(40);

		Assert.AreEqual(top, curve.MaxLevelXp);
		Assert.AreEqual(top, curve.StarXp);
		Assert.AreEqual(0, curve.StarsFor(0));
		Assert.AreEqual(0, curve.StarsFor(top));
		Assert.AreEqual(0, curve.StarsFor((2 * top) - 1));
		Assert.AreEqual(1, curve.StarsFor(2 * top));
		Assert.AreEqual(3, curve.StarsFor((4 * top) + 5));
		Assert.AreEqual(0, curve.ProgressToNextStar(top / 2), 1e-9);
		Assert.AreEqual(0.5, curve.ProgressToNextStar((2 * top) + (top / 2)), 1e-9);
		Assert.AreEqual(5, curve.XpTowardStar((4 * top) + 5));
	}

	[TestMethod]
	public void Curve_StarsNeverOverflow()
	{
		WeaponLevelCurve curve = new(1, 0, 2);
		Assert.AreEqual(int.MaxValue, curve.StarsFor(long.MaxValue));
	}

	[TestMethod]
	public void Engine_AnnouncesEachNewStarWithoutPayingRankXp()
	{
		ProgressionEngine engine = TestHelpers.Engine(SmallCurve);
		PlayerProgress player = TestHelpers.Player();
		WeaponProgress ak = player.Weapon(TestHelpers.Weapon("ak47").Key);
		ak.Xp = 79;
		ak.Level = 5;

		ProgressionResult result = engine.ApplyCombat(player, TestHelpers.Kill("ak47"));

		ProgressionNotice star = result.Notices.Single(n => n.Kind == NoticeKind.MasteryStar);
		Assert.AreEqual(1, star.Value);
		Assert.AreEqual(0, star.Xp);
		Assert.IsTrue(star.Broadcast);
		Assert.AreEqual("AK-47", star.Weapon?.DisplayName);
		Assert.AreEqual(5, ak.Level);

		ProgressionResult next = engine.ApplyCombat(player, TestHelpers.Kill("ak47"));
		Assert.IsFalse(next.Notices.Any(n => n.Kind == NoticeKind.MasteryStar));
	}

	[TestMethod]
	public void Engine_NoStarsBeforeTheTopLevel()
	{
		ProgressionEngine engine = TestHelpers.Engine(SmallCurve);
		PlayerProgress player = TestHelpers.Player();

		List<ProgressionNotice> notices = [];
		for (int i = 0; i < 3; i++)
		{
			notices.AddRange(engine.ApplyCombat(player, TestHelpers.Kill("ak47")).Notices);
		}

		Assert.IsFalse(notices.Any(n => n.Kind == NoticeKind.MasteryStar));
	}

	[TestMethod]
	public void Panel_WeaponPageShowsStarsAndTheNextStar()
	{
		ProgressionEngine engine = TestHelpers.Engine(SmallCurve);
		PlayerProgress player = TestHelpers.Player();
		WeaponProgress ak = player.Weapon(TestHelpers.Weapon("ak47").Key);
		ak.Xp = 40 + 80 + 10;
		ak.Level = 5;

		PanelPage page = new PanelBuilder(engine).Weapon(player, TestHelpers.Weapon("ak47"));

		Assert.AreEqual("Level 5 · 2 stars", page.Subtitle);
		Assert.AreEqual("10 / 40 XP to star 3", page.ProgressLabel);
		Assert.AreEqual(0.25, page.Progress, 1e-9);
	}

	[TestMethod]
	public void Panel_WeaponPageWithoutStarsKeepsThePlainLevel()
	{
		ProgressionEngine engine = TestHelpers.Engine(SmallCurve);
		PlayerProgress player = TestHelpers.Player();
		player.Weapon(TestHelpers.Weapon("ak47").Key).Xp = 15;

		PanelPage page = new PanelBuilder(engine).Weapon(player, TestHelpers.Weapon("ak47"));

		Assert.AreEqual("Level 2", page.Subtitle);
	}

	[TestMethod]
	public void Notices_StarIsNamedInTheFeedFloatiesRecapAndDiscord()
	{
		ProgressionResult result = new();
		result.Notices.Add(Star(2));

		FeedEntry entry = FeedBuilder.From(result, 1, "Tester", DateTime.UnixEpoch).Single();
		Assert.AreEqual(FeedKind.MasteryStar, entry.Kind);
		Assert.AreEqual("AK-47 mastery star 2", entry.Title);

		Assert.AreEqual("AK-47 mastery star 2", FloatieMerger.Labels(result).Single());
		Assert.AreEqual("AK-47 mastery star 2", RecapTracker.Describe(result.Notices[0]));

		Assert.AreEqual("⭐ **Tester**'s AK-47 earned mastery star **2**!", DiscordMessages.From(result, "Tester", new DiscordSettings()).Single());
		Assert.IsEmpty(DiscordMessages.From(result, "Tester", new DiscordSettings { MasteryStars = false }));
	}

	[TestMethod]
	public void Site_PlayerPageAndWeaponBoardCarryStars()
	{
		ProgressionSettings settings = SmallSettings();
		PlayerProgress player = TestHelpers.Player();
		WeaponProgress ak = player.Weapon(TestHelpers.Weapon("ak47").Key);
		ak.Xp = 40 + 40 + 20;
		ak.Level = 5;
		ak.Kills = 1;

		WeaponView view = PlayerViewBuilder.Build(player, settings, online: false).Weapons.Single();
		Assert.AreEqual(1, view.Stars);
		Assert.AreEqual(0.5, view.StarProgress, 1e-9);

		LeaderboardsView boards = LeaderboardViewBuilder.Build([PlayerTally.From(player, settings)], null);
		Assert.AreEqual(1, boards.Weapons.Single(w => w.Key == "ak47").Rows.Single().Stars);
	}

	[TestMethod]
	public void Chat_StatsAndTopNameTheStars()
	{
		ProgressionEngine engine = TestHelpers.Engine(SmallCurve);
		ChatReports reports = new(engine);
		PlayerProgress player = TestHelpers.Player();
		WeaponProgress ak = player.Weapon(TestHelpers.Weapon("ak47").Key);
		ak.Xp = 40 + 80;
		ak.Level = 5;

		Assert.Contains("Best weapon AK-47, level 5, 2 stars.", reports.Stats("Tester", player));
		Assert.AreEqual("1. P1 level 5, 1 star (80 XP)", reports.WeaponLeader(1, new WeaponLeaderboardEntry("P1", 80, 5)));
		Assert.AreEqual("2. P2 level 3 (25 XP)", reports.WeaponLeader(2, new WeaponLeaderboardEntry("P2", 25, 3)));
	}

	[TestMethod]
	public void WeaponLeaderMarksABot()
	{
		ChatReports reports = new(TestHelpers.Engine(SmallCurve));

		Assert.AreEqual("1. Muhlik [BOT] level 3 (25 XP)", reports.WeaponLeader(1, new WeaponLeaderboardEntry("Muhlik", 25, 3, BotIds.For("Muhlik"))));
	}
}

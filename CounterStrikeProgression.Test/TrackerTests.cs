// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Events;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Tracking;

[TestClass]
public sealed class TrackerTests
{
	private const int TeamA = 2;
	private const int TeamB = 3;

	[TestMethod]
	public void OnlyTheFirstKillOfTheRoundIsTheOpeningKill()
	{
		RoundTracker round = new(new ProgressionSettings());
		round.StartRound();

		Assert.IsTrue(round.RegisterKill(1, TeamA, 10, TeamB, 5.0, 100).IsOpeningKill);
		Assert.IsFalse(round.RegisterKill(2, TeamA, 11, TeamB, 6.0, 200).IsOpeningKill);

		round.StartRound();
		Assert.IsTrue(round.RegisterKill(2, TeamA, 11, TeamB, 50.0, 900).IsOpeningKill);
	}

	[TestMethod]
	public void KillsChainWhileEachIsInsideTheWindow()
	{
		RoundTracker round = new(new ProgressionSettings { MultiKillSeconds = 4 });
		round.StartRound();

		Assert.AreEqual(1, round.RegisterKill(1, TeamA, 10, TeamB, 0.0, 1).ChainedKills);
		Assert.AreEqual(2, round.RegisterKill(1, TeamA, 11, TeamB, 3.0, 2).ChainedKills);
		Assert.AreEqual(3, round.RegisterKill(1, TeamA, 12, TeamB, 6.5, 3).ChainedKills);
		Assert.AreEqual(1, round.RegisterKill(1, TeamA, 13, TeamB, 11.0, 4).ChainedKills);
	}

	[TestMethod]
	public void KillsOnTheSameTickAreACollateral()
	{
		RoundTracker round = new(new ProgressionSettings());
		round.StartRound();

		Assert.AreEqual(1, round.RegisterKill(1, TeamA, 10, TeamB, 1.0, 64).KillsWithThisBullet);
		Assert.AreEqual(2, round.RegisterKill(1, TeamA, 11, TeamB, 1.0, 64).KillsWithThisBullet);
		Assert.AreEqual(1, round.RegisterKill(1, TeamA, 12, TeamB, 1.1, 70).KillsWithThisBullet);
	}

	[TestMethod]
	public void KillingATeammatesKillerQuicklyIsATrade()
	{
		RoundTracker round = new(new ProgressionSettings { TradeSeconds = 3 });
		round.StartRound();

		round.RegisterKill(10, TeamB, 1, TeamA, 1.0, 1);
		Assert.IsTrue(round.RegisterKill(2, TeamA, 10, TeamB, 2.5, 2).IsTrade);

		round.RegisterKill(11, TeamB, 3, TeamA, 10.0, 3);
		Assert.IsFalse(round.RegisterKill(4, TeamA, 11, TeamB, 14.0, 4).IsTrade);
	}

	[TestMethod]
	public void AvengingYourselfIsNotATrade()
	{
		RoundTracker round = new(new ProgressionSettings());
		round.StartRound();

		round.RegisterKill(10, TeamB, 1, TeamA, 1.0, 1);
		Assert.IsFalse(round.RegisterKill(1, TeamA, 10, TeamB, 2.0, 2).IsTrade);
	}

	[TestMethod]
	public void TheLastPlayerAliveClutchesWhenTheirTeamWins()
	{
		RoundTracker round = new(new ProgressionSettings());
		round.StartRound();
		round.NoteLastAlive(1, TeamA, 3);
		round.NoteLastAlive(1, TeamA, 1);

		(int Player, int Enemies)? clutch = round.Clutch(TeamA, _ => true);
		Assert.AreEqual((1, 3), clutch);
		Assert.IsNull(round.Clutch(TeamB, _ => true));
		Assert.IsNull(round.Clutch(TeamA, _ => false));
	}

	[TestMethod]
	public void LandingIsTimedFromTheTickTheGroundIsReached()
	{
		PlayerCombatTracker tracker = new();
		tracker.Tick(1.0, 0, onGround: false);
		tracker.Tick(1.5, 0, onGround: true);

		Assert.AreEqual(0.1, tracker.Read(1.6).SecondsSinceLanded, 1e-9);
	}

	[TestMethod]
	public void ScopeTimeResetsWhenUnscopedOrSwitching()
	{
		PlayerCombatTracker tracker = new();
		tracker.ScopeChanged(2.0, scoped: true);
		Assert.AreEqual(0.25, tracker.Read(2.25).SecondsSinceScoped, 1e-9);

		tracker.ScopeChanged(3.0, scoped: false);
		Assert.IsTrue(double.IsPositiveInfinity(tracker.Read(3.1).SecondsSinceScoped));

		tracker.ScopeChanged(4.0, scoped: true);
		tracker.WeaponSwitched(4.5);
		Assert.IsTrue(double.IsPositiveInfinity(tracker.Read(4.6).SecondsSinceScoped));
		Assert.AreEqual(0.1, tracker.Read(4.6).SecondsSinceWeaponSwitch, 1e-9);
	}

	[TestMethod]
	public void OnlyTheFirstShotAfterAReloadCounts()
	{
		PlayerCombatTracker tracker = new();
		tracker.ReloadStarted();
		tracker.ShotFired(1);
		Assert.IsTrue(tracker.Read(0).FirstShotAfterReload);

		tracker.ShotFired(2);
		Assert.IsFalse(tracker.Read(0).FirstShotAfterReload);
	}

	[TestMethod]
	public void KillsCountTowardTheCurrentSprayUntilItResets()
	{
		PlayerCombatTracker tracker = new();
		tracker.ShotFired(1);
		Assert.AreEqual(0, tracker.Read(0).EarlierKillsInSpray);
		tracker.KillMade();
		tracker.ShotFired(4);
		Assert.AreEqual(1, tracker.Read(0).EarlierKillsInSpray);

		tracker.ShotFired(1);
		Assert.AreEqual(0, tracker.Read(0).EarlierKillsInSpray);
	}

	[TestMethod]
	public void KillsCountTowardTheLife_AndTheUnhurtRunStartsOverWhenHurt()
	{
		PlayerCombatTracker tracker = new();
		tracker.KillMade();
		tracker.KillMade();
		Assert.AreEqual(2, tracker.Read(0).EarlierLifeKills);
		Assert.AreEqual(2, tracker.Read(0).EarlierKillsUnhurt);

		tracker.Hurt(1);
		tracker.KillMade();
		Assert.AreEqual(3, tracker.Read(2).EarlierLifeKills);
		Assert.AreEqual(1, tracker.Read(2).EarlierKillsUnhurt);

		tracker.Reset();
		Assert.AreEqual(0, tracker.Read(3).EarlierLifeKills);
		Assert.AreEqual(0, tracker.Read(3).EarlierKillsUnhurt);
	}

	[TestMethod]
	public void FlicksMeasureTheLargestRecentTurnAcrossTheWrap()
	{
		PlayerCombatTracker tracker = new();
		tracker.Tick(0.00, 170, onGround: true);
		tracker.Tick(0.01, 175, onGround: true);
		tracker.Tick(0.02, -100, onGround: true);

		Assert.AreEqual(90, tracker.Read(0.02).RecentTurnDegrees, 1e-9);
	}

	[TestMethod]
	public void OldAimFallsOutOfTheFlickWindow()
	{
		PlayerCombatTracker tracker = new();
		tracker.Tick(0.00, 0, onGround: true);
		for (int i = 1; i <= PlayerCombatTracker.TurnHistoryTicks + 1; i++)
		{
			tracker.Tick(i * 0.01, 90, onGround: true);
		}

		Assert.AreEqual(0, tracker.Read(1).RecentTurnDegrees, 1e-9);
	}

	[TestMethod]
	[DataRow(10.0, 350.0, 20.0)]
	[DataRow(-170.0, 170.0, 20.0)]
	[DataRow(0.0, 180.0, 180.0)]
	[DataRow(45.0, 45.0, 0.0)]
	public void AngleBetweenTakesTheShortWay(double a, double b, double expected) =>
		Assert.AreEqual(expected, PlayerCombatTracker.AngleBetween(a, b), 1e-9);

	[TestMethod]
	public void HitsOnTheSameTickCountOnce()
	{
		LifeTracker life = new();
		Core.Weapons.WeaponInfo nova = TestHelpers.Weapon("nova");
		life.Shot(nova);
		life.Hit(nova, headshot: false, vsBot: false, vsTeammate: false, tick: 10);
		life.Hit(nova, headshot: false, vsBot: false, vsTeammate: false, tick: 10);
		life.Hit(nova, headshot: true, vsBot: false, vsTeammate: false, tick: 10);

		WeaponLifeStats stats = life.EndLife().Single();
		Assert.AreEqual(1, stats.Shots);
		Assert.AreEqual(1, stats.Hits);
	}

	[TestMethod]
	public void LivesReportEachWeaponAndClearAfterwards()
	{
		LifeTracker life = new();
		Core.Weapons.WeaponInfo ak = TestHelpers.Weapon("ak47");
		Core.Weapons.WeaponInfo deagle = TestHelpers.Weapon("deagle");
		life.Shot(ak);
		life.Shot(ak);
		life.Hit(ak, headshot: true, vsBot: true, vsTeammate: false, tick: 1);
		life.Shot(deagle);

		IReadOnlyList<WeaponLifeStats> stats = life.EndLife();
		Assert.HasCount(2, stats);
		WeaponLifeStats akStats = stats.Single(s => s.Weapon == ak);
		Assert.AreEqual(2, akStats.Shots);
		Assert.AreEqual(1, akStats.HeadshotHits);
		Assert.IsTrue(akStats.MostlyBots);
		Assert.IsEmpty(life.EndLife());
	}

	[TestMethod]
	public void TeammateHitsCountAsShotsButNotHits()
	{
		LifeTracker life = new();
		Core.Weapons.WeaponInfo ak = TestHelpers.Weapon("ak47");
		for (int tick = 1; tick <= 10; tick++)
		{
			life.Shot(ak);
		}

		for (int tick = 1; tick <= 4; tick++)
		{
			life.Hit(ak, headshot: false, vsBot: false, vsTeammate: false, tick: tick);
		}

		for (int tick = 5; tick <= 8; tick++)
		{
			life.Hit(ak, headshot: true, vsBot: true, vsTeammate: true, tick: tick);
		}

		Assert.AreEqual(new LifeTotals(0, 0, 10, 4, 0), life.Totals);
		WeaponLifeStats stats = life.EndLife().Single();
		Assert.AreEqual(10, stats.Shots);
		Assert.AreEqual(4, stats.Hits);
		Assert.AreEqual(0, stats.HeadshotHits);
		Assert.IsFalse(stats.MostlyBots);

		ProgressionEngine engine = TestHelpers.Engine();
		Assert.IsEmpty(TestHelpers.SkillShots(engine.ApplyLifeEnd(TestHelpers.Player(), stats)));
	}

	[TestMethod]
	public void ATeammateHitDoesNotHideAnEnemyHitOnTheSameTick()
	{
		LifeTracker life = new();
		Core.Weapons.WeaponInfo ak = TestHelpers.Weapon("ak47");
		life.Shot(ak);
		life.Hit(ak, headshot: false, vsBot: false, vsTeammate: true, tick: 3);
		life.Hit(ak, headshot: true, vsBot: false, vsTeammate: false, tick: 3);

		WeaponLifeStats stats = life.EndLife().Single();
		Assert.AreEqual(1, stats.Hits);
		Assert.AreEqual(1, stats.HeadshotHits);
	}
}

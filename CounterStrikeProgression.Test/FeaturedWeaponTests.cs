// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Challenges;
using ktsu.CounterStrikeProgression.Core.Events;
using ktsu.CounterStrikeProgression.Core.Panel;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Rotation;
using ktsu.CounterStrikeProgression.Core.Weapons;
using ktsu.CounterStrikeProgression.Site;

[TestClass]
public sealed class FeaturedWeaponTests
{
	private static readonly DateOnly Day = new(2026, 10, 17);

	private static ProgressionEngine FeaturedEngine(double multiplier = 2) =>
		TestHelpers.Engine(s => s.FeaturedWeaponMultiplier = multiplier, new ManualTime());

	private static WeaponInfo FeaturedWeaponToday() => FeaturedWeapon.Weapons(DailyRotation.FeaturedClass(Day))[0];

	private static WeaponInfo OtherWeaponToday() =>
		WeaponCatalog.All.First(w => w.IsGun && (w.Class != DailyRotation.FeaturedClass(Day)));

	private static CombatEvent Kill(WeaponInfo weapon) => TestHelpers.Kill(weapon.Key.WeakString);

	[TestMethod]
	public void TheFeaturedClassEarnsDoubleWeaponXpButTheSameRankXp()
	{
		ProgressionEngine engine = FeaturedEngine();
		WeaponInfo featured = FeaturedWeaponToday();
		WeaponInfo other = OtherWeaponToday();
		PlayerProgress player = TestHelpers.Player();

		ProgressionResult featuredResult = engine.ApplyCombat(player, Kill(featured));
		ProgressionResult otherResult = engine.ApplyCombat(player, Kill(other));

		long killXp = engine.Settings.KillXp;
		Assert.AreEqual(killXp * 2, player.Weapons[featured.Key].Xp);
		Assert.AreEqual(killXp, player.Weapons[other.Key].Xp);
		Assert.AreEqual(otherResult.XpGained, featuredResult.XpGained);
	}

	[TestMethod]
	public void TheFeaturedClassMovesItsLaddersTwiceAsFast()
	{
		ProgressionEngine engine = FeaturedEngine();
		WeaponInfo featured = FeaturedWeaponToday();
		PlayerProgress player = TestHelpers.Player();

		ProgressionResult result = new();
		for (int i = 0; i < 13; i++)
		{
			result = engine.ApplyCombat(player, Kill(featured));
		}

		ChallengeProgress kills = player.Weapons[featured.Key].Challenge(ChallengeDefinition.KillsId);
		Assert.AreEqual(26, kills.Count);
		Assert.AreEqual(1, kills.TierReached);
		Assert.AreEqual(13, player.Weapons[featured.Key].Kills);
		Assert.Contains(n => n.Kind == NoticeKind.TierCompleted, result.Notices);
	}

	[TestMethod]
	public void WeaponXpAgainstBotsIsStillMultiplied()
	{
		ProgressionEngine engine = FeaturedEngine();
		WeaponInfo featured = FeaturedWeaponToday();
		PlayerProgress player = TestHelpers.Player();

		engine.ApplyCombat(player, Kill(featured) with { VictimIsBot = true });

		Assert.AreEqual(engine.Settings.KillXp * 2L, player.Weapons[featured.Key].Xp);
	}

	[TestMethod]
	public void AMultiplierOfOneTurnsItOff()
	{
		ProgressionEngine engine = FeaturedEngine(multiplier: 1);
		WeaponInfo weapon = FeaturedWeaponToday();
		PlayerProgress player = TestHelpers.Player();

		engine.ApplyCombat(player, Kill(weapon));

		Assert.IsNull(engine.FeaturedClass);
		Assert.IsFalse(engine.IsFeatured(weapon));
		Assert.AreEqual(engine.Settings.KillXp, player.Weapons[weapon.Key].Xp);
		Assert.AreEqual(1, player.Weapons[weapon.Key].Challenge(ChallengeDefinition.KillsId).Count);
	}

	[TestMethod]
	public void ThePickFollowsTheUtcDay()
	{
		ManualTime time = new();
		ProgressionEngine engine = TestHelpers.Engine(s => s.FeaturedWeaponMultiplier = 2, time);
		Assert.AreEqual(DailyRotation.FeaturedClass(Day), engine.FeaturedClass);

		time.Advance(TimeSpan.FromHours(12));
		Assert.AreEqual(DailyRotation.FeaturedClass(Day.AddDays(1)), engine.FeaturedClass);
	}

	[TestMethod]
	public void ThePickIsAlwaysAGunClassAndReachesEveryOne()
	{
		HashSet<WeaponClass> seen = [];
		for (int i = 0; i < 120; i++)
		{
			WeaponClass pick = DailyRotation.FeaturedClass(Day.AddDays(i));
			Assert.AreEqual(pick, DailyRotation.FeaturedClass(Day.AddDays(i)));
			Assert.Contains(pick, DailyRotation.FeaturedClasses);
			seen.Add(pick);
		}

		Assert.HasCount(DailyRotation.FeaturedClasses.Count, seen);
		Assert.IsTrue(DailyRotation.FeaturedClasses.All(c => FeaturedWeapon.Weapons(c).Count > 0));
	}

	[TestMethod]
	public void WeeksAreIsoWeeksStartingOnMonday()
	{
		Assert.AreEqual("2026-10-17", DailyRotation.DayKey(Day));
		Assert.AreEqual("2026-W42", DailyRotation.WeekKey(Day));
		Assert.AreEqual(new DateOnly(2026, 10, 12), DailyRotation.WeekStart(Day));
		Assert.AreEqual(new DateOnly(2026, 10, 12), DailyRotation.WeekStart(new DateOnly(2026, 10, 12)));
		Assert.AreEqual("2026-W53", DailyRotation.WeekKey(new DateOnly(2027, 1, 1)));
	}

	[TestMethod]
	public void EveryClassHasAPluralName()
	{
		Dictionary<WeaponClass, string> expected = new()
		{
			[WeaponClass.Pistol] = "pistols",
			[WeaponClass.Smg] = "SMGs",
			[WeaponClass.Rifle] = "rifles",
			[WeaponClass.Sniper] = "snipers",
			[WeaponClass.Shotgun] = "shotguns",
			[WeaponClass.MachineGun] = "machine guns",
			[WeaponClass.Knife] = "knives",
			[WeaponClass.Grenade] = "grenades",
			[WeaponClass.Taser] = "the Zeus",
			[WeaponClass.Unknown] = "weapons",
		};

		foreach ((WeaponClass weaponClass, string name) in expected)
		{
			Assert.AreEqual(name, DailyRotation.ClassName(weaponClass));
		}
	}

	[TestMethod]
	public void TheHashIsStableAndSalted()
	{
		Assert.AreEqual(DailyRotation.Hash("2026-10-17", "featured"), DailyRotation.Hash("2026-10-17", "featured"));
		Assert.AreNotEqual(DailyRotation.Hash("2026-10-17", "featured"), DailyRotation.Hash("2026-10-17", "contracts"));
	}

	[TestMethod]
	public void TheLadderStepRoundsTheMultiplier()
	{
		Assert.AreEqual(1, FeaturedWeapon.LadderStep(1));
		Assert.AreEqual(2, FeaturedWeapon.LadderStep(1.5));
		Assert.AreEqual(3, FeaturedWeapon.LadderStep(3));
		Assert.AreEqual(1, FeaturedWeapon.LadderStep(0.5));
	}

	[TestMethod]
	public void ThePanelShowsTheFeaturedClass()
	{
		ProgressionEngine engine = FeaturedEngine();
		PanelBuilder panel = new(engine);
		PlayerProgress player = TestHelpers.Player();
		string name = DailyRotation.ClassName(DailyRotation.FeaturedClass(Day));

		PanelPage overview = panel.Overview(player);
		PanelPage featured = panel.Weapon(player, FeaturedWeaponToday());
		PanelPage other = panel.Weapon(player, OtherWeaponToday());

		Assert.AreEqual($"Featured: {char.ToUpperInvariant(name[0])}{name[1..]}", overview.Lines[0].Label);
		Assert.AreEqual("×2 weapon XP", overview.Lines[0].Detail);
		Assert.AreEqual("Level 1 · ×2 today", featured.Subtitle);
		Assert.AreEqual("Level 1", other.Subtitle);
	}

	[TestMethod]
	public void ThePanelLeavesTheLineOutWhenOff()
	{
		PanelPage overview = new PanelBuilder(FeaturedEngine(multiplier: 1)).Overview(TestHelpers.Player());

		Assert.AreEqual("Kills", overview.Lines[0].Label);
	}

	[TestMethod]
	public void TheSiteDescribesTheFeaturedClass()
	{
		WeaponClass pick = DailyRotation.FeaturedClass(Day);

		FeaturedView? view = Endpoints.Featured(new ProgressionSettings { FeaturedWeaponMultiplier = 2 }, new ManualTime());

		Assert.IsNotNull(view);
		Assert.AreEqual("2026-10-17", view.Day);
		Assert.AreEqual(2, view.Multiplier);
		CollectionAssert.AreEqual(FeaturedWeapon.Weapons(pick).Select(w => w.DisplayName).ToList(), view.Weapons.ToList());
		Assert.IsNull(Endpoints.Featured(new ProgressionSettings { FeaturedWeaponMultiplier = 1 }, new ManualTime()));
	}
}

// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core.Events;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.SkillShots;

[TestClass]
public sealed class SkillShotTests
{
	[TestMethod]
	public void ThereAreHeapsOfSkillShots() => Assert.IsGreaterThanOrEqualTo(45, SkillShotCatalog.All.Count);

	[TestMethod]
	public void IdsAreUnique()
	{
		List<string> ids = [.. SkillShotCatalog.All.Select(s => s.Id.WeakString)];
		Assert.HasCount(ids.Count, ids.Distinct());
	}

	[TestMethod]
	public void HitKillAndChickenSkillShotsHaveADetectorAndOthersDoNot()
	{
		foreach (SkillShotDefinition skillShot in SkillShotCatalog.All)
		{
			bool combat = skillShot.Trigger is SkillTrigger.Hit or SkillTrigger.Kill or SkillTrigger.Chicken;
			Assert.AreEqual(combat, skillShot.Detect is not null, skillShot.Id.WeakString);
		}
	}

	[TestMethod]
	public void EverySkillShotAppliesToAtLeastOneWeapon()
	{
		foreach (SkillShotDefinition skillShot in SkillShotCatalog.All)
		{
			Assert.IsTrue(Core.Weapons.WeaponCatalog.All.Any(skillShot.AppliesTo), skillShot.Id.WeakString);
		}
	}

	[TestMethod]
	public void APlainKillEarnsNoSkillShots() => Assert.IsEmpty(Earned(TestHelpers.Kill("ak47")));

	[TestMethod]
	public void APlainHitEarnsNoSkillShots() => Assert.IsEmpty(Earned(TestHelpers.Hit("ak47")));

	[TestMethod]
	public void MovingTargetNeedsAVictimCrossingTheView()
	{
		Assert.Contains("moving_target", Earned(TestHelpers.Hit("ak47") with { VictimSidewaysSpeed = 200 }));
		Assert.DoesNotContain("moving_target", Earned(TestHelpers.Hit("ak47") with { VictimSidewaysSpeed = 120, Victim = new PlayerState { Speed = 250 } }));
	}

	[TestMethod]
	public void ACollateralIsNotAlsoASprayTransfer()
	{
		List<string> earned = Earned(TestHelpers.Kill("ak47") with { EarlierKillsInSpray = 1, KillsWithThisBullet = 2 });
		Assert.Contains("collateral", earned);
		Assert.DoesNotContain("spray_transfer", earned);
	}

	[TestMethod]
	public void AirborneHitsAndKills()
	{
		PlayerState airborne = new() { OnGround = false };
		Assert.Contains("airborne_target", Earned(TestHelpers.Hit("ak47") with { Victim = airborne }));
		Assert.Contains("jumpshot", Earned(TestHelpers.Hit("ak47") with { Attacker = airborne }));
		Assert.Contains("air_to_air", Earned(TestHelpers.Kill("ak47") with { Attacker = airborne, Victim = airborne }));
		Assert.DoesNotContain("air_to_air", Earned(TestHelpers.Kill("ak47") with { Attacker = airborne }));
	}

	[TestMethod]
	public void OnALadderIsNotAirborne() =>
		Assert.DoesNotContain("jumpshot", Earned(TestHelpers.Hit("ak47") with { Attacker = new PlayerState { OnGround = false, OnLadder = true } }));

	[TestMethod]
	public void ScopeSkillShotsOnlyApplyToScopedWeapons()
	{
		Assert.Contains("no_scope", Earned(TestHelpers.Kill("awp") with { NoScope = true }));
		Assert.DoesNotContain("no_scope", Earned(TestHelpers.Kill("ak47") with { NoScope = true }));
		Assert.Contains("quickscope", Earned(TestHelpers.Kill("awp") with { SecondsSinceScoped = 0.2 }));
		Assert.DoesNotContain("quickscope", Earned(TestHelpers.Kill("awp") with { SecondsSinceScoped = 1.0 }));
		Assert.Contains("flying_no_scope", Earned(TestHelpers.Kill("ssg08") with { NoScope = true, Attacker = new PlayerState { OnGround = false } }));
	}

	[TestMethod]
	public void DistanceSkillShots()
	{
		Assert.Contains("long_shot", Earned(TestHelpers.Kill("ak47") with { Distance = 2500 }));
		Assert.DoesNotContain("long_shot", Earned(TestHelpers.Kill("awp") with { Distance = 2200 }));
		Assert.Contains("point_blank", Earned(TestHelpers.Kill("ak47") with { Distance = 100 }));
		Assert.DoesNotContain("point_blank", Earned(TestHelpers.Kill("nova") with { Distance = 100 }));
		Assert.Contains("pistol_sniper", Earned(TestHelpers.Kill("deagle") with { Distance = 1600, IsHeadshot = true }));
	}

	[TestMethod]
	public void PenetrationAndVisibility()
	{
		Assert.Contains("wallbang_hit", Earned(TestHelpers.Hit("ak47") with { Penetrations = 1 }));
		Assert.Contains("wallbang", Earned(TestHelpers.Kill("ak47") with { Penetrations = 1 }));
		Assert.DoesNotContain("double_wallbang", Earned(TestHelpers.Kill("ak47") with { Penetrations = 1 }));
		Assert.Contains("double_wallbang", Earned(TestHelpers.Kill("ak47") with { Penetrations = 2 }));
		Assert.Contains("through_smoke", Earned(TestHelpers.Kill("ak47") with { ThroughSmoke = true }));
		Assert.Contains("blind_kill", Earned(TestHelpers.Kill("ak47") with { AttackerBlind = true }));
	}

	[TestMethod]
	public void UtilityAndMelee()
	{
		Assert.Contains("grenade_kill", Earned(TestHelpers.Kill("hegrenade")));
		Assert.Contains("fire_kill", Earned(TestHelpers.Kill("molotov")));
		Assert.Contains("grenade_impact", Earned(TestHelpers.Kill("decoy")));
		Assert.Contains("knife_kill", Earned(TestHelpers.Kill("knife_karambit")));
		Assert.Contains("backstab", Earned(TestHelpers.Kill("knife") with { Damage = 180 }));
		Assert.DoesNotContain("backstab", Earned(TestHelpers.Kill("knife") with { Damage = 55 }));
		Assert.Contains("zeus_kill", Earned(TestHelpers.Kill("taser")));
	}

	[TestMethod]
	public void TimingAndAim()
	{
		Assert.Contains("quick_switch", Earned(TestHelpers.Kill("deagle") with { SecondsSinceWeaponSwitch = 0.3 }));
		Assert.Contains("fresh_mag", Earned(TestHelpers.Kill("ak47") with { FirstShotAfterReload = true }));
		Assert.Contains("last_bullet", Earned(TestHelpers.Kill("ak47") with { ClipAfterShot = 0 }));
		Assert.Contains("one_tap", Earned(TestHelpers.Kill("ak47") with { IsHeadshot = true, ShotsInSpray = 1 }));
		Assert.DoesNotContain("one_tap", Earned(TestHelpers.Kill("ak47") with { IsHeadshot = true, ShotsInSpray = 3 }));
		Assert.Contains("flick", Earned(TestHelpers.Kill("ak47") with { RecentTurnDegrees = 90 }));
		Assert.Contains("spray_transfer", Earned(TestHelpers.Kill("ak47") with { EarlierKillsInSpray = 1 }));
		Assert.DoesNotContain("spray_transfer", Earned(TestHelpers.Kill("deagle") with { EarlierKillsInSpray = 1 }));
	}

	[TestMethod]
	public void MovementKills()
	{
		Assert.Contains("run_and_gun", Earned(TestHelpers.Kill("ak47") with { Attacker = new PlayerState { Speed = 200 } }));
		Assert.DoesNotContain("run_and_gun", Earned(TestHelpers.Kill("mp9") with { Attacker = new PlayerState { Speed = 200 } }));
		Assert.Contains("crouch_kill", Earned(TestHelpers.Kill("ak47") with { Attacker = new PlayerState { Ducked = true } }));
		Assert.Contains("ladder_kill", Earned(TestHelpers.Kill("ak47") with { Attacker = new PlayerState { OnLadder = true, OnGround = false } }));
		Assert.Contains("silent_kill", Earned(TestHelpers.Kill("ak47") with { Attacker = new PlayerState { Walking = true } }));
		Assert.Contains("landing_shot", Earned(TestHelpers.Kill("ak47") with { SecondsSinceAttackerLanded = 0.1 }));
		Assert.Contains("water_kill", Earned(TestHelpers.Kill("ak47") with { Attacker = new PlayerState { InWater = true } }));
		Assert.Contains("scoped_on_the_move", Earned(TestHelpers.Kill("awp") with { Attacker = new PlayerState { Speed = 150, Scoped = true } }));
	}

	[TestMethod]
	public void SituationalKills()
	{
		Assert.Contains("double_kill", Earned(TestHelpers.Kill("ak47") with { ChainedKills = 2 }));
		Assert.Contains("triple_kill", Earned(TestHelpers.Kill("ak47") with { ChainedKills = 3 }));
		Assert.Contains("rampage", Earned(TestHelpers.Kill("ak47") with { ChainedKills = 6 }));
		Assert.Contains("collateral", Earned(TestHelpers.Kill("awp") with { KillsWithThisBullet = 2 }));
		Assert.Contains("opening_kill", Earned(TestHelpers.Kill("ak47") with { IsOpeningKill = true }));
		Assert.Contains("trade", Earned(TestHelpers.Kill("ak47") with { IsTrade = true }));
		Assert.Contains("revenge", Earned(TestHelpers.Kill("ak47") with { IsRevenge = true }));
		Assert.Contains("last_stand", Earned(TestHelpers.Kill("ak47") with { Attacker = new PlayerState { Health = 4 } }));
		Assert.Contains("bounce_back", Earned(TestHelpers.Kill("ak47") with { SecondsSinceAttackerHurt = 0.5 }));
		Assert.Contains("defuse_denial", Earned(TestHelpers.Kill("ak47") with { Victim = new PlayerState { Defusing = true } }));
	}

	[TestMethod]
	public void SkillShotsPayByRarity()
	{
		ProgressionEngine engine = TestHelpers.Engine(s => s.EpicSkillTiers = [1000, 2000, 3000, 4000, 5000]);
		PlayerProgress player = TestHelpers.Player();

		ProgressionResult result = engine.ApplyCombat(player, TestHelpers.Kill("ak47") with { AttackerBlind = true });

		Assert.AreEqual(10 + 60, result.XpGained);
		Assert.AreEqual(60, result.Notices.Single(n => n.Kind == NoticeKind.SkillShot).Xp);
	}

	private static List<string> Earned(CombatEvent combat) =>
		[.. TestHelpers.SkillShots(TestHelpers.Engine().ApplyCombat(TestHelpers.Player(), combat))];
}
